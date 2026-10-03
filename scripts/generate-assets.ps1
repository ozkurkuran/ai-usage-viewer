param()
# Regenerates the application icon, MSIX visual assets and Store listing logos.
# Output is deterministic and committed; builds do not run this script.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appAssets=Join-Path $repo 'src\AiUsageViewer.App\Assets'
$msixAssets=Join-Path $repo 'packaging\msix\Assets'
$storeAssets=Join-Path $repo 'packaging\store'
New-Item -ItemType Directory -Path $appAssets,$msixAssets,$storeAssets -Force | Out-Null

function New-RoundedPath([single]$x,[single]$y,[single]$w,[single]$h,[single]$r) {
    $path=[Drawing.Drawing2D.GraphicsPath]::new()
    if($r -le 0) { $path.AddRectangle([Drawing.RectangleF]::new($x,$y,$w,$h));return $path }
    $d=2*$r
    $path.AddArc($x,$y,$d,$d,180,90);$path.AddArc($x+$w-$d,$y,$d,$d,270,90)
    $path.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90);$path.AddArc($x,$y+$h-$d,$d,$d,90,90)
    $path.CloseFigure();return $path
}

# Draws the tile glyph into a square of $size pixels at ($left,$top).
function Draw-Glyph([Drawing.Graphics]$g,[single]$left,[single]$top,[int]$size) {
    $snap=$size -le 32
    $inset=if($snap) { 0 } else { [single]($size*0.03) }
    $tileSize=$size-2*$inset
    $tile=New-RoundedPath ($left+$inset) ($top+$inset) $tileSize $tileSize ([single]($tileSize*0.22))
    $brush=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.PointF]::new($left,$top),[Drawing.PointF]::new($left,$top+$size),
        [Drawing.Color]::FromArgb(255,0x8A,0x7C,0xFF),[Drawing.Color]::FromArgb(255,0x55,0x40,0xC8))
    $g.FillPath($brush,$tile);$brush.Dispose();$tile.Dispose()
    # Three ascending usage bars.
    $width=[single]($size*0.16);$gap=[single]($size*0.08);$bottom=[single]($size*0.78)
    $heights=@(0.26,0.40,0.56)
    if($snap) { $width=[Math]::Max(2,[Math]::Round($width));$gap=[Math]::Max(1,[Math]::Round($gap));$bottom=[Math]::Round($bottom) }
    $start=($size-(3*$width+2*$gap))/2
    if($snap) { $start=[Math]::Floor($start) }
    $white=[Drawing.SolidBrush]::new([Drawing.Color]::White)
    for($i=0;$i -lt 3;$i++) {
        $h=[single]($size*$heights[$i]);if($snap) { $h=[Math]::Round($h) }
        $bar=New-RoundedPath ($left+$start+$i*($width+$gap)) ($top+$bottom-$h) $width $h $(if($snap) { 0 } else { [single]($width*0.3) })
        $g.FillPath($white,$bar);$bar.Dispose()
    }
    $white.Dispose()
}

function New-Canvas([int]$width,[int]$height) {
    $bitmap=[Drawing.Bitmap]::new($width,$height,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g=[Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality=[Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([Drawing.Color]::Transparent)
    return $bitmap,$g
}

# $scale is the glyph size relative to the shorter side; 1 fills it.
function Save-Asset([string]$path,[int]$width,[int]$height,[double]$scale=1) {
    $bitmap,$g=New-Canvas $width $height
    $size=[int][Math]::Round([Math]::Min($width,$height)*$scale)
    Draw-Glyph $g ([single][Math]::Floor(($width-$size)/2)) ([single][Math]::Floor(($height-$size)/2)) $size
    $g.Dispose();$bitmap.Save($path,[Drawing.Imaging.ImageFormat]::Png);$bitmap.Dispose()
}

function Get-IconImage([int]$size) {
    $bitmap,$g=New-Canvas $size $size
    Draw-Glyph $g 0 0 $size;$g.Dispose()
    if($size -ge 256) {
        $stream=[IO.MemoryStream]::new();$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$bitmap.Dispose()
        return ,$stream.ToArray()
    }
    # 32-bit DIB with straight alpha, bottom-up rows, followed by the AND mask.
    $rect=[Drawing.Rectangle]::new(0,0,$size,$size)
    $data=$bitmap.LockBits($rect,[Drawing.Imaging.ImageLockMode]::ReadOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels=[byte[]]::new($size*$size*4)
    for($row=0;$row -lt $size;$row++) { [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0,$row*$data.Stride),$pixels,$row*$size*4,$size*4) }
    $bitmap.UnlockBits($data);$bitmap.Dispose()
    $maskStride=[int]([Math]::Ceiling($size/32)*4)
    $stream=[IO.MemoryStream]::new();$writer=[IO.BinaryWriter]::new($stream)
    $writer.Write([int]40);$writer.Write([int]$size);$writer.Write([int]($size*2));$writer.Write([int16]1);$writer.Write([int16]32)
    $writer.Write([int]0);$writer.Write([int]($size*$size*4+$maskStride*$size));$writer.Write([int]0);$writer.Write([int]0);$writer.Write([int]0);$writer.Write([int]0)
    for($row=$size-1;$row -ge 0;$row--) { $writer.Write($pixels,$row*$size*4,$size*4) }
    for($row=$size-1;$row -ge 0;$row--) {
        $mask=[byte[]]::new($maskStride)
        for($x=0;$x -lt $size;$x++) { if($pixels[($row*$size+$x)*4+3] -eq 0) { $mask[[Math]::Floor($x/8)]=$mask[[Math]::Floor($x/8)] -bor (0x80 -shr ($x%8)) } }
        $writer.Write($mask)
    }
    $writer.Flush();return ,$stream.ToArray()
}

# Application icon (exe, windows and tray).
$iconSizes=@(16,20,24,32,40,48,64,256)
$images=@($iconSizes | ForEach-Object { ,(Get-IconImage $_) })
$icon=[IO.MemoryStream]::new();$writer=[IO.BinaryWriter]::new($icon)
$writer.Write([int16]0);$writer.Write([int16]1);$writer.Write([int16]$iconSizes.Count)
$offset=6+16*$iconSizes.Count
for($i=0;$i -lt $iconSizes.Count;$i++) {
    $edge=if($iconSizes[$i] -ge 256) { 0 } else { $iconSizes[$i] }
    $writer.Write([byte]$edge);$writer.Write([byte]$edge);$writer.Write([byte]0);$writer.Write([byte]0)
    $writer.Write([int16]1);$writer.Write([int16]32);$writer.Write([int]$images[$i].Length);$writer.Write([int]$offset)
    $offset+=$images[$i].Length
}
foreach($image in $images) { $writer.Write($image) }
$writer.Flush();[IO.File]::WriteAllBytes((Join-Path $appAssets 'AppIcon.ico'),$icon.ToArray())

# MSIX visual assets. Scale variants follow the Windows asset naming convention.
$scales=[ordered]@{ '100'=1.0;'125'=1.25;'150'=1.5;'200'=2.0;'400'=4.0 }
foreach($scale in $scales.GetEnumerator()) {
    $factor=$scale.Value;$suffix='scale-'+$scale.Key
    Save-Asset (Join-Path $msixAssets "StoreLogo.$suffix.png") ([Math]::Round(50*$factor)) ([Math]::Round(50*$factor))
    Save-Asset (Join-Path $msixAssets "Square44x44Logo.$suffix.png") ([Math]::Round(44*$factor)) ([Math]::Round(44*$factor))
    Save-Asset (Join-Path $msixAssets "Square150x150Logo.$suffix.png") ([Math]::Round(150*$factor)) ([Math]::Round(150*$factor)) 0.6
    Save-Asset (Join-Path $msixAssets "Wide310x150Logo.$suffix.png") ([Math]::Round(310*$factor)) ([Math]::Round(150*$factor)) 0.6
    Save-Asset (Join-Path $msixAssets "SplashScreen.$suffix.png") ([Math]::Round(620*$factor)) ([Math]::Round(300*$factor)) 0.5
}
foreach($target in 16,24,32,48,256) {
    Save-Asset (Join-Path $msixAssets "Square44x44Logo.targetsize-$target.png") $target $target
    Save-Asset (Join-Path $msixAssets "Square44x44Logo.targetsize-$target`_altform-unplated.png") $target $target
}

# Optional Partner Center listing logos (1:1 app tile icon and box art).
Save-Asset (Join-Path $storeAssets 'AppTileIcon-300.png') 300 300
Save-Asset (Join-Path $storeAssets 'BoxArt-1080.png') 1080 1080 0.7
Get-ChildItem -LiteralPath $appAssets,$msixAssets,$storeAssets -File | Measure-Object | ForEach-Object { "Generated $($_.Count) files." }
