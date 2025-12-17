# AeroAI

AeroAI is a Windows .NET 8 ATC copilot with a WPF desktop UI, SimBrief ingest, STT/TTS audio pipeline, and OpenAI-based phrase engine.

## Highlights
- WPF desktop client with push-to-talk mic capture (whisper-cli by default, optional whisper-fast HTTP service), typed input, and per-device audio controls.
- SimBrief import plus navdata-aware runway/SID selection, live METAR/ATIS letter caching via CheckWX, and airport/airline name resolution.
- OpenAI phrase engine with guard rails (ReadbackValidator, AtcResponseValidator, spoken-number/callsign/aircraft normalizers) to stop hallucinated runways, squawks, or frequencies.
- Optional OpenAI TTS with voice profiles in `voices/`, radio effects from `Config/audio-effects.json`, and per-flight chat logging to `%APPDATA%\\AeroAI\\logs`.
- Deterministic STT correction layer driven by `Config/stt_corrections.json` (hot-reloads when the file changes).

## Requirements
- Windows with .NET 8 SDK
- OpenAI API key in `.env` (defaults to a fine-tuned `OPENAI_MODEL` if you do not set one)
- SimBrief pilot ID; optional CheckWX API key in `checkwx.json` for live METARs/ATIS letters
- Optional navdata SQLite path (`AEROAI_NAVDATA_PATH`) for runway selection
- STT: `whisper-cli.exe` + `whisper/models/ggml-medium.en-q5_0.bin` (default), or Python 3.10+ with `faster-whisper` for whisper-fast

## Setup
1) `copy .env.example .env` and fill `OPENAI_API_KEY` (plus `OPENAI_MODEL` if you want to override the default). Set `STT_BACKEND=whisper-fast` and `WHISPER_FAST_*` if you prefer the faster backend.
2) Place `whisper/whisper-cli.exe` and `whisper/models/ggml-medium.en-q5_0.bin`, or set up whisper-fast (`python -m venv whisper-fast/venv`, activate, `pip install faster-whisper`, adjust `.env` if needed).
3) Add your CheckWX key to `checkwx.json` (format: `{"ApiKey": "…"}`) and, if available, set `AEROAI_NAVDATA_PATH` to your PMDG/navdata SQLite for better runway selection.
4) (Optional) Enable TTS by setting `AEROAI_TTS_ENABLED=true` and OpenAI voice vars; edit `voices/*.json` and `Config/audio-effects.json` to tune radio effects and profiles.
5) Build: `dotnet build AeroAI.sln`.

## Running
- Desktop UI: `dotnet run --project AeroAI.UI`. Enter your SimBrief pilot ID in Settings (or via the SimBrief dialog), import the flight plan, then hold the PTT button or type to talk to ATC. Transcripts and ATC replies are logged under `%APPDATA%\\AeroAI\\logs`.
- Console demo: `dotnet run --project AtcNavDataDemo.csproj` for the clearance-delivery console flow.

## STT/LLM safeguards
- STT: correction rules from `Config/stt_corrections.json` (hot-reload), spoken-number normalizer, aircraft-type resolver, and callsign validator to clean noisy transcripts.
- LLM: `ReadbackValidator` scores pilot readbacks; `AtcResponseValidator` rejects LLM replies that invent runways, squawks, altitudes, frequencies, or procedures and falls back to a safe clearance.
- Weather/navdata cache: `AtisMetarCache` keeps ATIS letters deterministic between runs; runway selection prefers navdata when available.

## Tests
Run unit tests (validators, normalizers, response guards): `dotnet test AtcNavDataDemo.csproj`.
