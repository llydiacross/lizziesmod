Add-Type -AssemblyName System.Drawing

function New-PocketTelevisionIcon {
    param([bool] $WithPanel)

    $bitmap = [System.Drawing.Bitmap]::new(
        160,
        160,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        if ($WithPanel) {
            $panelBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                [System.Drawing.Point]::new(0, 0),
                [System.Drawing.Point]::new(160, 160),
                [System.Drawing.Color]::FromArgb(255, 17, 25, 36),
                [System.Drawing.Color]::FromArgb(255, 7, 11, 18))
            $panelPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(220, 69, 112, 136), 2)
            $graphics.FillRectangle($panelBrush, 7, 7, 146, 146)
            $graphics.DrawRectangle($panelPen, 7, 7, 146, 146)
            $panelBrush.Dispose()
            $panelPen.Dispose()
        }

        $shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(80, 0, 0, 0))
        $graphics.FillEllipse($shadowBrush, 23, 133, 116, 13)
        $shadowBrush.Dispose()

        $antennaPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 70, 82, 91), 4)
        $antennaPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $antennaPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($antennaPen, 76, 40, 57, 22)
        $graphics.DrawLine($antennaPen, 84, 40, 103, 20)
        $antennaPen.Dispose()

        $bodyPoints = [System.Drawing.Point[]]@(
            [System.Drawing.Point]::new(25, 44),
            [System.Drawing.Point]::new(119, 35),
            [System.Drawing.Point]::new(137, 50),
            [System.Drawing.Point]::new(132, 124),
            [System.Drawing.Point]::new(35, 133),
            [System.Drawing.Point]::new(21, 118))
        $bodyBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Point]::new(20, 40),
            [System.Drawing.Point]::new(135, 135),
            [System.Drawing.Color]::FromArgb(255, 55, 64, 70),
            [System.Drawing.Color]::FromArgb(255, 19, 25, 31))
        $bodyPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 8, 11, 15), 4)
        $graphics.FillPolygon($bodyBrush, $bodyPoints)
        $graphics.DrawPolygon($bodyPen, $bodyPoints)
        $bodyBrush.Dispose()
        $bodyPen.Dispose()

        $sidePoints = [System.Drawing.Point[]]@(
            [System.Drawing.Point]::new(119, 35),
            [System.Drawing.Point]::new(137, 50),
            [System.Drawing.Point]::new(132, 124),
            [System.Drawing.Point]::new(115, 116))
        $sideBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 29, 36, 42))
        $graphics.FillPolygon($sideBrush, $sidePoints)
        $sideBrush.Dispose()

        $topHighlight = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(170, 126, 147, 155), 2)
        $graphics.DrawLine($topHighlight, 28, 45, 118, 36)
        $topHighlight.Dispose()

        $bezelBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 9, 14, 18))
        $bezelPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 111, 128, 132), 2)
        $graphics.FillRectangle($bezelBrush, 32, 51, 79, 53)
        $graphics.DrawRectangle($bezelPen, 32, 51, 79, 53)
        $bezelBrush.Dispose()
        $bezelPen.Dispose()

        $screenBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Point]::new(38, 56),
            [System.Drawing.Point]::new(104, 98),
            [System.Drawing.Color]::FromArgb(255, 11, 54, 67),
            [System.Drawing.Color]::FromArgb(255, 8, 17, 33))
        $graphics.FillRectangle($screenBrush, 38, 57, 67, 39)
        $screenBrush.Dispose()

        $scanPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(70, 142, 238, 255), 1)
        for ($scanY = 59; $scanY -lt 96; $scanY += 4) {
            $graphics.DrawLine($scanPen, 40, $scanY, 102, $scanY)
        }
        $scanPen.Dispose()

        $glowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(70, 55, 219, 255))
        $graphics.FillEllipse($glowBrush, 48, 61, 45, 30)
        $glowBrush.Dispose()

        $cyanPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 86, 235, 255), 3)
        $magentaPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 255, 74, 202), 3)
        $cyanPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $cyanPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $magentaPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $magentaPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawArc($cyanPen, 49, 61, 43, 30, 200, 245)
        $graphics.DrawArc($magentaPen, 56, 65, 29, 22, 20, 250)
        $graphics.DrawArc($cyanPen, 62, 69, 16, 14, 205, 235)
        $cyanPen.Dispose()
        $magentaPen.Dispose()

        $sparkBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 217, 103))
        $graphics.FillEllipse($sparkBrush, 79, 62, 4, 4)
        $graphics.FillEllipse($sparkBrush, 50, 84, 3, 3)
        $graphics.FillEllipse($sparkBrush, 90, 82, 3, 3)
        $sparkBrush.Dispose()

        $controlBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 35, 42, 46))
        $controlPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 114, 128, 129), 1)
        $graphics.FillRectangle($controlBrush, 39, 103, 67, 12)
        $graphics.DrawRectangle($controlPen, 39, 103, 67, 12)
        $controlBrush.Dispose()
        $controlPen.Dispose()

        $dialBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 224, 151, 58))
        $graphics.FillEllipse($dialBrush, 44, 106, 7, 7)
        $graphics.FillEllipse($dialBrush, 55, 106, 7, 7)
        $graphics.FillEllipse($dialBrush, 94, 105, 8, 8)
        $dialBrush.Dispose()

        $labelBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 180, 242, 246))
        $font = [System.Drawing.Font]::new('Segoe UI', 7, [System.Drawing.FontStyle]::Bold)
        $graphics.DrawString('FLUX', $font, $labelBrush, 66, 105)
        $font.Dispose()
        $labelBrush.Dispose()

        $legPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 12, 16, 19), 6)
        $graphics.DrawLine($legPen, 43, 130, 37, 141)
        $graphics.DrawLine($legPen, 118, 128, 123, 139)
        $legPen.Dispose()

        return $bitmap
    }
    catch {
        $bitmap.Dispose()
        throw
    }
    finally {
        $graphics.Dispose()
    }
}

$modRoot = Split-Path -Parent $PSScriptRoot
$targets = @(
    [PSCustomObject]@{ Path = (Join-Path $modRoot 'UIAtlases\ItemIconAtlas\pocketDimensionPortal.png'); WithPanel = $false },
    [PSCustomObject]@{ Path = (Join-Path $modRoot 'UIAtlases\UIAtlas\pocketDimensionPortal.png'); WithPanel = $true },
    [PSCustomObject]@{ Path = (Join-Path $modRoot 'atlas.png'); WithPanel = $true }
)

foreach ($target in $targets) {
    $directory = Split-Path -Parent $target.Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $bitmap = New-PocketTelevisionIcon $target.WithPanel
    try {
        $bitmap.Save($target.Path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "Generated $($target.Path)"
    }
    finally {
        $bitmap.Dispose()
    }
}