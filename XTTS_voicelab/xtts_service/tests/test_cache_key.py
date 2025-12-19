import tempfile
import unittest
from pathlib import Path
import io
import wave
from array import array

from xtts_service.cache import TtsCache


class TestCacheKey(unittest.TestCase):
    def test_role_in_key(self):
        with tempfile.TemporaryDirectory() as td:
            with TtsCache(base_dir=Path(td), model_version="xtts_v2") as cache:
                key_tower = cache.make_key(
                    model_version="xtts_v2",
                    voice_id="uk_male_1",
                    role="tower",
                    radio_profile=None,
                    speed=1.0,
                    language="en",
                    text_norm="easy one two three",
                )
                key_ground = cache.make_key(
                    model_version="xtts_v2",
                    voice_id="uk_male_1",
                    role="ground",
                    radio_profile=None,
                    speed=1.0,
                    language="en",
                    text_norm="easy one two three",
                )
                self.assertNotEqual(key_tower, key_ground)

    def test_language_in_key(self):
        with tempfile.TemporaryDirectory() as td:
            with TtsCache(base_dir=Path(td), model_version="xtts_v2") as cache:
                key_en = cache.make_key(
                    model_version="xtts_v2",
                    voice_id="uk_male_1",
                    role="tower",
                    radio_profile=None,
                    speed=1.0,
                    language="en",
                    text_norm="easy one two three",
                )
                key_fr = cache.make_key(
                    model_version="xtts_v2",
                    voice_id="uk_male_1",
                    role="tower",
                    radio_profile=None,
                    speed=1.0,
                    language="fr",
                    text_norm="easy one two three",
                )
                self.assertNotEqual(key_en, key_fr)

    def test_segment_cache_hit_and_role_miss(self):
        def make_wav(sample_value: int) -> bytes:
            buf = io.BytesIO()
            with wave.open(buf, "wb") as w:
                w.setnchannels(1)
                w.setsampwidth(2)
                w.setframerate(22050)
                w.writeframes(array("h", [sample_value] * 200).tobytes())
            return buf.getvalue()

        with tempfile.TemporaryDirectory() as td:
            with TtsCache(base_dir=Path(td), model_version="xtts_v2") as cache:
                segments = ["easy one two three", "radio check", "wind calm"]

                first = [
                    cache.get_or_generate(
                        model_version="xtts_v2",
                        voice_id="uk_male_1",
                        role="tower",
                        radio_profile=None,
                        speed=1.0,
                        language="en",
                        text=s,
                        generator=lambda v=i: make_wav(1000 + v),
                        return_audio=False,
                    )
                    for i, s in enumerate(segments)
                ]
                self.assertTrue(all(not r["from_cache"] for r in first))

                second = [
                    cache.get_or_generate(
                        model_version="xtts_v2",
                        voice_id="uk_male_1",
                        role="tower",
                        radio_profile=None,
                        speed=1.0,
                        language="en",
                        text=s,
                        generator=lambda: (_ for _ in ()).throw(AssertionError("should hit cache")),
                        return_audio=False,
                    )
                    for s in segments
                ]
                self.assertTrue(all(r["from_cache"] for r in second))

                role_changed = cache.get_or_generate(
                    model_version="xtts_v2",
                    voice_id="uk_male_1",
                    role="ground",
                    radio_profile=None,
                    speed=1.0,
                    language="en",
                    text=segments[0],
                    generator=lambda: make_wav(9999),
                    return_audio=False,
                )
                self.assertFalse(role_changed["from_cache"])


if __name__ == "__main__":
    unittest.main()
