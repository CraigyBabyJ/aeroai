# Create a minimal ICO file from PNG
# This creates a basic ICO structure that Windows can use

param(
    [string]$PngPath = "AeroAI.png",
    [string]$IcoPath = "AeroAI.ico"
)

Add-Type -AssemblyName System.Drawing

$pngFullPath = Join-Path $PSScriptRoot $PngPath
$icoFullPath = Join-Path $PSScriptRoot $IcoPath

if (-not (Test-Path $pngFullPath)) {
    Write-Error "PNG file not found: $pngFullPath"
    exit 1
}

# Load the PNG
$img = [System.Drawing.Image]::FromFile($pngFullPath)
$originalWidth = $img.Width
$originalHeight = $img.Height

# Create ICO file structure
# ICO header: 6 bytes
# Icon directory entry: 16 bytes per entry
# Icon data: PNG data for each size

$sizes = @(256, 128, 64, 48, 32, 16)
$iconEntries = New-Object System.Collections.ArrayList
$allData = New-Object System.Collections.ArrayList

$dataOffset = 6 + ($sizes.Count * 16)  # Header + directory entries

foreach ($size in $sizes) {
    # Resize image
    $resized = New-Object System.Drawing.Bitmap($img, $size, $size)
    
    # Save as PNG in memory
    $ms = New-Object System.IO.MemoryStream
    $resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData = $ms.ToArray()
    $ms.Close()
    $resized.Dispose()
    
    # Create directory entry
    $entry = @{
        Width = if ($size -eq 256) { 0 } else { $size }
        Height = if ($size -eq 256) { 0 } else { $size }
        ColorPlanes = 0
        BitsPerPixel = 32
        Size = $pngData.Length
        Offset = $dataOffset
        Data = $pngData
    }
    
    [void]$iconEntries.Add($entry)
    [void]$allData.Add($pngData)
    $dataOffset += $pngData.Length
}

$img.Dispose()

# Write ICO file
$icoStream = [System.IO.File]::Create($icoFullPath)
$writer = New-Object System.IO.BinaryWriter($icoStream)

# Write ICO header
$writer.Write([UInt16]0)  # Reserved (must be 0)
$writer.Write([UInt16]1)  # Type (1 = ICO)
$writer.Write([UInt16]$sizes.Count)  # Number of images

# Write directory entries
foreach ($entry in $iconEntries) {
    $writer.Write([Byte]$entry.Width)
    $writer.Write([Byte]$entry.Height)
    $writer.Write([Byte]0)  # Color palette (0 = no palette)
    $writer.Write([Byte]0)  # Reserved
    $writer.Write([UInt16]$entry.ColorPlanes)
    $writer.Write([UInt16]$entry.BitsPerPixel)
    $writer.Write([UInt32]$entry.Size)
    $writer.Write([UInt32]$entry.Offset)
}

# Write image data
foreach ($data in $allData) {
    $writer.Write($data)
}

$writer.Close()
$icoStream.Close()

Write-Host "Successfully created $icoFullPath"
Write-Host "The icon file contains $($sizes.Count) sizes: $($sizes -join ', ')"

