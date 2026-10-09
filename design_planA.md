# 方案 A：Windows 版 Trae 任务电台 — 设计文档

> 基线：zuming58 的 Windows 版 Codex 任务电台（`Esayinput-zm-L7`）
> 目标：把「任务绑定层」从 Codex 换成 Trae，其余全部复用
> 状态：验证已完成，设计已成形；argv 已按 `--help` 实测校正，标注了 4 项待真机确认（另 1 项已销）

---

## 0. 本轮结论

1. **原生 Windows TraeCode CLI 独立可用，不依赖 WSL2。** 实测 `traex.exe --version` → `traecli 0.207.1 (public edition)`，退出码 0。
2. **TraeCode CLI 是 Codex CLI 的分支。** 证据：`exec --help` 里残留 `Do not load $CODEX_HOME/config.toml; auth still uses CODEX_HOME`；命令面（`exec` / `resume` / `--json` / `--output-last-message`）与 Codex CLI 同构；`traecli features list` 里还挂着成排的 Codex 血统特性名：`codex_git_commit`、`apply_patch_freeform`、`apply_patch_streaming_events`、`responses_websockets`、`use_legacy_landlock`、`use_linux_sandbox_bwrap`。分支结论从「一行残留」升级为「整套特性面同源」。**这意味着现成的 `CodexRunner` 骨架基本可以直接沿用，只换程序名与标记。**
3. **接口比官方文档丰富得多。** 除了文档写的 `-p/--print`、`--json`、`--resume`、`--session-id`，实际还有 `exec` 子命令、`dashboard`、`app-server`、`remote-control`、`acp`、`mcp-server`、`archive/delete/unarchive`、`--output-schema`、`--output-last-message`。方案 A 的稳定性因此再上一档。

---

## 1. 验证记录（可复现）

### 1.1 环境
| 项 | 值 |
|---|---|
| 系统架构 | AMD64（`windows-x86_64`） |
| 安装脚本 | `https://trae.cn/trae-cli/install_v2.ps1`（52007 B，已下载审阅） |
| 下载源 | `https://lf-cdn.trae.com.cn/obj/trae-com-cn/trae-cli` |
| 实测版本 | **traecli 0.207.1 (public edition)** |
| 二进制 | `traex.exe`，424.7 MB（自带运行时） |
| 状态目录 | `%LOCALAPPDATA%\TraeCLI\`（`releases/`、`current/`、`install.lock`） |
| 目标 bin | `%LOCALAPPDATA%\Programs\TraeCLI\bin` |
| 配置文件 | `~/.trae/traecli.toml`（可用 `TRAE_HOME` 覆盖） |

### 1.2 判定「是否依赖 WSL2」
| 证据 | 结论 |
|---|---|
| 安装脚本全文检索 `wsl|WSL` → **0 命中** | 安装过程不需要 WSL |
| 平台映射：`AMD64 → windows-x86_64`、`ARM64 → windows-aarch64` | 下载的是**原生 Windows 二进制**，非 Linux 包 |
| 脚本第 34 行 `OSVersion.Platform -ne Win32NT` 直接拒绝非 Windows | 脚本本身是 Windows 专用 |
| `traex.exe --version` / `--help` 均 exit 0 | **原生二进制可在 Windows 直接运行** |
| 官方文档措辞 | 「建议优先使用 WSL2；如使用 Windows 原生版本，首次启动时按界面提示完成沙箱初始化」——**是建议，不是依赖** |

**结论：原生版独立可用；WSL2 只是官方推荐，不是运行前提。**

### 1.3 未完成项（如实标注）
- **PATH 未写入**：`WARNING: proceeding, even though we could not update PATH: 拒绝访问 (os error 5)` —— 被本机 agent 沙箱拒绝，非 CLI 缺陷。
- **staging 未提升**：文件停在 `releases\.staging.0.207.1-...\`，未生成 `current\` 与 `bin\traecli.cmd` 入口。
- **未登录、未跑真实 `exec`**：本机 agent 沙箱禁止访问 `C:\Users\ASUS\.trae`，而 CLI 运行期需要该目录；**真实 exec 必须在沙箱外执行**。
- **`exec` 卡在企业鉴权门禁**：未登录时 `traecli exec` 直接返回 `Error: Trae enterprise authentication required. Run 'traecli login' before using TraeCode CLI.` —— 即 `exec` 不是匿名可用；`traecli doctor` 显示会读取 `TRAE_ACCESS_TOKEN`（当前 unset），提示可用环境变量做免交互登录（待确认）。
- 因此本轮验证的是「**原生二进制可独立运行**」，**不是**「已完成一次真实任务对话」。

---

## 2. 关键接口发现（超出官方文档）

| 命令 / 参数 | 作用 | 在电台里的用途 |
|---|---|---|
| `traecli exec [PROMPT]` | 非交互运行（`exec` 即非交互模式） | **上行主入口** |
| `traecli exec resume <ID>` | 恢复指定会话（`--last` 取最近） | **槽位绑定 + 续聊** |
| `traecli exec fork <ID>` | 从旧会话派生新会话 | 换任务不污染历史 |
| `--json` | **以 JSONL 打印事件到 stdout** | **完成真相源（事件流）** |
| `-o, --output-last-message <FILE>` | 把 agent 最后一条消息写入文件 | **下行摘要文本** |
| `--output-schema <FILE>` | 约束最终回复的 JSON 结构 | 结构化摘要 |
| `traecli resume <SESSION_ID> [PROMPT]` | 会话寻址；**UUID 或线程名，UUID 优先** | 台账键设计依据 |
| `traecli dashboard` | 在共享 app-server daemon 上浏览会话与活动 | 会话枚举（补上「CLI 无 list 命令」的缺口） |
| `traecli app-server` / `remote-control` | 共享后台服务 / 无头服务 | **常驻 daemon**，免每次起 424MB 进程 |
| `--remote ws://host:port` + `--remote-auth-token-env` | 通过 websocket + Bearer token 连远程服务 | **官方带鉴权的控制通道**（替代 Codex 私有 IPC） |
| `traecli acp` / `--acp` | 以 ACP（Agent Client Protocol）服务运行 | 标准化接入（备选） |
| `traecli mcp-server` | 以 MCP server（stdio）运行 | 备选接入 |
| `traecli archive / delete / unarchive <ID>` | 会话生命周期 | 槽位清理 |
| `--permission-mode`（default / bypass_permissions / auto） | 权限预设 | 无人值守策略 |
| `-s, --sandbox`（read-only / workspace-write / danger-full-access） | 沙箱策略 | 安全边界 |
| `-a, --ask-for-approval`（on-request / never） | 审批策略（**仅交互式 `traecli` / `traecli resume` 有；`exec` 无此参数**） | 无人值守走 `exec`，用不到它 |
| `--allowed-tool` / `--disallowed-tool` | 工具白/黑名单 | 收敛可执行面 |
| `--shell-tool-timeout` / `--query-timeout` | 超时强杀 | 对应 Host 的 `timeout` |
| `--ephemeral` | 不落盘会话文件 | 临时任务 |
| `--skip-git-repo-check` | 允许在非 git 目录运行 | 电台工作目录不受限 |
| `--dangerously-bypass-hook-trust` | 免 Hook 信任确认 | 自动化装 Hook 用 |
| `/name` 前缀（prompt 内） | 调用已启用的 skill / 自定义命令 | **一句话触发技能**，电台的天然玩法 |

---

## 3. 架构：只换一层

```
EasyInput V2 键盘（ESP32-S3）
   │  上行：按住说  ── 音频 ──┐
   │  下行：按一下听 ←─ 音频 ─┤
   └──────── USB（优先）/ Wi-Fi（回退）────────┘
                      │
              Host 运行时（Windows x64 桌面 exe）
              ├── 协议层（EIAU/EIPD 线格式、IMA-ADPCM、AES-GCM）   ← 复用
              ├── 音频层（48kHz PCM、播放回执）                      ← 复用
              ├── 语音层（ASR / TTS，双供应商）                      ← 复用
              ├── 状态层（灯光语义、未读计数、凭据管理器）           ← 复用
              └── ★ 任务绑定层（HarnessSpec）  ← ← ← 唯一替换点
                      │
                 traecli exec resume <uuid> --json -o <file>
                      │
              TraeCode（本地会话 / 共享 app-server daemon）
```

**替换点收敛在一处**：`HarnessSpec`。UI、灯光、音频、USB/Wi-Fi、ASR/TTS 全部不动。

---

## 4. HarnessSpec::trae()：从占位到实契约

```rust
pub fn trae(program: PathBuf) -> Self {
    Self {
        name: "trae",
        program,                       // traecli（或 traex.exe 绝对路径）
        // 上行：把一句话送进指定会话；prompt 走 stdin（"-"）避免转义与长度问题
        resume_args: vec![
            "exec".into(),
            "resume".into(), "{task_id}".into(),
            "--json".into(),                    // JSONL 事件流
            "-o".into(), "{out_file}".into(),   // 最终消息落盘
            "--skip-git-repo-check".into(),
            "-".into(),
        ],
        // ⚠ 以下两个标记名必须用一次真实 --json 输出校准，当前为占位
        started_marker: "TBD_STARTED",      // 待真实样本确认
        completion_marker: "TBD_COMPLETED", // 待真实样本确认
    }
}
```

> 与 Codex 版的差别：Codex 用 `exec resume --json <id> -`；Trae 用 `exec resume <id> --json -o <file> -`。
> **形状几乎一致**，因为 TraeCode CLI 派生自 Codex CLI。

> ⚠ **argv 实测修正**（`traecli exec --help` / `traecli exec resume --help`）：
> `exec resume` **有** `--json`、`-o`、`--skip-git-repo-check`、`--last`、`--permission-mode`、`-y`、`--allowed-tool`、`--shell-tool-timeout`；
> 但**没有** `-s/--sandbox`、`-C/--cd`、`--session-id`、`-a/--ask-for-approval`。
> `exec`（新建会话）才有 `-s/--sandbox`、`-C/--cd`、`--session-id`。
> 所以「无人值守」在 resume 路径上只能靠 `--permission-mode`（或 `-y`）；沙箱策略要写进配置，不能当命令行参数传。

---

## 5. 会话台账（4 槽 ↔ 4 会话）

- 键：**session UUID**（`resume` 明确「UUID 优先于线程名」）。
- 槽位映射：`slot[1..4] -> uuid`，持久化到本地 SQLite（沿用 zuming58 的本地库 + 每槽 FIFO）。
- 新建会话：首次 `exec --session-id <uuid>`（legacy flag，可命名）或 `exec` 后回写生成的 id。
- 会话清理：`archive` / `delete`。
- **补强**：`dashboard` 提供「会话 + 活动」视图，可作为台账的校验来源（解决 CLI 无 list 命令的担忧）。

---

## 6. 完成真相源（三级，fail closed）

| 级别 | 来源 | 说明 |
|---|---|---|
| L1（主） | `--json` 的 JSONL 终态事件 | 事件流里最后一条表示本回合结束 |
| L2（辅） | `-o` 文件落盘 | 最终消息文本，供摘要使用 |
| L3（兜底） | Hook `Stop` 事件 | 智能体停止执行时触发，写完成标记文件 |

**fail closed**：三级都没拿到「完成」，就不点绿灯、不置未读——宁可漏报，不可误报。

---

## 7. 常驻 daemon（性能）

`traecli features list` 实测：`daemon_auto_start` = **experimental / false** —— 共享后台 daemon **默认不会自动启动**。`--no-daemon` 是显式关闭开关（存在于 `--help`），说明该能力存在但默认关。电台要用常驻 daemon，需**显式启用该特性或手动起 `app-server`**：
- 手动常驻一个 `app-server` 并让任务走它，按键时不再冷启 424 MB 进程；
- 需要跨机/无头时用 `remote-control` + `--remote ws://host:port` + `--remote-auth-token-env`。

这是相对 Codex 版的实质升级：**控制通道是官方带 Bearer token 的 websocket，不是逆向出来的私有 IPC。**

---

## 8. exe 打包方案

| 项 | 方案 |
|---|---|
| 框架 | Tauri + WebView2（与 zuming58 同构，体积小） |
| CLI 依赖 | 二选一：①要求用户自行安装（同 zuming58 要求装 Codex）；②作为 sidecar 打包（**424.7 MB，需评估安装包体积与再分发许可**） |
| 鉴权 | PAT（`TRAECLI_PERSONAL_ACCESS_TOKEN` + `traecli login --with-trae-pat`）存入 **Windows 凭据管理器**，运行时注入 |
| 定位 CLI | 优先 `%LOCALAPPDATA%\Programs\TraeCLI\bin`，其次 PATH，允许用户在设置里指定 |
| 前置检查 | 启动时 `traecli --version` 探活 + `login status` 校验，缺一给明确指引 |

---

## 9. 待真机确认（4 项待定 + 1 项已销）

1. ~~`exec resume <uuid>` 是否接受 `--json` 与 `-o`~~ —— **已确认：接受**（实测 `traecli exec resume --help`）。
2. **`--json` JSONL 里「开始 / 结束」事件的真实字段名** —— 必须用一次真实运行输出校准 `started_marker` / `completion_marker`。
3. PAT 登录是否足以跑 `exec`（还是必须 TUI 登录一次）。
4. 原生版「沙箱初始化」是否会在无人值守下弹交互（影响自动化）。
5. Hook `Stop` 在 `exec`（非交互）模式下是否触发。

> 这 5 项都要一次**沙箱外的真实运行**才能定。本机 agent 沙箱禁止访问 `~/.trae`，所以这一步需要你在沙箱外执行，或把输出样本给我。

---

## 10. 与 Codex 版对照

| 维度 | Codex 版（zuming58） | Trae 版（方案 A） |
|---|---|---|
| 任务绑定 | 桌面版内部 IPC（未公开） | `traecli exec resume`（官方） |
| 会话枚举 | IPC | `dashboard` + 自建台账 |
| 完成真相源 | Codex 精确轮次 | `--json` 事件 + `-o` + Hook `Stop` |
| 控制通道 | 私有 IPC，随版本失效 | 官方 websocket + Bearer token |
| 上游同源 | Codex CLI | **TraeCode CLI 派生自 Codex CLI** |
| 版本脆弱性 | 高（README 自述） | 低（公开契约） |
| 改动范围 | 重写 Host 运行时 | **只换 HarnessSpec 一层** |