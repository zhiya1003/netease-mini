if(-not $Install){ $Install = 'D:\dsh工作区\netease-mini\install' }
if(-not $Overlay){ $Overlay = 'D:\dsh工作区\netease-mini\overlay' }
if(-not $SrcPkg){ $SrcPkg = 'D:\CloudMusic\package\orpheus.ntpk' }
if(-not $NcmExe){ $NcmExe = 'D:\CloudMusic\cloudmusic.exe' }
$ErrorActionPreference='Stop'
$NL=[Environment]::NewLine
function ShaFile($p){ $h=New-Object System.IO.FileStream($p,[System.IO.FileMode]::Open,[System.IO.FileAccess]::Read,[System.IO.FileShare]::ReadWrite)
  $s=[System.Security.Cryptography.SHA256]::Create().ComputeHash($h); $h.Close(); (-join($s|ForEach-Object{$_.ToString('X2')})) }
$BK=Join-Path $Install 'backup'
$ORIG=Join-Path $BK 'orpheus.ntpk.orig'
$NEW=Join-Path $Install 'orpheus.ntpk.new'
$MP=Join-Path $Overlay 'MiniPlayer.exe'

'=== install: 0) preconditions ==='
if(-not (Test-Path -LiteralPath $ORIG)){ throw ('backup missing: ' + $ORIG + ' - refusing to touch the live package') }
if(-not (Test-Path -LiteralPath $NEW)){ throw ('new package missing: ' + $NEW + ' - run build-package.ps1 first') }
$origSha=ShaFile $ORIG
'backup ' + $ORIG + ' bytes=' + (Get-Item -LiteralPath $ORIG).Length + ' sha256=' + $origSha
'newpkg ' + $NEW + ' bytes=' + (Get-Item -LiteralPath $NEW).Length + ' sha256=' + (ShaFile $NEW)
$liveBefore='(absent)'; if(Test-Path -LiteralPath $SrcPkg){ $liveBefore=ShaFile $SrcPkg }
'live-before sha256=' + $liveBefore

'=== install: 1) stop cloudmusic ==='
$procs=@(Get-Process cloudmusic -ErrorAction SilentlyContinue)
'cloudmusic pids before = ' + (($procs|ForEach-Object{$_.Id}) -join ',')
$procs | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3
'cloudmusic pids after  = ' + ((@(Get-Process cloudmusic -ErrorAction SilentlyContinue)|ForEach-Object{$_.Id}) -join ',')

'=== install: 2) replace package ==='
Copy-Item -LiteralPath $NEW -Destination $SrcPkg -Force
'live-after  sha256=' + (ShaFile $SrcPkg) + ' bytes=' + (Get-Item -LiteralPath $SrcPkg).Length

'=== install: 3) register protocol (MiniPlayer --register) ==='
& $MP --register 2>&1 | ForEach-Object { '  ' + $_ }
$cmdKey='HKCU:\Software\Classes\ncmmini\shell\open\command'
$asRegistered=(Get-ItemProperty -Path $cmdKey).'(default)'
'REGISTERED_VALUE=' + $asRegistered
if(-not $NoVerbFix){
  $fixed=($asRegistered -replace '%1','ncmmini://show')
  Set-ItemProperty -Path $cmdKey -Name '(default)' -Value $fixed
  'APPLIED_VALUE =' + ((Get-ItemProperty -Path $cmdKey).'(default)')
  'verb note: mini.js fires ncmmini://start but overlay MiniPlayer Program.cs:238 only cold-launches for toggle|show'
}

'=== install: 4) launch cloudmusic (no debug port args) ==='
if(-not $NoLaunch){
  $p=Start-Process -FilePath $NcmExe -PassThru
  'started cloudmusic pid=' + $p.Id + ' cmdline=' + ((Get-CimInstance Win32_Process -Filter ("ProcessId=" + $p.Id) -ErrorAction SilentlyContinue).CommandLine)
}
'=== INSTALL DONE ==='
