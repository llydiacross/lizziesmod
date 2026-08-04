Add-Type -AssemblyName System.Drawing

function New-BackroomsTerminalIcon {
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
                [System.Drawing.Color]::FromArgb(255, 47, 45, 30),
                [System.Drawing.Color]::FromArgb(255, 19, 20, 14))
            $panelPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 172, 155, 74), 2)
            $graphics.FillRectangle($panelBrush, 7, 7, 146, 146)
            $graphics.DrawRectangle($panelPen, 7, 7, 146, 146)
            $panelBrush.Dispose()
            $panelPen.Dispose()
        }

        $shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(85, 0, 0, 0))
        $graphics.FillEllipse($shadowBrush, 29, 136, 103, 12)
        $shadowBrush.Dispose()

        $cablePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 33, 37, 32), 5)
        $cablePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $cablePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($cablePen, 58, 35, 42, 17)
        $graphics.DrawLine($cablePen, 103, 35, 119, 16)
        $cablePen.Dispose()

        $housingPoints = [System.Drawing.Point[]]@(
            [System.Drawing.Point]::new(39, 32),
            [System.Drawing.Point]::new(111, 27),
            [System.Drawing.Point]::new(126, 42),
            [System.Drawing.Point]::new(120, 132),
            [System.Drawing.Point]::new(44, 139),
            [System.Drawing.Point]::new(31, 123),
            [System.Drawing.Point]::new(32, 48))
        $housingBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Point]::new(31, 27),
            [System.Drawing.Point]::new(125, 139),
            [System.Drawing.Color]::FromArgb(255, 100, 96, 54),
            [System.Drawing.Color]::FromArgb(255, 42, 44, 30))
        $housingPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 20, 23, 18), 4)
        $graphics.FillPolygon($housingBrush, $housingPoints)
        $graphics.DrawPolygon($housingPen, $housingPoints)
        $housingBrush.Dispose()
        $housingPen.Dispose()

        $edgeHighlight = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(160, 201, 191, 114), 2)
        $graphics.DrawLine($edgeHighlight, 40, 34, 110, 29)
        $edgeHighlight.Dispose()

        $screenFrameBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 21, 25, 17))
        $screenFramePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 207, 189, 93), 2)
        $graphics.FillRectangle($screenFrameBrush, 45, 47, 62, 45)
        $graphics.DrawRectangle($screenFramePen, 45, 47, 62, 45)
        $screenFrameBrush.Dispose()
        $screenFramePen.Dispose()

        $screenBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Point]::new(50, 51),
            [System.Drawing.Point]::new(102, 87),
            [System.Drawing.Color]::FromArgb(255, 244, 236, 150),
            [System.Drawing.Color]::FromArgb(255, 108, 111, 53))
        $graphics.FillRectangle($screenBrush, 50, 52, 52, 35)
        $screenBrush.Dispose()

        $mazePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 93, 88, 37), 3)
        $graphics.DrawLine($mazePen, 54, 58, 74, 58)
        $graphics.DrawLine($mazePen, 74, 58, 74, 68)
        $graphics.DrawLine($mazePen, 60, 68, 90, 68)
        $graphics.DrawLine($mazePen, 60, 68, 60, 80)
        $graphics.DrawLine($mazePen, 84, 68, 84, 80)
        $graphics.DrawLine($mazePen, 70, 80, 96, 80)
        $mazePen.Dispose()

        $glowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(100, 255, 235, 111))
        $graphics.FillEllipse($glowBrush, 66, 56, 18, 18)
        $glowBrush.Dispose()

        $switchPlateBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 33, 36, 25))
        $switchPlatePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 155, 147, 73), 1)
        $graphics.FillRectangle($switchPlateBrush, 47, 98, 58, 25)
        $graphics.DrawRectangle($switchPlatePen, 47, 98, 58, 25)
        $switchPlateBrush.Dispose()
        $switchPlatePen.Dispose()

        $buttonBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 91, 104, 67))
        for ($buttonX = 53; $buttonX -le 83; $buttonX += 10) {
            $graphics.FillRectangle($buttonBrush, $buttonX, 104, 6, 6)
        }
        $buttonBrush.Dispose()

        $lampGlow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(100, 255, 220, 69))
        $graphics.FillEllipse($lampGlow, 88, 101, 14, 14)
        $lampGlow.Dispose()
        $lampBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 230, 94))
        $graphics.FillEllipse($lampBrush, 91, 104, 8, 8)
        $lampBrush.Dispose()

        $ventPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 22, 25, 17), 2)
        for ($ventY = 116; $ventY -le 127; $ventY += 4) {
            $graphics.DrawLine($ventPen, 55, $ventY, 99, $ventY)
        }
        $ventPen.Dispose()

        $footPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 25, 27, 20), 6)
        $graphics.DrawLine($footPen, 51, 137, 45, 144)
        $graphics.DrawLine($footPen, 112, 132, 117, 141)
        $footPen.Dispose()

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
    [PSCustomObject]@{ Path = (Join-Path $modRoot 'UIAtlases\ItemIconAtlas\backroomsAccessTerminal.png'); WithPanel = $false },
    [PSCustomObject]@{ Path = (Join-Path $modRoot 'UIAtlases\UIAtlas\backroomsAccessTerminal.png'); WithPanel = $true },
    [PSCustomObject]@{ Path = (Join-Path $modRoot 'atlas.png'); WithPanel = $true }
)

foreach ($target in $targets) {
    $directory = Split-Path -Parent $target.Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $bitmap = New-BackroomsTerminalIcon $target.WithPanel
    try {
        $bitmap.Save($target.Path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "Generated $($target.Path)"
    }
    finally {
        $bitmap.Dispose()
    }
}