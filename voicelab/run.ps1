$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path ".venv")) {
    python -m venv .venv
}

$venvActivate = ".\.venv\Scripts\Activate.ps1"
& $venvActivate

python -m pip install --upgrade pip | Out-Null
python -m pip install -r requirements.txt

$env:PYTHONPATH = if ($env:PYTHONPATH) { "$($env:PYTHONPATH);." } else { "." }
uvicorn xtts_service.app:app --host 127.0.0.1 --port 8008 --reload
