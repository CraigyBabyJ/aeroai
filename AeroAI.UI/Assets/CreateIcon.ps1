# Create a simple icon file for AeroAI
# This script creates an ICO file with multiple sizes

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 32, 48, 64, 128, 256)
$iconPath = "$PSScriptRoot\AeroAI.ico"

# Create a list to hold the icon images
$images = New-Object System.Collections.ArrayList

foreach ($size in $sizes) {
    # Create a bitmap
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    
    # Set high quality rendering
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    
    # Fill background with dark blue (matching the app theme)
    $bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(26, 26, 46))
    $graphics.FillRectangle($bgBrush, 0, 0, $size, $size)
    
    # Draw the paper plane icon in cyan
    $planeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0, 212, 255))
    
    # Scale the path points for the current size
    $scale = $size / 16.0
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(2 * $scale, 8 * $scale),
        [System.Drawing.PointF]::new(12 * $scale, 6 * $scale),
        [System.Drawing.PointF]::new(6 * $scale, 10 * $scale),
        [System.Drawing.PointF]::new(8 * $scale, 14 * $scale),
        [System.Drawing.PointF]::new(10 * $scale, 10 * $scale),
        [System.Drawing.PointF]::new(14 * $scale, 8 * $scale),
        [System.Drawing.PointF]::new(8 * $scale, 4 * $scale)
    )
    
    # Create and fill the polygon
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon($points)
    $graphics.FillPath($planeBrush, $path)
    $path.Dispose()
    
    # Add to images list
    [void]$images.Add($bitmap)
    
    $graphics.Dispose()
}

# Save as ICO file
# Note: .NET doesn't have built-in ICO support, so we'll save as PNG and note that
# For a proper ICO, you'd need a library or external tool
# For now, we'll create a high-quality PNG that can be converted to ICO

# Save the largest size as PNG (can be converted to ICO with external tools)
$largestBitmap = $images[$images.Count - 1]
$pngPath = "$PSScriptRoot\AeroAI.png"
$largestBitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Host "Created AeroAI.png at $pngPath"
Write-Host "Note: To create a proper .ico file, you can:"
Write-Host "1. Use an online converter (e.g., convertio.co, ico-convert.com)"
Write-Host "2. Use ImageMagick: magick convert AeroAI.png -define icon:auto-resize=256,128,64,48,32,16 AeroAI.ico"
Write-Host "3. Use Visual Studio: Add existing image, then right-click > Convert to Icon"

# Cleanup
foreach ($img in $images) {
    $img.Dispose()
}

