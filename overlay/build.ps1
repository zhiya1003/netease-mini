# One-shot build for MiniPlayer.exe using the in-box .NET Framework 4.x compiler (csc.exe).
# Direct .ps1 launch is blocked by the execution policy, so run it like this:
#   $OverlayDir = '<absolute path to this overlay folder>'
#   (Get-Content -Raw '<absolute path>\build.ps1') | Invoke-Expression
# $OverlayDir may also come from the OVERLAY_DIR environment variable.
$ErrorActionPreference = 'Continue'
$env:VSLANG = '1033'
$cscDefault = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$cands = @()
if ($OverlayDir) { $cands += $OverlayDir }
if ($env:OVERLAY_DIR) { $cands += $env:OVERLAY_DIR }
$cands += (Get-Location).Path
$cands += (Join-Path (Get-Location).Path 'netease-mini\overlay')
foreach ($c in $cands) { if ($c -and (Test-Path (Join-Path $c 'src\Program.cs'))) { $OverlayDir = $c; break } }
if (-not $OverlayDir) { throw 'could not locate the overlay folder (set $OverlayDir or OVERLAY_DIR)' }
if (-not (Test-Path (Join-Path $OverlayDir 'src'))) { throw ('no src folder under ' + $OverlayDir) }
if (-not $CscPath) { $CscPath = $cscDefault }
if (-not (Test-Path $CscPath)) { throw ('csc.exe not found: ' + $CscPath) }
$Overlay = $OverlayDir
$Csc = $CscPath
# ---------------------------------------------------------------------------
# P4: ARCHIVE BEFORE OVERWRITE. The 17:40 -> 18:56 build replaced
# overlay\MiniPlayer.exe in place and left no prev copy, so the rollback chain was
# broken for that one step (recorded in docs\STATUS.md section I). Every build now
# files the exe it is about to replace as MiniPlayer.prev-<HHmm>.exe and keeps the
# newest $KeepPrev of them. Nothing else is ever deleted: MiniPlayer.old.exe and the
# .bak files are out of scope of the filter. Set $ArchiveOnly = $true to run ONLY the
# archiving (no csc call) - that is how the mechanism is verified without a compile.
# ---------------------------------------------------------------------------
# 5, not 3: with keep=3 the rotation evicted prev-1856 (the build the click chain was accepted on)
# during the wash calibration rounds. The parent ruled keep=5 plus a pinned read-only accepted copy
# (Pin-Accepted below), so an accepted artifact cannot be lost to rotation again.
$KeepPrev = 5
$ExePath = Join-Path $Overlay 'MiniPlayer.exe'
function Archive-Prev([string]$Target, [string]$Dir, [int]$Keep) {
  if (Test-Path $Target) {
    $src = Get-Item -LiteralPath $Target
    # <HHmm> is the time STAMP OF THE EXE BEING RETIRED (its own mtime = when it was built), which is
    # how prev-1449 / prev-1520 were already named; the name and the sort key therefore agree.
    $dst = Join-Path $Dir ('MiniPlayer.prev-' + $src.LastWriteTime.ToString('HHmm') + '.exe')
    if ((Split-Path -Leaf $Target) -eq (Split-Path -Leaf $dst)) { $dst = Join-Path $Dir ('MiniPlayer.prev-' + $src.LastWriteTime.ToString('HHmm') + '.exe') }
    Copy-Item -LiteralPath $Target -Destination $dst -Force
    $di = Get-Item -LiteralPath $dst
    Write-Host ('PREV archived ' + (Split-Path -Leaf $Target) + ' -> ' + (Split-Path -Leaf $dst) + ' ' + $di.Length + 'B srcMtime=' + $di.LastWriteTime.ToString('s'))
  } else {
    Write-Host ('PREV nothing to archive (' + $Target + ' does not exist)')
  }
  $all = @(Get-ChildItem -LiteralPath $Dir -Filter 'MiniPlayer.prev-*.exe' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime,Name -Descending)
  if ($all.Count -gt $Keep) {
    foreach ($x in @($all | Select-Object -Skip $Keep)) {
      Write-Host ('PREV prune ' + $x.Name + ' ' + $x.Length + 'B mtime=' + $x.LastWriteTime.ToString('s'))
      Remove-Item -LiteralPath $x.FullName -Force
    }
  } else {
    Write-Host ('PREV prune not needed (' + $all.Count + ' <= keep ' + $Keep + ')')
  }
  $left = @(Get-ChildItem -LiteralPath $Dir -Filter 'MiniPlayer.prev-*.exe' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime,Name -Descending)
  Write-Host ('PREV keep=' + $Keep + ' present=' + $left.Count + ': ' + (($left | ForEach-Object { $_.Name + '(' + $_.Length + 'B @' + $_.LastWriteTime.ToString('HH:mm:ss') + ')' }) -join ', '))
  return $left
}
# Pin-Accepted: freeze the CURRENT exe as the accepted baseline under install\backup, read-only, and print
# its sha256 so install\README.md can record it. Deliberately NOT automatic: only an exe that has passed the
# acceptance run gets pinned, and prev-* rotation (keep=5) can then never lose the accepted baseline again
# (it did lose prev-1856 while keep was 3 - see docs\STATUS.md section K8).
function Pin-Accepted([string]$Target, [string]$ProjDir) {
  if (-not (Test-Path $Target)) { Write-Host 'PIN accepted: no exe to pin'; return }
  $bak = Join-Path $ProjDir 'install\backup'
  if (-not (Test-Path $bak)) { New-Item -ItemType Directory -Path $bak | Out-Null }
  $src = Get-Item -LiteralPath $Target
  $dst = Join-Path $bak ('MiniPlayer.accepted-' + $src.LastWriteTime.ToString('HHmm') + '.exe')
  if (Test-Path -LiteralPath $dst) {
    Set-ItemProperty -LiteralPath $dst -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $dst -Force
  }
  Copy-Item -LiteralPath $Target -Destination $dst
  Set-ItemProperty -LiteralPath $dst -Name IsReadOnly -Value $true
  $di = Get-Item -LiteralPath $dst
  $h = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
  Write-Host ('PIN accepted -> install\backup\' + $di.Name + ' ' + $di.Length + 'B srcMtime=' + $di.LastWriteTime.ToString('s') + ' IsReadOnly=' + $di.IsReadOnly)
  Write-Host ('PIN sha256=' + $h)
}
$ProjDirRoot = Split-Path $Overlay -Parent
[void](Archive-Prev $ExePath $Overlay $KeepPrev)
if ($PinAccepted) { Pin-Accepted $ExePath $ProjDirRoot }
if ($ArchiveOnly) { Write-Host 'ARCHIVE ONLY - csc was not invoked ($ArchiveOnly)'; exit 0 }
$Gac = 'C:\Windows\Microsoft.NET\assembly'
function Find-Asm([string]$name) {
  $roots = @( ($Gac + '\GAC_64\' + $name), ($Gac + '\GAC_MSIL\' + $name), ($Gac + '\GAC_32\' + $name), 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF' )
  foreach ($r in $roots) {
    if (-not (Test-Path $r)) { continue }
    $direct = Join-Path $r ($name + '.dll')
    if (Test-Path $direct) { return $direct }
    $hit = Get-ChildItem $r -Recurse -Filter ($name + '.dll') -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }
  }
  return $null
}
$names = 'PresentationCore','PresentationFramework','WindowsBase','System.Xaml','System','System.Core','System.Xml','System.Windows.Forms','System.Drawing'
$refs = @()
foreach ($n in $names) {
  $p = Find-Asm $n
  if ($p) { $refs += ('/reference:' + $p); Write-Host ('REF ' + $n + ' = ' + $p) }
  else { Write-Host ('REF ' + $n + ' = ABSENT (skipped)') }
}
$srcDir = Join-Path $Overlay 'src'
$sources = @(Get-ChildItem $srcDir -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw ('no .cs sources in ' + $srcDir) }
$out = Join-Path $Overlay 'MiniPlayer.exe'
if (Test-Path $out) { Remove-Item $out -Force }
$outArg = '/out:' + $out
$cscArgs = @('/nologo','/noconfig','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/nowarn:1591,1701,1702,0067,0169,0414,0649')
$cscArgs += $outArg
$cscArgs += $refs
$cscArgs += $sources
Write-Host ('CMDLINE = "' + $Csc + '" ' + ($cscArgs -join ' '))
Write-Host ('SOURCES = ' + ($sources -join ', '))
& $Csc @cscArgs
$code = $LASTEXITCODE
Write-Host ('csc exit code = ' + $code)
if ($code -eq 0 -and (Test-Path $out)) {
  $fi = Get-Item $out
  Write-Host ('BUILT ' + $fi.FullName + ' ' + $fi.Length + ' bytes mtime=' + $fi.LastWriteTime.ToString('s'))
} else { Write-Host 'BUILD FAILED' }
exit $code
