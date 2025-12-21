import unittest

from xtts_service.tts_pronunciation import apply_pronunciation_map, normalize_diacritics


class PronunciationTests(unittest.TestCase):
    def test_normalize_diacritics(self):
        self.assertEqual(normalize_diacritics("Düsseldorf"), "Duesseldorf")

    def test_word_boundary(self):
        mapping = {"Dusseldorf": "Duesseldorf"}
        text = "Dusseldorfian"
        self.assertEqual(apply_pronunciation_map(text, mapping), text)

    def test_pipes_preserved(self):
        mapping = {"Duesseldorf": "DOO-sel-dorf"}
        text = "Taxi to Duesseldorf | ready"
        output = apply_pronunciation_map(normalize_diacritics(text), mapping)
        self.assertEqual(output.count("|"), 1)


if __name__ == "__main__":
    unittest.main()
