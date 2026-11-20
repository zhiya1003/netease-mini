if(-not $Install){ $Install = 'D:\dsh工作区\netease-mini\install' }
if(-not $Bridge){ $Bridge = 'D:\dsh工作区\netease-mini\bridge' }
if(-not $SrcPkg){ $SrcPkg = 'D:\CloudMusic\package\orpheus.ntpk' }
$ErrorActionPreference='Stop'
$NL=[Environment]::NewLine
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression

function Sha([byte[]]$b){ $h=[System.Security.Cryptography.SHA256]::Create().ComputeHash($b); (-join($h|ForEach-Object{$_.ToString('X2')})) }
function Read-Entry($entry){ $s=$entry.Open(); $m=New-Object System.IO.MemoryStream; $s.CopyTo($m); $s.Dispose(); return ,$m.ToArray() }
function AppHtml-Transform([byte[]]$b){
  $s=[System.Text.Encoding]::UTF8.GetString($b)
  $ms=[regex]::Matches($s,"[^\r\n]*(\r\n|\n)?") | Where-Object { $_.Value.Length -gt 0 }
  if($ms.Count -ne 32){ throw ('app.html line count = ' + $ms.Count + ' (expected 32)') }
  if($ms[8].Value -notmatch 'c312fd27b392865ceeed\.css'){ throw 'anchor line9 mismatch' }
  if($ms[29].Value -notmatch 'app\.chunk\.1482700000000\.js'){ throw 'anchor line30 mismatch' }
  $cssLine = '    <link href="/pub/hybrid/mini.css" rel="stylesheet">' + $NL
  $cfgLine = '    <script>window.__NCM_MINI_CFG__={selfStartMode:"href"}</script>' + $NL
  $jsLine  = '    <script src="/pub/hybrid/mini.js"></script>' + $NL
  $sb=New-Object System.Text.StringBuilder
  for($i=0;$i -lt 32;$i++){
    [void]$sb.Append($ms[$i].Value)
    if($i -eq 8){ [void]$sb.Append($cssLine) }
    if($i -eq 29){ [void]$sb.Append($cfgLine); [void]$sb.Append($jsLine) }
  }
  return ,[System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
}

$BK=Join-Path $Install 'backup'
$EV=Join-Path $Install 'evidence'
New-Item -ItemType Directory -Force -Path $Install,$BK,$EV | Out-Null

'=== S1 backup ==='
Copy-Item -LiteralPath $SrcPkg -Destination (Join-Path $BK 'orpheus.ntpk.orig') -Force
$b=[System.IO.File]::ReadAllBytes((Join-Path $BK 'orpheus.ntpk.orig'))
'orig_copy bytes=' + $b.Length + ' sha256=' + (Sha $b)
'src_live  bytes=' + (Get-Item -LiteralPath $SrcPkg).Length + ' sha256=' + (Sha ([System.IO.File]::ReadAllBytes($SrcPkg)))
$hd=New-Object byte[] 100; [Array]::Copy($b,0,$hd,0,100)
[System.IO.File]::WriteAllBytes((Join-Path $BK 'header100.bin'),$hd)
$bd=New-Object byte[] ($b.Length-100); [Array]::Copy($b,100,$bd,0,$bd.Length)
[System.IO.File]::WriteAllBytes((Join-Path $BK 'body.zip'),$bd)
'header100 sha256=' + (Sha $hd) + ' first36ascii=' + (-join($hd[0..35]|ForEach-Object{ if($_ -ge 32 -and $_ -le 126){[char]$_}else{'.'} }))
'body.zip  bytes=' + $bd.Length + ' sha256=' + (Sha $bd)

'=== S2 build new package ==='
$jsBytes=[System.IO.File]::ReadAllBytes((Join-Path $Bridge 'mini.js'))
$cssBytes=[System.IO.File]::ReadAllBytes((Join-Path $Bridge 'mini.css'))
'disk mini.js bytes=' + $jsBytes.Length + ' sha256=' + (Sha $jsBytes)
'disk mini.css bytes=' + $cssBytes.Length + ' sha256=' + (Sha $cssBytes)
$zipb=New-Object System.IO.MemoryStream(,$bd)
$zin=New-Object System.IO.Compression.ZipArchive($zipb,[System.IO.Compression.ZipArchiveMode]::Read)
$srcIndex=@{}
foreach($e in $zin.Entries){ $srcIndex[$e.FullName]=$e }
'orig entries=' + $zin.Entries.Count
$fixedTs=$srcIndex['pub/app.html'].LastWriteTime
'fixedTs for new entries=' + $fixedTs.ToString('o')
$out=New-Object System.IO.MemoryStream
$zout=New-Object System.IO.Compression.ZipArchive($out,[System.IO.Compression.ZipArchiveMode]::Create,$true)
$OPT=[System.IO.Compression.CompressionLevel]::Optimal
$NONE=[System.IO.Compression.CompressionLevel]::NoCompression
$copied=0; $skipped=0
foreach($e in $zin.Entries){
  $name=$e.FullName
  if($name -eq 'pub/hybrid/mini.js' -or $name -eq 'pub/hybrid/mini.css'){ $skipped++; continue }
  $lvl=$OPT
  if($name.EndsWith('/') -or $e.Length -eq 0){ $lvl=$NONE }
  $ne=$zout.CreateEntry($name,$lvl)
  $ne.LastWriteTime=$e.LastWriteTime
  $bytes=Read-Entry $e
  if($name -eq 'pub/app.html'){ $bytes=AppHtml-Transform $bytes }
  if($null -eq $bytes){ $bytes=New-Object byte[] 0 }
  $ws=$ne.Open(); $ws.Write($bytes,0,$bytes.Length); $ws.Dispose()
  $copied++
}
'copied=' + $copied + ' skipped_old_hybrid=' + $skipped
$ne=$zout.CreateEntry('pub/hybrid/mini.js',$OPT); $ne.LastWriteTime=$fixedTs
$ws=$ne.Open(); $ws.Write($jsBytes,0,$jsBytes.Length); $ws.Dispose(); 'added pub/hybrid/mini.js bytes=' + $jsBytes.Length
$ne=$zout.CreateEntry('pub/hybrid/mini.css',$OPT); $ne.LastWriteTime=$fixedTs
$ws=$ne.Open(); $ws.Write($cssBytes,0,$cssBytes.Length); $ws.Dispose(); 'added pub/hybrid/mini.css bytes=' + $cssBytes.Length
$zout.Dispose()
$newBody=$out.ToArray(); $out.Dispose()
'new body bytes=' + $newBody.Length + ' sha256=' + (Sha $newBody)
$NEW=Join-Path $Install 'orpheus.ntpk.new'
$fs=[System.IO.File]::Create($NEW); $fs.Write($hd,0,100); $fs.Write($newBody,0,$newBody.Length); $fs.Close(); $fs.Dispose()
'NEW pkg bytes=' + (Get-Item -LiteralPath $NEW).Length + ' sha256=' + (Sha ([System.IO.File]::ReadAllBytes($NEW)))

'=== S3 preflight on NEW package ==='
$nb=[System.IO.File]::ReadAllBytes($NEW)
$h2=New-Object byte[] 100; [Array]::Copy($nb,0,$h2,0,100)
'new header100 sha256=' + (Sha $h2) + ' equal_to_orig=' + ((Sha $h2) -eq (Sha $hd))
$tail=New-Object byte[] ($nb.Length-100); [Array]::Copy($nb,100,$tail,0,$tail.Length)
$zb=New-Object System.IO.MemoryStream(,$tail)
$zv=New-Object System.IO.Compression.ZipArchive($zb,[System.IO.Compression.ZipArchiveMode]::Read)
'entries=' + $zv.Entries.Count + ' (expect 1413)'
$vi=@{}; foreach($e in $zv.Entries){ $vi[$e.FullName]=$e }
foreach($n in @('pub/app.html','pub/hybrid/mini.js','pub/hybrid/mini.css')){ '  has ' + $n + ' = ' + ($vi.ContainsKey($n)) }
$mh=Read-Entry $vi['pub/app.html']
$ss=[System.Text.Encoding]::UTF8.GetString($mh)
$ll=[regex]::Matches($ss,"[^\r\n]*(\r\n|\n)?") | Where-Object { $_.Value.Length -gt 0 }
'app.html bytes=' + $mh.Length + ' lines=' + $ll.Count + ' (expect 35)'
for($i=8;$i -le 11;$i++){ '   L' + ($i+1) + ': ' + ($ll[$i].Value -replace '[\r\n]+$','') }
for($i=29;$i -le 33;$i++){ '   L' + ($i+1) + ': ' + ($ll[$i].Value -replace '[\r\n]+$','') }
$mj=Read-Entry $vi['pub/hybrid/mini.js']
'pkg mini.js bytes=' + $mj.Length + ' sha256=' + (Sha $mj) + ' equal_disk=' + ((Sha $mj) -eq (Sha $jsBytes))
$mc=Read-Entry $vi['pub/hybrid/mini.css']
'pkg mini.css bytes=' + $mc.Length + ' sha256=' + (Sha $mc) + ' equal_disk=' + ((Sha $mc) -eq (Sha $cssBytes))
'--- spot check original entries byte-identical (orig body.zip vs new) ---'
foreach($n in @('pub/hybrid/app.chunk.1482700000000.js','pub/hybrid/0.chunk.1482700000000.js','pub/index.html','pub/hybrid/vendors~app.chunk.1482700000000.js')){
  if(-not $srcIndex.ContainsKey($n)){ '  SKIP not in orig: ' + $n; continue }
  if(-not $vi.ContainsKey($n)){ '  FAIL not in new: ' + $n; continue }
  $a=Read-Entry $srcIndex[$n]; $c=Read-Entry $vi[$n]
  '  ' + $n + ' orig_len=' + $a.Length + ' new_len=' + $c.Length + ' identical=' + ((Sha $a) -eq (Sha $c))
}
'--- name-set diff ---'
$onlyOrig=@(); $onlyNew=@()
foreach($k in $srcIndex.Keys){ if(-not $vi.ContainsKey($k)){ $onlyOrig+=$k } }
foreach($k in $vi.Keys){ if(-not $srcIndex.ContainsKey($k)){ $onlyNew+=$k } }
'only in orig = [' + ($onlyOrig -join ',') + ']'
'only in new  = [' + ($onlyNew -join ',') + ']'
$sumOrig=0; foreach($k in $srcIndex.Keys){ $sumOrig += $srcIndex[$k].Length }
$sumNew=0; foreach($k in $vi.Keys){ $sumNew += $vi[$k].Length }
'sum_uncompressed orig=' + $sumOrig + ' new=' + $sumNew + ' delta=' + ($sumNew-$sumOrig)
$zv.Dispose(); $zb.Dispose()
$zin.Dispose(); $zipb.Dispose()
'=== BUILD DONE ==='
