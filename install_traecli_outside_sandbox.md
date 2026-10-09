# 沙箱外重跑 TraeCode CLI 安装脚本 · 具体步骤

> 目标：在你自己的（非 agent 沙箱）PowerShell 里把 TraeCode CLI 完整装好、登录、跑通一次真实 `exec`，把 JSONL 事件流回填给我，用于校准第 8.5 节的 `started_marker` / `completion_marker`。

## 0. 为什么必须在沙箱外跑

当前 agent 沙箱有三处限制，安装只完成了一半：

| 现象 | 原因 | 影响 |
|---|---|---|
| `WARNING: could not update PATH: 拒绝访问。 (os error 5)` | 沙箱禁止改注册表 / PATH | `traecli` 命令不可直接调用 |
| `install_v2.ps1 : Exception calling "GetFullPath" ... The given path's format is not supported.` | 沙箱对路径做了重定向 | 安装流程中断 |
| `Error: Trae enterprise authentication required.` | 未登录（登录需要真人） | `exec` 无法执行 |

结果：二进制停在 `%LOCALAPPDATA%\TraeCLI\releases\.staging.0.207.1-windows-x86_64.<hash>\traex.exe`（424.7 MB），没有生成 `current\` 与 bin 入口；`--version` / `--help` 能跑，但没登录、没跑过真实任务。

## 1. 前置条件

- Windows 10 / 11 x64
- 普通用户权限即可（脚本写的是**用户级** PATH；若仍报「拒绝访问」，再用管理员重开 PowerShell）
- 网络能访问 `trae.cn` 与 `lf-cdn.trae.com.cn`

## 2. 清理沙箱留下的半成品（建议）

```powershell
# 只删半成品安装目录
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\TraeCLI" -ErrorAction SilentlyContinue
```

> `~/.trae` 先别删——如果里面有你其它 trae 配置就留着。本次沙箱用的是 `TRAE_HOME` 覆盖到别处，没污染默认目录。

## 3. 打开一个「你自己的」PowerShell

开始菜单 → 搜 `PowerShell` → 打开。**不要**用 agent 面板里的终端（那还是沙箱）。

## 4. 下载并执行安装脚本

```powershell
# 4.1 下载
Invoke-WebRequest -Uri "https://trae.cn/trae-cli/install_v2.ps1" -OutFile "$env:TEMP\install_v2.ps1"

# 4.2 执行
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\install_v2.ps1"
```

脚本行为：拉取 release metadata → 下载 `windows-x86_64` 版本 → 校验并解压 → 写入 PATH。

## 5. 验证安装

**关掉再重开一个 PowerShell**（让新 PATH 生效），然后：

```powershell
traecli --version
traecli doctor
```

若提示找不到 `traecli`，手动补 PATH：

```powershell
# 仅当前会话
$bin = "$env:LOCALAPPDATA\TraeCLI\current\bin"
$env:Path = "$bin;$env:Path"

# 永久写入（用户级）
[Environment]::SetEnvironmentVariable("Path", "$bin;" + [Environment]::GetEnvironmentVariable("Path","User"), "User")
```

## 6. 登录

**交互式（推荐先试这个）**

```powershell
traecli login
```

**非交互（ToB PAT）** —— 适合以后打包 exe 时注入

```powershell
$env:TRAECLI_PERSONAL_ACCESS_TOKEN = "<你的 PAT>"
traecli login --with-trae-pat

# 企业 API base url 需要时再加：
# traecli login --with-trae-pat --base-url "https://<你的企业 API 域名>"
```

确认登录态：

```powershell
traecli login status
traecli doctor          # Auth → status 应为 logged_in
```

## 7. 跑一次真实 exec（关键一步）

```powershell
cd $env:USERPROFILE\Desktop

traecli exec --json --skip-git-repo-check -o out.txt "say hello in one sentence" `
  1> exec_events.jsonl 2> exec_stderr.txt
```

- `exec_events.jsonl` —— stdout 的 JSONL 事件流（**完成真相源**，我要的就是它）
- `out.txt` —— 最终消息文本（下行摘要）
- `exec_stderr.txt` —— 错误输出

再补一条「续聊」验证（对应第 8.8 节第 1 项）：

```powershell
# 先看有哪些会话 / 拿到 session id
traecli resume --last
# 或直接对同一个 id 续聊，验证 exec resume 是否接受 --json 与 -o
traecli exec resume --last --json --skip-git-repo-check -o out2.txt "再说一句" 1> exec_resume_events.jsonl
```

## 8. 顺手确认一件事：daemon 默认到底启不启动

跑 `exec` 的时候，用**任务管理器**看一下有没有常驻的 `traex.exe` / `app-server` 进程（不在命令行进程列表里、跑完仍不退出）。

- 有 → 默认会探测/启动共享后台 server（`--no-daemon` 帮助文本支持这个说法）
- 没有 → 默认不启动（特性开关 `daemon_auto_start` = experimental / false 支持这个说法）

这两条证据目前互相拉扯，你这一眼就能定论，我把结论写回文档第 8.3 节。

## 9. 回填清单

把这两个文件发我即可：

- [ ] `exec_events.jsonl`
- [ ] `out.txt`
- [ ] （可选）`exec_resume_events.jsonl`

我要用它做两件事：

1. 校准 `HarnessSpec::trae()` 的 `started_marker` / `completion_marker`（现在还是 `TBD_STARTED` / `TBD_COMPLETED` 占位）
2. 确认 `exec resume <uuid> --json -o <file>` 的可用性（第 8.8 节 5 项待真机确认里的前 2 项）

## 附：一键版脚本（可选）

见同目录 `reinstall_traecli.ps1`，等价于上面第 2、4、5 步；登录与 exec 仍需你手动跑（涉及凭据与交互）。