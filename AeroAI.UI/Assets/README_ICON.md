# AeroAI Icon Setup

The application icon file (`AeroAI.ico`) is required for proper taskbar display in Windows.

## Current Status

A PNG file (`AeroAI.png`) has been generated. To create the proper `.ico` file:

### Option 1: Online Converter (Easiest)
1. Go to https://convertio.co/png-ico/ or https://www.ico-convert.com/
2. Upload `AeroAI.png`
3. Download the `.ico` file
4. Save it as `AeroAI.ico` in this directory

### Option 2: ImageMagick (If Installed)
```powershell
magick convert AeroAI.png -define icon:auto-resize=256,128,64,48,32,16 AeroAI.ico
```

### Option 3: Visual Studio
1. Right-click on `AeroAI.png` in Solution Explorer
2. Select "Convert to Icon"
3. Save as `AeroAI.ico`

### Option 4: PowerShell (Using .NET)
Run the `CreateIcon.ps1` script which will generate both PNG and attempt to create ICO.

## Icon Requirements

For best results, the `.ico` file should contain multiple sizes:
- 16x16 (taskbar small icons)
- 32x32 (taskbar normal icons)
- 48x48 (desktop icons)
- 64x64
- 128x128
- 256x256 (high DPI)

The current PNG is 256x256 and can be resized to create all required sizes.

