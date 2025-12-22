# AeroAI

AeroAI is a Windows .NET 8 ATC copilot with a WPF desktop UI, SimBrief ingest, STT/TTS audio pipeline, and an ATC text generator (OpenAI provider by default).

## Highlights
- WPF desktop client with push-to-talk mic capture (whisper-cli by default, optional whisper-fast HTTP service), typed input, and per-device audio controls.
- SimBrief import plus navdata-aware runway/SID selection, live METAR/ATIS letter caching via CheckWX, and airport/airline name resolution.
- ATC response generator with guard rails (ReadbackValidator, AtcResponseValidator, spoken-number/callsign/aircraft normalizers) to stop hallucinated runways, squawks, or frequencies.
- Deterministic ATC session flow driven by JSON packs (intents/flows/templates), including two-step handoffs that only complete on pilot check-in.
- Generator provider selection via `userconfig.json` (`AtcTextProvider`: `openai` or `template`).
- VoiceLab TTS (FastAPI) is the default local TTS backend when enabled; voice selection lives in VoiceLab `meta.json` profiles, and the base URL is stored in `userconfig.json`.
- Deterministic STT correction layer driven by `Config/stt_corrections.json` (hot-reloads when the file changes).

## Requirements
- Windows with .NET 8 SDK
- OpenAI API key in `.env` when `AtcTextProvider` is `openai` (defaults to a fine-tuned `OPENAI_MODEL` if you do not set one)
- SimBrief pilot ID; optional CheckWX API key in a local `checkwx.json` (see `checkwx.example.json`) for live METARs/ATIS letters
- Optional navdata SQLite path (`AEROAI_NAVDATA_PATH`) for runway selection
- STT: `whisper-cli.exe` + `whisper/models/ggml-medium.en-q5_0.bin` (default), or Python 3.10+ with `faster-whisper` for whisper-fast

## Setup
1) `copy .env.example .env` and fill `OPENAI_API_KEY` (plus `OPENAI_MODEL` if you want to override the default). Set `STT_BACKEND=whisper-fast` and `WHISPER_FAST_*` if you prefer the faster backend.
2) (Optional) Set `AtcTextProvider` in `userconfig.json` to `template` if you want a stub ATC generator without OpenAI.
3) Place `whisper/whisper-cli.exe` and `whisper/models/ggml-medium.en-q5_0.bin`, or set up whisper-fast (`python -m venv whisper-fast/venv`, activate, `pip install faster-whisper`, adjust `.env` if needed).
4) Copy `checkwx.example.json` → `checkwx.json` and add your CheckWX key (format: `{"ApiKey": "…"}`); `checkwx.json` is gitignored so it won't be committed. If available, set `AEROAI_NAVDATA_PATH` to your PMDG/navdata SQLite for better runway selection.
5) (Optional) Run VoiceLab TTS: from `voicelab/`, start `python -m uvicorn xtts_service.app:app --host 127.0.0.1 --port 8008`. Adjust the base URL in Settings (stored in `userconfig.json`) if you host it elsewhere.
6) Build: `dotnet build AeroAI.sln`.

## Running
- Desktop UI: `dotnet run --project AeroAI.UI`. Enter your SimBrief pilot ID in Settings (or via the SimBrief dialog), import the flight plan, then hold the PTT button or type to talk to ATC. Transcripts and ATC replies are logged under `%APPDATA%\\AeroAI\\logs`.

## STT/LLM safeguards
- STT: correction rules from `Config/stt_corrections.json` (hot-reload), spoken-number normalizer, aircraft-type resolver, and callsign validator to clean noisy transcripts.
- LLM: `ReadbackValidator` scores pilot readbacks; `AtcResponseValidator` rejects LLM replies that invent runways, squawks, altitudes, frequencies, or procedures and falls back to a safe clearance.
- Weather/navdata cache: `AtisMetarCache` keeps ATIS letters deterministic between runs; runway selection prefers navdata when available.

## Tests
Run unit tests (validators, normalizers, response guards): `dotnet test AeroAI.Tests\\AeroAI.Tests.csproj`.
