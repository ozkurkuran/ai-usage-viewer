param([string]$Executable='artifacts/publish/win-x64/AIUsageViewer.exe',[string]$Output='artifacts/store-screenshots',[ValidateSet('en','tr')][string]$Language='en')
# Renders synthetic demo windows at 2x and composes 3840x2160 Microsoft Store screenshots.
# Demo mode never reads user profiles, accounts or the network.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$executablePath=(Resolve-Path -LiteralPath $Executable).Path
$outputPath=[IO.Path]::GetFullPath($Output)
$capture=Join-Path $outputPath 'capture'
$data=Join-Path $outputPath ('data-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $capture -Force | Out-Null
$arguments='--demo --language '+$Language+' --capture-scale 2 --smoke-suite --screenshot "'+$capture+'" --data-dir "'+$data+'"'
$process=Start-Process -FilePath $executablePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(180000)) { throw "Screenshot process timed out (PID $($process.Id))." }
if($process.ExitCode -ne 0) { throw "Screenshot process exited $($process.ExitCode)." }

$width=3840;$height=2160;$margin=170;$gap=120
function Compose([string]$Name,[string[]]$Images) {
    $bitmaps=@($Images | ForEach-Object { [Drawing.Image]::FromFile((Join-Path $capture $_)) })
    try {
        $contentWidth=($bitmaps | Measure-Object Width -Sum).Sum+$gap*($bitmaps.Count-1)
        $contentHeight=($bitmaps | Measure-Object Height -Maximum).Maximum
        # Downscale only; the captures are already rendered at 2x.
        $scale=[Math]::Min(1,[Math]::Min(($width-2*$margin)/$contentWidth,($height-2*$margin)/$contentHeight))
        $canvas=[Drawing.Bitmap]::new($width,$height,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g=[Drawing.Graphics]::FromImage($canvas)
        $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $background=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0,0),[Drawing.Point]::new($width,$height),
            [Drawing.Color]::FromArgb(255,0x0E,0x12,0x1D),[Drawing.Color]::FromArgb(255,0x26,0x1E,0x52))
        $g.FillRectangle($background,0,0,$width,$height);$background.Dispose()
        $labelFont=[Drawing.Font]::new('Segoe UI',28,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
        $labelBrush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,0xC7,0xC1,0xFF))
        $label=if($Language -eq 'en') { 'DEMO DATA' } else { 'ÖRNEK VERİ' }
        $labelWidth=$g.MeasureString($label,$labelFont).Width
        $g.DrawString($label,$labelFont,$labelBrush,[single](($width-$labelWidth)/2),[single]60)
        $labelBrush.Dispose();$labelFont.Dispose()
        $x=($width-$contentWidth*$scale)/2
        foreach($bitmap in $bitmaps) {
            $w=$bitmap.Width*$scale;$h=$bitmap.Height*$scale;$y=($height-$h)/2
            for($i=1;$i -le 12;$i++) {
                $shadow=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(9,0,0,0))
                $g.FillRectangle($shadow,[single]($x-$i*2),[single]($y-$i*2+18),[single]($w+$i*4),[single]($h+$i*4));$shadow.Dispose()
            }
            $g.DrawImage($bitmap,[single]$x,[single]$y,[single]$w,[single]$h)
            $border=[Drawing.Pen]::new([Drawing.Color]::FromArgb(90,0xA9,0xA0,0xFF),2)
            $g.DrawRectangle($border,[single]$x,[single]$y,[single]$w,[single]$h);$border.Dispose()
            $x+=$w+$gap*$scale
        }
        $g.Dispose();$canvas.Save((Join-Path $outputPath "$Name.png"),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
    } finally { $bitmaps | ForEach-Object { $_.Dispose() } }
}
Compose "01-overview-$Language" @('dashboard.png','widget.png')
Compose "02-widget-and-tray-$Language" @('widget.png','tray.png','widget-light-compact-en.png')
Compose "03-project-session-detail-$Language" @('session-detail.png')
Compose "04-openrouter-history-$Language" @('openrouter-history.png')
Compose "05-settings-$Language" @('settings-1.png','settings-3.png')
if(-not $data.StartsWith($outputPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected demo data path.' }
Remove-Item -LiteralPath $data -Recurse -Force
Get-ChildItem -LiteralPath $outputPath -Filter '*.png' | Select-Object Name,Length
