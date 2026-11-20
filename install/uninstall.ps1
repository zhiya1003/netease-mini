# =====================================================================
# uninstall.ps1 - remove the ncm mini player WITHOUT touching the music client.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File D:\dsh<ws>\netease-mini\install\uninstall.ps1
#   add -KeepLog to keep runtime\server.log for post-mortem
#
# What it does                                            What it deliberately does NOT do
#   1. stop the watchdog (--unwatch)                        - it does not stop, restart or signal
#   2. MiniPlayer --quit                                       cloudmusic or Folia in any way
#   3. delete HKCU:\Software\Classes\ncmmini                 - it does not write to D:\CloudMusic
#   4. delete the run files under runtime\                       (the package is verified read-only
#   5. verify D:\CloudMusic\package\orpheus.ntpk is still        below; this script only hashes it)
#      byte-identical to install\backup\orpheus.ntpk.orig      - it does not touch HKLM, C:\Windows
#                                                                 or Program Files
# The old version of this script restored the package and killed the client. That path is dead:
# the patched package is refused at start-up by the integrity check, and the live package was
# never replaced, so there is nothing to restore. It is now verify-only.
# =====================================================================
param([switch]$KeepLog, [switch]$DryRun)
$ErrorActionPreference = 'Continue'

# ASCII-only source: Windows PowerShell 5.1 decodes .ps1 with the ANSI codepage, so the
# workspace name is assembled from char codes instead of being written literally.
$Cn    = [string]([char]0x5DE5) + [string]([char]0x4F5C) + [string]([char]0x533A)
$Root  = 'D:\dsh' + $Cn
$Proj  = Join-Path $Root 'netease-mini'
$Install = Join-Path $Proj 'install'
$Runtime = Join-Path $Proj 'runtime'
$MP      = Join-Path $Proj 'overlay\MiniPlayer.exe'
$Pkg     = 'D:\CloudMusic\package\orpheus.ntpk'
$Orig    = Join-Path $Install 'backup\orpheus.ntpk.orig'
$WantSha = '170413B1675708E87A9E89C2D122A8643A51E86B5B5AE1C6260CFEAF25B19BE4'

function Sha([string]$p) {
    if (-not (Test-Path -LiteralPath $p)) { return 'ABSENT' }
    try { return (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToUpperInvariant() } catch { return 'ERR ' + $_.Exception.Message }
}

Write-Host '=== 0) preconditions ==='
if ($DryRun) { Write-Host '  DRY-RUN MODE: every destructive step below is printed as [DRYRUN] would: ... and NOT executed.' }
Write-Host ('MiniPlayer.exe    : ' + $MP + '  exists=' + (Test-Path -LiteralPath $MP))
Write-Host ('backup orig       : ' + $Orig)
Write-Host ('live package      : ' + $Pkg)

Write-Host ''
Write-Host '=== 1) stop the D3 watchdog if one is running ==='
if ($DryRun) { Write-Host '  [DRYRUN] would: MiniPlayer.exe --unwatch (removes the watchdog)' }
elseif (Test-Path -LiteralPath $MP) { & $MP --unwatch 2>&1 | ForEach-Object { Write-Host ('  ' + $_) } }
$wj = Join-Path $Runtime 'watch.json'
Write-Host ('  watch.json present after --unwatch = ' + (Test-Path -LiteralPath $wj))

Write-Host ''
Write-Host '=== 2) MiniPlayer --quit (graceful; no taskkill unless it ignores us) ==='
if ($DryRun) {
    Write-Host '  [DRYRUN] would: MiniPlayer.exe --quit, then Stop-Process if it ignores us'
    $left = @(Get-Process MiniPlayer -ErrorAction SilentlyContinue)
} else {
    if (Test-Path -LiteralPath $MP) { & $MP --quit 2>&1 | ForEach-Object { Write-Host ('  ' + $_) } }
    Start-Sleep -Seconds 2
    $left = @(Get-Process MiniPlayer -ErrorAction SilentlyContinue)
    if ($left.Count -gt 0) {
        Write-Host ('  ' + $left.Count + ' MiniPlayer still alive after --quit, stopping pid ' + (($left | ForEach-Object { $_.Id }) -join ','))
        $left | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        $left = @(Get-Process MiniPlayer -ErrorAction SilentlyContinue)
    }
}
Write-Host ('  MiniPlayer processes now = ' + $left.Count)
Write-Host ('  cloudmusic processes     = ' + @(Get-Process cloudmusic -ErrorAction SilentlyContinue).Count + '  (untouched by this script)')
Write-Host ('  Folia processes          = ' + @(Get-Process Folia -ErrorAction SilentlyContinue).Count + '  (untouched by this script)')

Write-Host ''
Write-Host '=== 3) unregister the ncmmini: protocol (HKCU only) ==='
if ($DryRun) { Write-Host '  [DRYRUN] would: MiniPlayer.exe --unregister and Remove-Item HKCU:\Software\Classes\ncmmini' }
else {
    if (Test-Path -LiteralPath $MP) { & $MP --unregister 2>&1 | ForEach-Object { Write-Host ('  ' + $_) } }
    if (Test-Path 'HKCU:\Software\Classes\ncmmini') {
        Remove-Item -Path 'HKCU:\Software\Classes\ncmmini' -Recurse -Force -ErrorAction SilentlyContinue
    }
}
Write-Host ('  HKCU:\Software\Classes\ncmmini present = ' + (Test-Path 'HKCU:\Software\Classes\ncmmini'))
Write-Host ('  HKLM:\SOFTWARE\Classes\ncmmini present = ' + (Test-Path 'HKLM:\SOFTWARE\Classes\ncmmini') + '  (never written by the install)')

Write-Host ''
Write-Host '=== 4) delete the run files (project-local, backup kept) ==='
$run = @('port.json', 'MiniPlayer.lock', 'position.json', 'config.json', 'winrect.json', 'hotspots.json', 'watch.json')
if (-not $KeepLog) { $run += 'server.log' }
foreach ($f in $run) {
    $p = Join-Path $Runtime $f
    if ($DryRun) {
        if (Test-Path -LiteralPath $p) { Write-Host ('  [DRYRUN] would remove ' + $f + ' (' + (Get-Item -LiteralPath $p).Length + ' bytes)') }
        else { Write-Host ('  absent  ' + $f) }
    }
    elseif (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue; Write-Host ('  removed ' + $f) }
    else { Write-Host ('  absent  ' + $f) }
}
Write-Host '  runtime\*.png screenshot debris left alone on purpose - they are evidence, delete by hand.'

Write-Host ''
Write-Host '=== 5) verify the music client was never modified ==='
$a = Sha $Orig
$b = Sha $Pkg
Write-Host ('  backup orpheus.ntpk.orig   sha256=' + $a)
Write-Host ('  live   D:\CloudMusic\...   sha256=' + $b)
Write-Host ('  live bytes=' + $(if (Test-Path -LiteralPath $Pkg) { (Get-Item -LiteralPath $Pkg).Length } else { 'ABSENT' }) + '   backup bytes=' + $(if (Test-Path -LiteralPath $Orig) { (Get-Item -LiteralPath $Orig).Length } else { 'ABSENT' }))
$okBackup = ($a -eq $WantSha)
$okLive   = ($b -eq $WantSha)
$identical = ($a -eq $b -and $a -ne 'ABSENT')
Write-Host ('  backup  == expected 170413B1675708E8... : ' + $okBackup)
Write-Host ('  live    == expected 170413B1675708E8... : ' + $okLive)
Write-Host ('  live    == backup (byte identical)      : ' + $identical)
if ($okLive -and $identical) { Write-Host '  RESULT: the netease package was never replaced. Nothing to restore.' }
else { Write-Host '  RESULT: the live package does NOT match the pristine backup. Restore it by hand from ' + $Orig + ' (this script will not write into D:\CloudMusic).' }

Write-Host ''
if ($DryRun) {
    Write-Host ''
    Write-Host '=== DRY-RUN COMPLETE - nothing was deleted, stopped or unregistered ==='
}
Write-Host '=== UNINSTALL DONE ==='
Write-Host ('  left on disk: ' + $Proj + ' (the program itself) and ' + $Install + '\backup (the pristine package copy).')
Write-Host '  delete those two folders by hand for a full removal; nothing else was installed.'
if ($okLive -and $okBackup) { exit 0 } else { exit 1 }
