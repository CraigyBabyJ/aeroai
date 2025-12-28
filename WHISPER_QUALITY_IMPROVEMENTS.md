# Whisper Transcription Quality Improvements

## Changes Made

### 1. Increased Transcription Quality Settings (`whisper-fast/service.py`)
- **beam_size**: Increased from `1` to `5` (better accuracy, more thorough search)
- **best_of**: Increased from `1` to `5` (better sampling diversity)
- **condition_on_previous_text**: Changed from `False` to `True` (uses context from previous transcriptions)

**Impact**: These changes significantly improve transcription accuracy, especially for ATC-specific terminology and callsigns. The trade-off is slightly slower transcription (still very fast with faster-whisper).

### 2. Added Callsign/Airline Context to Initial Prompt (`AeroAI.UI/MainWindow.xaml.cs`)
- Added `Callsign` (spoken form, e.g., "Air Canada 223")
- Added `Airline` (e.g., "Air Canada")
- Added `Callsign ICAO` (raw form, e.g., "ACA223") if different from spoken form

**Impact**: Whisper now has context about the expected callsign and airline, which helps it correctly recognize "Air Canada" instead of mishearing it as "read your checker commin" or similar.

## Optional: Upgrade to Larger Model

The current model is `jacktol/whisper-medium.en-fine-tuned-for-ATC-faster-whisper` (medium size).

For even better accuracy, you can upgrade to a larger model:

### Option 1: Large-v3 (Best Quality)
- Model: `openai/whisper-large-v3` or `Systran/faster-whisper-large-v3`
- Requires: ~10GB VRAM (GPU) or ~10GB RAM (CPU)
- Quality: Excellent, best for ATC audio

### Option 2: Large-v2 (Good Quality)
- Model: `openai/whisper-large-v2` or `Systran/faster-whisper-large-v2`
- Requires: ~10GB VRAM (GPU) or ~10GB RAM (CPU)
- Quality: Very good

### Option 3: Keep Current Model (Balanced)
- Current: `jacktol/whisper-medium.en-fine-tuned-for-ATC-faster-whisper`
- Requires: ~5GB VRAM (GPU) or ~5GB RAM (CPU)
- Quality: Good, ATC-specific fine-tuning

**To upgrade**: Set `WHISPER_FAST_MODEL` in `.env`:
```
WHISPER_FAST_MODEL=openai/whisper-large-v3
```

Or edit `whisper-fast/start.ps1` line 33.

## Testing

After restarting the whisper-fast service, test with:
1. "Air Canada two two three radio check" - should correctly transcribe "Air Canada"
2. Clearance requests with callsigns - should recognize airline names correctly
3. Check console logs for transcription quality improvements

## Performance Notes

- **beam_size=5, best_of=5**: ~2-3x slower than beam_size=1, but still very fast (<1 second for typical ATC transmissions)
- **condition_on_previous_text=True**: Slightly slower but provides better context awareness
- If transcription becomes too slow, you can reduce `beam_size` to `3` and `best_of` to `3` as a compromise

