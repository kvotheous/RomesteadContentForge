<#
  xnb2png - extract Romestead/XNA sprite-sheet textures (.xnb) to .png, for use as Content Forge custom-art
  templates. Handles UNCOMPRESSED Texture2D with SurfaceFormat.Color (RGBA) - what the character/weapon
  sheets use. Compressed (.xnb flag 0x40) or DXT-format textures are reported and skipped.

  Usage:
    ./xnb2png.ps1 -In "<file.xnb or folder>" -Out "<out folder>"
    ./xnb2png.ps1 -In "C:\...\romestead\Content\media\sprites\character\tools" -Out ".\templates" -Recurse
#>
param(
  [Parameter(Mandatory=$true)][string]$In,
  [Parameter(Mandatory=$true)][string]$Out,
  [switch]$Recurse
)

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $Out -Force | Out-Null

$files = @()
if (Test-Path $In -PathType Leaf) { $files += $In }
elseif (Test-Path $In -PathType Container) { $files += (Get-ChildItem -Path $In -Filter *.xnb -Recurse:$Recurse).FullName }
else { Write-Error "Not found: $In"; exit 2 }

function Convert-One($path, $outDir) {
  $b = [System.IO.File]::ReadAllBytes($path)
  $name = [System.IO.Path]::GetFileNameWithoutExtension($path)
  if ($b.Length -lt 10 -or [System.Text.Encoding]::ASCII.GetString($b[0..2]) -ne "XNB") { Write-Host "  skip $name (not XNB)"; return }
  if ($b[5] -band 0x40) { Write-Host "  skip $name (compressed XNB - not supported)"; return }

  $pos = 10
  $r7 = { $r=0;$s=0; while($true){ $v=$b[$script:pos]; $script:pos++; $r=$r -bor (($v -band 0x7F) -shl $s); if(($v -band 0x80)-eq 0){break}; $s+=7 }; $r }
  $i32 = { $v=[BitConverter]::ToInt32($b,$script:pos); $script:pos+=4; $v }
  $script:pos = $pos
  $trc = & $r7
  for($i=0;$i -lt $trc;$i++){ $len = & $r7; $script:pos += $len; $script:pos += 4 }
  $null = & $r7        # shared resource count
  $null = & $r7        # object type id
  $fmt = & $i32; $w = & $i32; $h = & $i32; $null = & $i32; $dsz = & $i32
  if ($fmt -ne 0) { Write-Host "  skip $name (SurfaceFormat $fmt - only Color/0 supported)"; return }
  if ($dsz -ne ($w*$h*4)) { Write-Host "  skip $name (size mismatch)"; return }

  $px = $script:pos
  $dest = New-Object byte[] ($w*$h*4)
  for($i=0;$i -lt ($w*$h);$i++){ $o=$i*4
    $dest[$o]   = $b[$px+$o+2]   # B
    $dest[$o+1] = $b[$px+$o+1]   # G
    $dest[$o+2] = $b[$px+$o]     # R
    $dest[$o+3] = $b[$px+$o+3]   # A
  }
  $bmp = New-Object System.Drawing.Bitmap($w,$h,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $rect = New-Object System.Drawing.Rectangle(0,0,$w,$h)
  $bd = $bmp.LockBits($rect,[System.Drawing.Imaging.ImageLockMode]::WriteOnly,$bmp.PixelFormat)
  [System.Runtime.InteropServices.Marshal]::Copy($dest,0,$bd.Scan0,$dest.Length)
  $bmp.UnlockBits($bd)
  $outPath = Join-Path $outDir "$name.png"
  $bmp.Save($outPath,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Write-Host "  $name.png  (${w}x${h})"
}

Write-Host "Extracting $($files.Count) file(s) -> $Out"
foreach ($f in $files) { Convert-One $f $Out }
Write-Host "Done."
