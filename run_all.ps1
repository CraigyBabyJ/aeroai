# run_all.ps1 - Start all AeroAI components

Write-Host "Starting AeroAI components..." -ForegroundColor Cyan

# Check if we are in the root directory
if (!(Test-Path "AeroAI.sln")) {
    Write-Error "Please run this script from the root of the repository (where AeroAI.sln is)."
    exit 1
}

# 1. Start VoiceLab (TTS)
Write-Host "Launching VoiceLab (TTS) in a new window..." -ForegroundColor Green
Start-Process powershell -ArgumentList "-NoExit", "-Command", "& { cd voicelab; ./run.ps1 }"

# 2. Start Whisper-Fast (STT)
Write-Host "Launching Whisper-Fast (STT) in a new window..." -ForegroundColor Green
Start-Process powershell -ArgumentList "-NoExit", "-Command", "& { cd whisper-fast; ./start.ps1 }"

# 3. Start AeroAI UI
Write-Host "Building and Starting AeroAI UI..." -ForegroundColor Green
dotnet run --project AeroAI.UI/AeroAI.UI.csproj
