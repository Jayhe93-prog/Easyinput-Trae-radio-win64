# reinstall_traecli.ps1
# 在「非沙箱」的普通 PowerShell 里运行，把 TraeCode CLI 完整装好。
# 用法： powershell -NoProfile -ExecutionPolicy Bypass -File .\reinstall_traecli.ps1
# 说明：只做「清理 + 下载 + 安装 + 验证」。登录与 exec 需你手动跑（涉及凭据与交互）。

$ErrorActionPreference = "Stop"

Write-Host "==> 1/5 清理沙箱留下的半成品安装目录" -ForegroundColor Cyan
$cliHome = Join-Path $env:LOCALAPPDATA "TraeCLI"
if (Test-Path -LiteralPath $cliHome) {
    Remove-Item -Recurse -Force -LiteralPath $cliHome
    Write-Host "    已删除 $cliHome"
} else {
    Write-Host "    $cliHome 不存在，跳过"
}

Write-Host "==> 2/5 下载安装脚本" -ForegroundColor Cyan
$scriptPath = Join-Path $env:TEMP "install_v2.ps1"
Invoke-WebRequest -Uri "https://trae.cn/trae-cli/install_v2.ps1" -OutFile $scriptPath
Write-Host "    已保存到 $scriptPath ($((Get-Item -LiteralPath $scriptPath).Length) B)"

Write-Host "==> 3/5 执行安装（首次下载约 424.7 MB，请耐心等待）" -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath
if ($LASTEXITCODE -ne 0) {
    Write-Warning "安装脚本退出码 $LASTEXITCODE —— 若报 '拒绝访问'，请用管理员身份重开 PowerShell 再跑一次。"
}

Write-Host "==> 4/5 校验产物" -ForegroundColor Cyan
$exe = Join-Path $cliHome "current\traex.exe"
if (Test-Path -LiteralPath $exe) {
    Write-Host "    OK: $exe" -ForegroundColor Green
} else {
    $staging = Get-ChildItem -LiteralPath (Join-Path $cliHome "releases") -Directory -Filter ".staging.*" -ErrorAction SilentlyContinue
    if ($staging) {
        Write-Warning "仍停在 staging：$($staging[0].FullName)"
        Write-Warning "说明安装未完成提升（current 未生成），请检查上一步的报错。"
    } else {
        Write-Warning "未找到 traex.exe，请检查 $cliHome"
    }
}

Write-Host "==> 5/5 补 PATH（用户级）并验证" -ForegroundColor Cyan
$bin = Join-Path $cliHome "current\bin"
if (Test-Path -LiteralPath $bin) {
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($userPath -notlike "*$bin*") {
        [Environment]::SetEnvironmentVariable("Path", "$bin;$userPath", "User")
        Write-Host "    已把 $bin 写入用户级 PATH（新开终端生效）"
    } else {
        Write-Host "    $bin 已在用户级 PATH 中"
    }
    $env:Path = "$bin;$env:Path"
    & (Join-Path $bin "traecli.exe") --version
} else {
    Write-Warning "未找到 $bin，PATH 未改动"
}

Write-Host ""
Write-Host "完成。下一步请手动执行：" -ForegroundColor Yellow
Write-Host "  traecli login              # 或  traecli login --with-trae-pat"
Write-Host "  traecli doctor             # Auth status 应为 logged_in"
Write-Host "  traecli exec --json --skip-git-repo-check -o out.txt `"say hello`" 1> exec_events.jsonl"