#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

if [ ! -d ".venv" ]; then
  python -m venv .venv
fi
source .venv/bin/activate
pip install --upgrade pip >/dev/null
pip install -r requirements.txt

export PYTHONPATH="${PYTHONPATH:-.}"
uvicorn xtts_service.app:app --host 127.0.0.1 --port 8008 --reload
