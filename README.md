# Trae 任务电台（Windows 客户端）

把「任务绑定层」接到 TraeCode CLI 的 Windows 桌面客户端：一个双击即用的单文件程序，管理 4 个任务槽位，按槽位收发任务并播报结果。

## 仓库内容

| 文件 | 说明 |
|---|---|
| `dist/TraeRadio.exe` | 主程序：WinForms 单文件，内嵌电台引擎 |
| `radio.ps1` | 电台引擎（槽位台账 / 新建 / 收发 / 播报，支持 `-Backend mock`） |
| `dist/TraeRadioGui.cs` | 界面源码（主窗口、槽位管理、登录引导、自检模式） |
| `dist/build.ps1` | 重建脚本（csc 免工具链编译，一条命令复现 exe） |
| `design_planA.md` | 设计方案与验证记录 |
| `install_traecli_outside_sandbox.md` | TraeCode CLI 安装 / 登录 / 真实 exec 步骤 |

## 运行前提（重要）

本程序**不打包** TraeCode CLI 本体（约 425MB），需使用者自行安装并登录：

1. Windows 10/11 x64，自带 .NET Framework 4.x（系统一般已预装）。
2. 安装 TraeCode CLI：
   ```powershell
   Invoke-WebRequest -Uri "https://trae.cn/trae-cli/install_v2.ps1" -OutFile "$env:TEMP\install_v2.ps1"
   powershell -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\install_v2.ps1"
   ```
3. 拥有 Trae 企业账号，首次启动时按界面引导登录（企业 SSO 浏览器登录，或企业 PAT）。

## 使用

双击 `dist/TraeRadio.exe` → 程序自动定位本机 traecli 并检测登录状态 → 未登录则弹出登录引导 → 登录后即可使用电台的槽位功能。

## 重建

```powershell
cd dist
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

## 说明

- 从网络下载的 exe 未做代码签名，Windows SmartScreen 可能提示，选择「更多信息 → 仍要运行」即可。
- 未在真实企业账号下做端到端验证；当前已验证的是登录通道生成与 mock 链路（见 `design_planA.md` 8.10 节）。