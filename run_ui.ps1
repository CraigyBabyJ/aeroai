# run_ui.ps1 - Start AeroAI UI only

# Check if we are in the root directory
if (!(Test-Path "AeroAI.sln")) {
    Write-Error "Please run this script from the root of the repository (where AeroAI.sln is)."
    exit 1
}

Write-Host "Building and Starting AeroAI UI..." -ForegroundColor Green
dotnet run --project AeroAI.UI/AeroAI.UI.csproj
