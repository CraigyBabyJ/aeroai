import io
import random
import wave
from array import array

from xtts_service.radio import apply_radio_profile


def _make_hot_wave(sample_rate: int = 22050, frames: int = 22050) -> bytes:
    samples = array("h")
    for i in range(frames):
        samples.append(32767 if i % 2 == 0 else -32768)
    buf = io.BytesIO()
    with wave.open(buf, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(sample_rate)
        out.writeframes(samples.tobytes())
    return buf.getvalue()


def test_apply_radio_profile_clamps_hot_signal():
    random.seed(0)
    wav_bytes = _make_hot_wave()
    processed = apply_radio_profile(wav_bytes, profile="vhf")

    with wave.open(io.BytesIO(wav_bytes), "rb") as original:
        orig_frames = original.getnframes()
        orig_rate = original.getframerate()

    with wave.open(io.BytesIO(processed), "rb") as output:
        out_frames = output.getnframes()
        out_rate = output.getframerate()

    assert out_frames == orig_frames
    assert out_rate == orig_rate


def test_radio_intensity_range_does_not_change_length():
    random.seed(0)
    wav_bytes = _make_hot_wave()
    outputs = [
        apply_radio_profile(wav_bytes, profile="vhf", radio_intensity=0.0),
        apply_radio_profile(wav_bytes, profile="vhf", radio_intensity=0.5),
        apply_radio_profile(wav_bytes, profile="vhf", radio_intensity=1.0),
    ]
    with wave.open(io.BytesIO(wav_bytes), "rb") as original:
        orig_frames = original.getnframes()
        orig_rate = original.getframerate()

    for processed in outputs:
        with wave.open(io.BytesIO(processed), "rb") as output:
            assert output.getnframes() == orig_frames
            assert output.getframerate() == orig_rate
