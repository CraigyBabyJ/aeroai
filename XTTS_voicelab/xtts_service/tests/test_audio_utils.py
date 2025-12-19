import io
import unittest
import wave
from array import array

from xtts_service.audio_utils import stitch_wavs, adjust_speed


def _make_wav(samples, sample_rate=22050):
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sample_rate)
        w.writeframes(array("h", samples).tobytes())
    return buf.getvalue()


class TestAudioUtils(unittest.TestCase):
    def test_stitch_inserts_silence(self):
        sr = 22050
        wav1 = _make_wav([1000] * 2000, sample_rate=sr)
        wav2 = _make_wav([2000] * 1000, sample_rate=sr)
        out = stitch_wavs([wav1, wav2], pause_ms=140)

        with wave.open(io.BytesIO(out), "rb") as w:
            self.assertEqual(w.getnchannels(), 1)
            self.assertEqual(w.getsampwidth(), 2)
            self.assertEqual(w.getframerate(), sr)
            frames = w.readframes(w.getnframes())

        out_samples = array("h", frames)
        silence_len = int(sr * 0.14)
        self.assertGreaterEqual(len(out_samples), 2000 + silence_len + 1000)
        silence_start = 2000
        silence_end = 2000 + silence_len
        self.assertTrue(all(s == 0 for s in out_samples[silence_start:silence_end]))

    def test_adjust_speed_fast(self):
        sr = 22050
        wav = _make_wav([1000] * 2000, sample_rate=sr)
        faster = adjust_speed(wav, speed=1.5)
        with wave.open(io.BytesIO(faster), "rb") as w:
            self.assertEqual(w.getframerate(), sr)
            frames = w.getnframes()
        self.assertLess(frames, 2000)  # fewer frames => faster playback

    def test_adjust_speed_slow(self):
        sr = 22050
        wav = _make_wav([1000] * 2000, sample_rate=sr)
        slower = adjust_speed(wav, speed=0.5)
        with wave.open(io.BytesIO(slower), "rb") as w:
            self.assertEqual(w.getframerate(), sr)
            frames = w.getnframes()
        self.assertGreater(frames, 2000)  # more frames => slower playback


if __name__ == "__main__":
    unittest.main()
