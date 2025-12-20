$ErrorActionPreference = "Stop"

$python = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
if (!(Test-Path $python)) { throw "Venv python not found: $python" }

& $python -m pip install -r requirements.txt
& $python -m uvicorn xtts_service.app:app --host 127.0.0.1 --port 8008 --reload
