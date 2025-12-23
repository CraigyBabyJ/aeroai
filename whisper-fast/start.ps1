$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$envFile = Join-Path $repoRoot ".env"

Set-Location $scriptRoot

if (Test-Path $envFile) {
    foreach ($line in Get-Content $envFile) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) {
            continue
        }
        $parts = $trimmed -split "=", 2
        if ($parts.Length -ne 2) {
            continue
        }
        $key = $parts[0].Trim()
        if (-not $key.StartsWith("WHISPER_FAST_")) {
            continue
        }
        $value = $parts[1].Trim()
        if ($value.StartsWith('"') -and $value.EndsWith('"')) {
            $value = $value.Substring(1, $value.Length - 2)
        } elseif ($value.StartsWith("'") -and $value.EndsWith("'")) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        Set-Item -Path "Env:$key" -Value $value
    }
}

if (-not $env:WHISPER_FAST_MODEL) {
    $env:WHISPER_FAST_MODEL = "jacktol/whisper-medium.en-fine-tuned-for-ATC-faster-whisper"
}
if (-not $env:WHISPER_FAST_PORT) {
    $env:WHISPER_FAST_PORT = "8766"
}
if (-not $env:WHISPER_FAST_DEVICE) {
    $env:WHISPER_FAST_DEVICE = "cuda"
}
if (-not $env:WHISPER_FAST_COMPUTE_TYPE) {
    $env:WHISPER_FAST_COMPUTE_TYPE = "auto"
}

$python = Join-Path $scriptRoot "venv311\\Scripts\\python.exe"
if (-not (Test-Path $python)) {
    Write-Host "[whisper-fast] venv311 not found; falling back to python on PATH"
    $python = "python"
}

& $python -c "import faster_whisper" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "[whisper-fast] faster-whisper missing; installing requirements..."
    & $python -m pip install -r (Join-Path $scriptRoot "requirements.txt")
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[whisper-fast] install failed; fix pip/venv and retry."
        exit 1
    }
}

$modelArg = $env:WHISPER_FAST_MODEL.Trim()
$portArg = $env:WHISPER_FAST_PORT.Trim()
Write-Host "[whisper-fast] using model=$modelArg port=$portArg device=$env:WHISPER_FAST_DEVICE compute=$env:WHISPER_FAST_COMPUTE_TYPE"

& $python (Join-Path $scriptRoot "service.py") --port $portArg --model $modelArg
