<#
    Run Unity headlessly WITH A WINDOW YOU CAN SEE.

    WHY THIS EXISTS. Every headless run in this project goes `-batchmode -logFile <somewhere>`
    with the window minimised, so from the outside it is indistinguishable from nothing
    happening - which is exactly what it looked like during a run that appeared to hang for
    twenty-six minutes and was really a second Unity exiting instantly because the first still
    held the project. The log had the answer the whole time and nobody was looking at it.

    So: the log lands in Logs\ (git-ignored, always the same place), and a second window opens
    tailing it live. Close that window whenever you like - it is watching a file, not driving
    anything, and killing it cannot hurt the run.

        tools\watch-run.ps1                          # the layer photographs
        tools\watch-run.ps1 -Filter Noir.PlayTests.TrafficPlayTests
        tools\watch-run.ps1 -Method Noir.Editor.SmokeTest.Run    # an editor entry point instead

    THE WINDOW IS SIZED, NOT MAXIMISED. The primary display here is 5120 wide and a maximised
    console is unreadable across it.
#>
param(
    [string]$Filter = "Noir.PlayTests.LayerProof",
    [string]$Method = "",
    [switch]$BuiltTown = $true
)

$ErrorActionPreference = "Stop"
$project = Split-Path $PSScriptRoot -Parent
$unity = "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe"
if (-not (Test-Path $unity)) { throw "Unity not found at $unity" }

# ONE UNITY AT A TIME. A second instance cannot open a project the first still holds: it prints
# four lines and exits 1, which reads as a hang to anything watching for output.
$stale = Get-Process Unity -ErrorAction SilentlyContinue
if ($stale) {
    Write-Host "closing $($stale.Count) Unity instance(s) that hold the project..." -ForegroundColor Yellow
    $stale | Stop-Process -Force
    Start-Sleep -Seconds 3
}
$lock = Join-Path $project "Temp\UnityLockfile"
if (Test-Path $lock) { Remove-Item $lock -Force -ErrorAction SilentlyContinue }

$logs = Join-Path $project "Logs"
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$stamp = Get-Date -Format "MMdd-HHmmss"
$log = Join-Path $logs "run-$stamp.log"
$xml = Join-Path $logs "run-$stamp.xml"
Set-Content -Path $log -Value "waiting for Unity to start..." -Encoding utf8

if ($BuiltTown) { $env:NOIR_BUILT_TOWN = "1" }

if ($Method) {
    $args = @("-batchmode","-quit","-projectPath",$project,"-executeMethod",$Method,"-logFile",$log)
    $what = "editor method $Method"
} else {
    $args = @("-batchmode","-projectPath",$project,"-runTests","-testPlatform","PlayMode",
              "-assemblyNames","Noir.PlayTests","-testFilter",$Filter,
              "-testResults",$xml,"-logFile",$log)
    $what = "play-mode tests matching $Filter"
}

# The watcher first, so it catches the whole run rather than joining halfway.
$tail = @"
`$Host.UI.RawUI.WindowTitle = 'Noir - $what'
try {
  `$b = `$Host.UI.RawUI.BufferSize; `$b.Width = 150; `$b.Height = 4000; `$Host.UI.RawUI.BufferSize = `$b
  `$w = `$Host.UI.RawUI.WindowSize; `$w.Width = 150; `$w.Height = 48; `$Host.UI.RawUI.WindowSize = `$w
} catch {}
Write-Host '$what' -ForegroundColor Cyan
Write-Host '$log' -ForegroundColor DarkGray
Write-Host ('-' * 120) -ForegroundColor DarkGray
Get-Content -Path '$log' -Wait -Tail 40 | Where-Object { `$_ -notmatch 'Licensing|Entitlement|^\s*$' }
"@
Start-Process powershell -ArgumentList @("-NoExit","-Command",$tail) | Out-Null

$p = Start-Process -PassThru -FilePath $unity -WindowStyle Minimized -ArgumentList $args
Write-Host "Unity pid $($p.Id) - $what" -ForegroundColor Cyan
Write-Host "log: $log"

$p.WaitForExit()
Write-Host "exit code $($p.ExitCode)" -ForegroundColor $(if ($p.ExitCode -eq 0) { "Green" } else { "Red" })

if (Test-Path $xml) {
    [xml]$r = Get-Content $xml
    Write-Host ("tests: {0}  passed {1}  failed {2}" -f `
        $r.'test-run'.result, $r.'test-run'.passed, $r.'test-run'.failed)
    $r.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
        Write-Host ("  FAILED " + $_.name) -ForegroundColor Red
        Write-Host ("          " + ($_.failure.message.InnerText -split "`n")[0])
    }
}
