# Trae 任务电台 v0.1.0

把「任务绑定层」从 Codex 换成 TraeCode CLI 的 Windows 桌面电台：管理 4 个任务槽位，按槽位收发任务并播报结果。**单文件、免安装、双击即用。**

## ✨ 亮点
- **单文件客户端** `TraeRadio.exe`：WinForms 界面 + 内嵌电台引擎 `radio.ps1`，不依赖 .NET SDK / Rust 工具链。
- **免工具链构建**：用系统自带 `csc.exe` 编译，`dist/build.ps1` 一条命令即可复现。
- **双通道登录引导**：企业 SSO（浏览器）/ 企业 PAT（非交互令牌）；未登录时自动弹出引导。
- **mock 后端**：离线跑通「台账 / 建会话 / 收发 / 播报」全链路，不调用真实 CLI。
- **中文编码修复**：子进程强制 UTF-8 输出 + 编译加 `/codepage:65001`。

## 📦 下载
- `TraeRadio.exe` —— 见本页 Assets 附件，下载后双击运行即可。

## ⚙️ 使用前提（重要）
1. Windows 10/11 x64（系统自带 .NET Framework 4.x）。
2. 自行安装 TraeCode CLI（本程序**不打包**其约 425MB 本体）：

   ```powershell
   Invoke-WebRequest -Uri "https://trae.cn/trae-cli/install_v2.ps1" -OutFile "$env:TEMP\install_v2.ps1"
   powershell -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\install_v2.ps1"
   ```

3. 拥有 Trae 企业账号，首次启动按界面引导登录。

## ⚠️ 已知限制
- exe 未做代码签名，SmartScreen 可能提示，选「更多信息 → 仍要运行」。
- 未在真实企业账号下做端到端验证；当前已验证「登录通道生成 + mock 链路」。
- 仅支持 Windows x64；本版为纯软件电台，不含 EasyInput 硬件键盘链路。

## 📄 许可证与来源
本项目是 zuming58 [`Esayinput-zm-L7`](https://github.com/zuming58/Esayinput-zm-L7) 的 **Trae 移植版**，沿用 **Apache License 2.0**；上游署名与修改说明见仓库 `NOTICE`。