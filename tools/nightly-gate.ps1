# nightly-gate.ps1 - the two standing gates, run unattended overnight.
#
# Registered as the Windows Scheduled Task "Rossville Nightly Gate" (see CLAUDE.md).
# Runs the Core suite in Release and the PlayMode gate, compares both against the
# recorded baselines, and writes:
#   nightly\YYYY-MM-DD.md   - the dated report
#   nightly\LATEST.md       - always the most recent report (read this in the morning)
#   nightly\history.log     - one line per run, forever
# The nightly\ directory is gitignored: reports are measurements, not source.
#
# THE EDITOR WINS. If Unity.exe is running when this fires, the owner left it open on
# purpose and this script SKIPS the PlayMode half (a -batchmode launch cannot share the
# Library lock, and killing his editor is forbidden). The Core half runs regardless -
# it needs no Unity.
#
# -DryRun exercises every code path except the two long runs.

param([switch]$DryRun)

$ErrorActionPreference = 'Continue'
$repo    = 'C:\SerialKillerGame'
$unity   = 'C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe'
$outDir  = Join-Path $repo 'nightly'
$stamp   = Get-Date -Format 'yyyy-MM-dd'
$started = Get-Date

# The baselines this run is judged against. When a landing moves them, CLAUDE.md is
# the authority - update these two numbers in the same commit that updates it.
$coreBaselinePass     = 596
# 37, not 38+: the standing gate builds the survey-plan town, which stands no owner
# models, so the two 408-door gates Assert.Ignore there by design - they measure the
# dressed town (live editor, or NOIR_BUILT_TOWN=1).
$playmodeBaselinePass = 37

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# One run at a time: a lock file younger than 3 hours means a run is still going.
$lock = Join-Path $outDir '.running'
if ((Test-Path $lock) -and ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalHours -lt 3) {
    Add-Content -Encoding utf8 (Join-Path $outDir 'history.log') "$stamp SKIPPED: a previous run's lock is still fresh"
    exit 0
}
Set-Content -Encoding utf8 $lock "$started"

$lines = @()
$lines += "# Nightly gate - $stamp"
$lines += ""
$verdicts = @()

# ---- 1. Core suite, Release ------------------------------------------------------
if ($DryRun) {
    $coreSummary = 'DRY RUN - core suite not executed'
    $verdicts += 'DRY'
} else {
    Set-Location $repo
    $coreOut = & dotnet test -c Release tools\Noir.Core.Tests\Noir.Core.Tests.csproj 2>&1
    $coreSummary = ($coreOut | Select-String -Pattern 'Passed!|Failed!' | Select-Object -Last 1).ToString().Trim()
    if (-not $coreSummary) { $coreSummary = 'NO SUMMARY LINE - the run crashed; see raw output below' }
    $corePassed = if ($coreSummary -match 'Passed:\s+(\d+)') { [int]$Matches[1] } else { -1 }
    $coreFailed = if ($coreSummary -match 'Failed:\s+(\d+)') { [int]$Matches[1] } else { -1 }
    if ($coreFailed -eq 0 -and $corePassed -ge $coreBaselinePass) { $verdicts += 'CORE GREEN' }
    else {
        $verdicts += 'CORE RED'
        $lines += '## Core failures'
        $lines += '```'
        $lines += ($coreOut | Select-String -Pattern 'Failed ' | ForEach-Object { $_.ToString().Trim() })
        $lines += '```'
    }
}
$lines += "**Core:** $coreSummary (baseline $coreBaselinePass pass, 0 fail)"
$lines += ""

# ---- 2. PlayMode gate ------------------------------------------------------------
$editorOpen = @(Get-Process -Name Unity -ErrorAction Ignore).Count -gt 0
if ($DryRun) {
    $lines += '**PlayMode:** DRY RUN - not executed'
} elseif ($editorOpen) {
    $lines += '**PlayMode:** SKIPPED - the editor was open, and the editor wins.'
    $verdicts += 'PLAYMODE SKIPPED (editor open)'
} else {
    $xml = Join-Path $outDir "playmode-$stamp.xml"
    $log = Join-Path $outDir "playmode-$stamp.log"
    Remove-Item $xml, $log -ErrorAction Ignore
    $p = Start-Process -FilePath $unity -ArgumentList '-batchmode', '-projectPath', $repo,
        '-runTests', '-testPlatform', 'PlayMode', '-assemblyNames', 'Noir.PlayTests',
        '-testCategory', '!Diagnostic', '-testResults', $xml, '-logFile', $log -PassThru
    # An honest wait: the process, with a hard ceiling - 90 min is double the measured run.
    $p | Wait-Process -Timeout 5400 -ErrorAction SilentlyContinue
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction Ignore   # our own tracked batch PID only
        $lines += '**PlayMode:** KILLED after 90 min without finishing - the log tail is in the report.'
        $verdicts += 'PLAYMODE HUNG'
    }
    if (Test-Path $xml) {
        $r = ([xml](Get-Content $xml)).'test-run'
        $pm = "passed $($r.passed), failed $($r.failed), skipped $($r.skipped), $([math]::Round([double]$r.duration))s"
        $lines += "**PlayMode:** $pm (baseline $playmodeBaselinePass pass, 0 fail)"
        if ([int]$r.failed -eq 0 -and [int]$r.passed -ge $playmodeBaselinePass) { $verdicts += 'PLAYMODE GREEN' }
        else {
            $verdicts += 'PLAYMODE RED'
            $lines += '## PlayMode failures'
            foreach ($tc in (Select-Xml -Path $xml -XPath '//test-case[@result="Failed"]')) {
                $msg = ($tc.Node.SelectSingleNode('failure/message').'#text' -split "`n" | Select-Object -First 4) -join ' / '
                $lines += "- **$($tc.Node.name)** - $msg"
            }
        }
    } elseif (-not $editorOpen) {
        $lines += '**PlayMode:** NO RESULTS XML - the run died. Log tail:'
        $lines += '```'
        if (Test-Path $log) { $lines += (Get-Content $log -Tail 15) }
        $lines += '```'
        $verdicts += 'PLAYMODE DEAD'
    }
}

# ---- 3. The record ---------------------------------------------------------------
$took = [math]::Round(((Get-Date) - $started).TotalMinutes)
$verdict = $verdicts -join ' | '
$lines = @("**$verdict** - took $took min", "") + $lines
$report = Join-Path $outDir "$stamp.md"
$lines | Out-File -Encoding utf8 $report
Copy-Item $report (Join-Path $outDir 'LATEST.md') -Force
Add-Content -Encoding utf8 (Join-Path $outDir 'history.log') "$stamp $verdict ($took min)"
Remove-Item $lock -ErrorAction Ignore
