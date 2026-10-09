# Trae 任务电台 · 最小可用原型
# Windows / PowerShell 5.1 · 零编译依赖
#
#   .\radio.ps1 status                     查看 4 个槽位台账
#   .\radio.ps1 new 1 [工作目录]            给槽位开一个新会话（会真跑一次极短 prompt 取 session id）
#   .\radio.ps1 bind 1 <sessionId>         把槽位绑到已有会话
#   .\radio.ps1 say 1 "帮我看看这个报错"     发送并取回结果（加 -Speak 播报）
#   .\radio.ps1 hear 1                     重播槽位最近一次结果（TTS）
#   .\radio.ps1 log 1 [行数]               看槽位最近一次的原始 JSONL 事件
#   .\radio.ps1 interactive                交互模式（选槽位 → 说话 → 听结果）
#   .\radio.ps1 help
#
# 全局可选参数：-Cli <traecli 路径>   -WorkDir <默认工作目录>   -Speak
#
# 范围与边界（v0.1 原型）
#   · 只打通「一句话 → traecli 会话 → 取回结果」这条链路；输入是打字。
#     语音/ASR 与硬件键盘（EasyInput V2）是下一步，接进来只需替换 Send-Prompt 的输入来源。
#   · 完成判定 fail closed：进程退出码 + `-o` 落盘非空；JSONL 事件标记待真机校准后
#     填进 $HARNESS.completion_marker（现在是 TBD_COMPLETED，即不参与判定）。
#   · 4 个槽位 = 4 个 traecli 会话，台账落 %LOCALAPPDATA%\TraeRadio\ledger.json。
#   · 换 harness 只改 $HARNESS 那一块 —— 对应设计文档第 8.5 节的 HarnessSpec。

param(
    [Parameter(Position = 0)][string]$Command = "",
    [Parameter(Position = 1)][string]$Slot = "",
    [Parameter(Position = 2)][string]$Text = "",
    [string]$WorkDir = "",
    [string]$Cli = "",
    [ValidateSet("trae", "mock")][string]$Backend = "trae",
    [switch]$Speak
)

$ErrorActionPreference = "Stop"

# 被 GUI/父进程以重定向方式拉起时，PS 5.1 默认按系统 OEM 代码页(如 GBK)输出，
# 会让中文变成乱码。这里强制 UTF-8，父进程按 UTF-8 解码即可（与系统区域无关）。
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

# ── HarnessSpec 等价物：换 harness 只改这一块 ───────────────────────────────
$HARNESS = @{
    program           = "traecli"
    new_args          = @("exec", "--json", "--skip-git-repo-check", "-o", "{out}", "-")
    new_sandbox       = @("-s", "workspace-write")
    resume_args       = @("exec", "resume", "{session}", "--json", "--skip-git-repo-check", "-o", "{out}", "-")
    started_marker    = "TBD_STARTED"
    completion_marker = "TBD_COMPLETED"
}

# mock 后端：不调真实 CLI，用于离线验证电台链路（台账 / 绑定 / 收发 / 播报）
if ($Backend -eq "mock") { $HARNESS.program = "mock" }

$StateDir   = if ($Backend -eq "mock") { Join-Path $env:LOCALAPPDATA "TraeRadio-mock" } else { Join-Path $env:LOCALAPPDATA "TraeRadio" }
$LedgerPath = Join-Path $StateDir "ledger.json"
$RunDir     = Join-Path $StateDir "runs"
New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
New-Item -ItemType Directory -Force -Path $RunDir   | Out-Null

function Get-Ledger {
    $fresh = @{ cli = $HARNESS.program; workdir = (Get-Location).Path; slots = @{} }
    if (-not (Test-Path -LiteralPath $LedgerPath)) { return $fresh }
    $raw = Get-Content -LiteralPath $LedgerPath -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) { return $fresh }
    $o = $raw | ConvertFrom-Json
    $slots = @{}
    if ($o.slots) {
        foreach ($p in $o.slots.PSObject.Properties) {
            $slots[$p.Name] = @{
                session    = $p.Value.session
                workdir    = $p.Value.workdir
                last_out   = $p.Value.last_out
                last_jsonl = $p.Value.last_jsonl
                last_at    = $p.Value.last_at
                last_ok    = [bool]$p.Value.last_ok
            }
        }
    }
    return @{
        cli     = $(if ($o.cli) { $o.cli } else { $HARNESS.program })
        workdir = $(if ($o.workdir) { $o.workdir } else { (Get-Location).Path })
        slots   = $slots
    }
}

function Save-Ledger($led) {
    ($led | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $LedgerPath -Encoding UTF8
}

function Get-Cli($led) {
    if ($Cli) { return $Cli }
    if ($HARNESS.program -eq "mock") { return "mock" }
    if ($led.cli) { return $led.cli }
    return $HARNESS.program
}

function Expand-Args([string[]]$Template, [hashtable]$Map) {
    return @($Template | ForEach-Object {
        $s = $_
        foreach ($k in $Map.Keys) { $s = $s.Replace("{$k}", [string]$Map[$k]) }
        $s
    })
}

function New-RunPaths([string]$slot) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
    $base  = Join-Path $RunDir "slot$slot-$stamp"
    return @{
        out   = "$base.out.txt"
        jsonl = "$base.events.jsonl"
        err   = "$base.stderr.txt"
        base  = $base
    }
}

function Invoke-Mock {
    param([string[]]$Argv, [string]$Prompt, [hashtable]$Paths)
    $idx = [Array]::IndexOf($Argv, "resume")
    $isResume = ($idx -ge 0)
    $sid = if ($isResume -and ($idx + 1) -lt $Argv.Count) { $Argv[$idx + 1] } else { [guid]::NewGuid().ToString() }
    $reply = if ($isResume) {
        "[mock] 槽位会话 $($sid.Substring(0, 8)) 收到：「$Prompt」`n这是模拟回复，用于验证电台链路；未调用真实 traecli。"
    } else { "ready" }
    $events = @(
        (@{ type = "session.created"; session_id = $sid } | ConvertTo-Json -Compress),
        (@{ type = "agent.message"; text = $reply } | ConvertTo-Json -Compress),
        (@{ type = "task.completed"; session_id = $sid } | ConvertTo-Json -Compress)
    ) -join "`n"
    Start-Sleep -Milliseconds 250
    $events | Set-Content -LiteralPath $Paths.jsonl -Encoding UTF8
    $reply  | Set-Content -LiteralPath $Paths.out  -Encoding UTF8
    return @{ code = 0; result = $reply; jsonl = $events }
}

function Invoke-Trae {
    param([string]$CliPath, [string[]]$Argv, [string]$Prompt, [string]$Cwd, [hashtable]$Paths)

    if ($CliPath -eq "mock") { return Invoke-Mock -Argv $Argv -Prompt $Prompt -Paths $Paths }

    if (-not (Get-Command $CliPath -ErrorAction SilentlyContinue)) {
        throw "找不到 traecli：$CliPath（用 -Cli <完整路径> 指定，或先把它加进 PATH）"
    }

    # PS 5.1 会把原生程序的 stderr 当成终止性错误，这里临时降级以免误伤
    $savedEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    Push-Location $Cwd
    try {
        $stdout = $Prompt | & $CliPath @Argv 2> $Paths.err
        $code   = $LASTEXITCODE
    } finally {
        Pop-Location
        $ErrorActionPreference = $savedEap
    }
    $joined = ($stdout -join "`n")
    $joined | Set-Content -LiteralPath $Paths.jsonl -Encoding UTF8
    $result = ""
    if (Test-Path -LiteralPath $Paths.out) {
        $result = (Get-Content -LiteralPath $Paths.out -Raw -Encoding UTF8).Trim()
    }
    return @{ code = $code; result = $result; jsonl = $joined }
}

function Find-SessionId([string]$jsonl) {
    $uuid = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
    foreach ($line in ($jsonl -split "`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $o = $line | ConvertFrom-Json } catch { continue }
        foreach ($name in @('session_id', 'sessionId', 'thread_id', 'id')) {
            $v = $o.$name
            if ($v -and ($v -match "^$uuid$")) { return $v }
        }
    }
    $m = [regex]::Match($jsonl, $uuid)
    if ($m.Success) { return $m.Value }
    return $null
}

function Speak-Text([string]$text) {
    $clean = $text -replace '(?s)```.*?```', '（代码块）'
    $clean = $clean -replace '`([^`]*)`', '$1'
    $clean = $clean -replace '\[([^\]]*)\]\([^)]*\)', '$1'
    $clean = $clean -replace '(?m)^\s*[-*#>]+\s*', ''
    $clean = $clean -replace '\s+', ' '
    if ($clean.Length -gt 600) { $clean = $clean.Substring(0, 600) + "……（已截断）" }
    try {
        Add-Type -AssemblyName System.Speech
        $sp = New-Object System.Speech.Synthesis.SpeechSynthesizer
        $sp.Speak($clean)
    } catch {
        Write-Warning "TTS 不可用：$($_.Exception.Message)"
    }
}

function Show-Status($led) {
    Write-Host "Trae 任务电台 · 槽位台账" -ForegroundColor Cyan
    Write-Host ("  cli            : {0}" -f (Get-Cli $led))
    Write-Host ("  默认工作目录    : {0}" -f $led.workdir)
    Write-Host ("  台账文件        : {0}" -f $LedgerPath)
    Write-Host ""
    for ($i = 1; $i -le 4; $i++) {
        $e = $led.slots["$i"]
        if ($e) {
            $mark  = $(if ($e.last_ok) { "OK " } else { "-- " })
            $short = $(if ($e.session) { $e.session.Substring(0, [Math]::Min(8, $e.session.Length)) } else { "(无)" })
            Write-Host ("  [{0}] {1} session {2}…  {3}  {4}" -f $i, $mark, $short, $e.last_at, $e.workdir)
        } else {
            Write-Host ("  [{0}] (空)  用 .\radio.ps1 new {0} 建会话" -f $i) -ForegroundColor DarkGray
        }
    }
}

function New-Session($led, [string]$slot, [string]$workdir) {
    $cli = Get-Cli $led
    $cwd = $workdir
    if (-not $cwd) { $cwd = $WorkDir }
    if (-not $cwd) { $cwd = $led.workdir }
    if (-not (Test-Path -LiteralPath $cwd)) { New-Item -ItemType Directory -Force -Path $cwd | Out-Null }

    $p    = New-RunPaths $slot
    $argv = Expand-Args ($HARNESS.new_args + $HARNESS.new_sandbox) @{ out = $p.out }

    Write-Host "→ 槽位 $slot 新建会话（工作目录 $cwd）…" -ForegroundColor Cyan
    $r = Invoke-Trae -CliPath $cli -Argv $argv -Prompt "Reply with exactly: ready" -Cwd $cwd -Paths $p

    $sid = Find-SessionId $r.jsonl
    if (-not $sid) {
        Write-Warning "没能从事件流识别出 session id。请手动绑定：.\radio.ps1 bind $slot <uuid>"
        Write-Host "原始事件：$($p.jsonl)"
        Write-Host "stderr  ：$($p.err)"
        return
    }
    $led.slots[$slot] = @{
        session    = $sid
        workdir    = $cwd
        last_out   = $p.out
        last_jsonl = $p.jsonl
        last_at    = (Get-Date).ToString("s")
        last_ok    = [bool](($r.code -eq 0) -and $r.result)
    }
    Save-Ledger $led
    Write-Host "✓ 槽位 $slot → $sid" -ForegroundColor Green
}

function Bind-Slot($led, [string]$slot, [string]$sid) {
    if (-not $sid) { throw "用法：.\radio.ps1 bind <槽位 1-4> <sessionId>" }
    $old = $led.slots[$slot]
    $wd  = $(if ($old -and $old.workdir) { $old.workdir } else { $led.workdir })
    $led.slots[$slot] = @{
        session    = $sid
        workdir    = $wd
        last_out   = $(if ($old) { $old.last_out } else { $null })
        last_jsonl = $(if ($old) { $old.last_jsonl } else { $null })
        last_at    = $(if ($old) { $old.last_at } else { $null })
        last_ok    = [bool]$(if ($old) { $old.last_ok } else { $false })
    }
    Save-Ledger $led
    Write-Host "✓ 槽位 $slot → $sid" -ForegroundColor Green
}

function Send-Prompt($led, [string]$slot, [string]$prompt) {
    $e = $led.slots[$slot]
    if (-not $e) { throw "槽位 $slot 未绑定会话。先执行：.\radio.ps1 new $slot" }
    if (-not $e.session) { throw "槽位 $slot 没有 session id。先执行：.\radio.ps1 new $slot" }

    $cli = Get-Cli $led
    $cwd = $(if ($e.workdir) { $e.workdir } else { $led.workdir })
    if (-not (Test-Path -LiteralPath $cwd)) { $cwd = (Get-Location).Path }

    $p    = New-RunPaths $slot
    $argv = Expand-Args $HARNESS.resume_args @{ session = $e.session; out = $p.out }

    Write-Host ("→ 槽位 {0}  会话 {1}…  cwd {2}" -f $slot, $e.session.Substring(0, 8), $cwd) -ForegroundColor Cyan
    $r = Invoke-Trae -CliPath $cli -Argv $argv -Prompt $prompt -Cwd $cwd -Paths $p

    # 完成真相源：L1 事件标记（校准后）→ L2 -o 落盘 → 进程退出码；三级都不满足则 fail closed
    $ok = ($r.code -eq 0) -and (-not [string]::IsNullOrWhiteSpace($r.result))
    if ($ok -and ($HARNESS.completion_marker -notlike "TBD_*")) {
        $ok = $r.jsonl.Contains($HARNESS.completion_marker)
    }

    $e.last_out   = $p.out
    $e.last_jsonl = $p.jsonl
    $e.last_at    = (Get-Date).ToString("s")
    $e.last_ok    = $ok
    Save-Ledger $led

    if ($ok) {
        Write-Host "✓ 完成" -ForegroundColor Green
        Write-Host $r.result
        if ($Speak) { Speak-Text $r.result }
    } else {
        Write-Warning "✗ 未拿到完成结果（exit=$($r.code)）"
        if ($r.code -ne 0) {
            $err = Get-Content -LiteralPath $p.err -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
            if ($err) { Write-Host $err.Trim() -ForegroundColor DarkYellow }
        }
        Write-Host "原始事件：$($p.jsonl)" -ForegroundColor DarkGray
    }
}

function Hear-Slot($led, [string]$slot) {
    $e = $led.slots[$slot]
    if (-not $e -or -not $e.last_out -or -not (Test-Path -LiteralPath $e.last_out)) {
        Write-Warning "槽位 $slot 还没有可播报的结果"
        return
    }
    $t = (Get-Content -LiteralPath $e.last_out -Raw -Encoding UTF8).Trim()
    Write-Host $t
    Speak-Text $t
}

function Show-Log($led, [string]$slot, [string]$n) {
    $e = $led.slots[$slot]
    if (-not $e -or -not $e.last_jsonl -or -not (Test-Path -LiteralPath $e.last_jsonl)) {
        Write-Warning "槽位 $slot 还没有运行记录"
        return
    }
    $k = $(if ($n -match '^\d+$') { [int]$n } else { 20 })
    Write-Host ("── {0}（末尾 {1} 行）" -f $e.last_jsonl, $k) -ForegroundColor Cyan
    Get-Content -LiteralPath $e.last_jsonl | Select-Object -Last $k
}

function Show-Usage {
    Write-Host "Trae 任务电台 · 最小可用原型" -ForegroundColor Cyan
    Write-Host @"
  .\radio.ps1 status                  查看槽位台账
  .\radio.ps1 new <槽位> [工作目录]     新建会话并绑定槽位
  .\radio.ps1 bind <槽位> <sessionId>  绑定已有会话
  .\radio.ps1 say <槽位> `"文本`"       发送并取回结果
  .\radio.ps1 hear <槽位>              重播最近结果（TTS）
  .\radio.ps1 log <槽位> [行数]        查看原始 JSONL 事件
  .\radio.ps1 interactive             交互模式
  .\radio.ps1 help                    本帮助

可选参数：-Speak（播报）  -Cli <路径>  -WorkDir <目录>  -Backend <trae|mock>
"@
}

function Start-Interactive($led) {
    Write-Host "交互模式：输入 1-4 选槽位说话；h 帮助；q 退出。" -ForegroundColor Cyan
    while ($true) {
        Show-Status $led
        $slot = (Read-Host "槽位 [1-4]").Trim()
        if ($slot -eq 'q') { break }
        if ($slot -eq 'h' -or $slot -eq '') { continue }
        if ($slot -notmatch '^[1-4]$') { Write-Warning "只支持 1-4"; continue }
        $say = (Read-Host "对槽位 $slot 说").Trim()
        if ([string]::IsNullOrWhiteSpace($say)) { continue }
        try { Send-Prompt $led $slot $say } catch { Write-Warning $_.Exception.Message }
    }
}

$led = Get-Ledger
switch ($Command.ToLower()) {
    ""            { Start-Interactive $led }
    "interactive" { Start-Interactive $led }
    "status"      { Show-Status $led }
    "new"         { if (-not $Slot) { throw "用法：.\radio.ps1 new <槽位 1-4> [工作目录]" }; New-Session $led $Slot $Text }
    "bind"        { if (-not $Slot) { throw "用法：.\radio.ps1 bind <槽位 1-4> <sessionId>" }; Bind-Slot $led $Slot $Text }
    "say"         { if (-not $Slot -or -not $Text) { throw "用法：.\radio.ps1 say <槽位 1-4> `"文本`"" }; Send-Prompt $led $Slot $Text }
    "hear"        { Hear-Slot $led $Slot }
    "log"         { Show-Log $led $Slot $Text }
    "help"        { Show-Usage }
    default       { Write-Warning "未知命令：$Command"; Show-Usage }
}