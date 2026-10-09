# 构建 TraeRadio.exe（免安装工具链，用系统自带 .NET Framework csc.exe）
# 用法： powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
$ErrorActionPreference = "Stop"

$here = $PSScriptRoot
$root = Split-Path -Parent $here

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 csc.exe（需要 .NET Framework 4.x）" }

$src   = Join-Path $here "TraeRadioGui.cs"
$radio = Join-Path $root "radio.ps1"
$out   = Join-Path $here "TraeRadio.exe"

# 内嵌电台引擎：不带名字时，资源名默认为文件名 "radio.ps1"，与 TraeRadioGui.cs 的读取一致
$cscArgs = @(
    "/nologo", "/target:winexe", "/optimize+", "/utf8output", "/codepage:65001",
    "/out:$out",
    "/r:System.dll", "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll",
    "/resource:$radio",
    $src
)
& $csc @cscArgs

if ($LASTEXITCODE -ne 0) { throw "csc 编译失败，退出码 $LASTEXITCODE" }
Write-Host "已生成：$out  ($((Get-Item -LiteralPath $out).Length) 字节)"