import unittest

from xtts_service.segmentation import segment_text, split_segments


class TestSegmentation(unittest.TestCase):
    def test_pipe_splitting(self):
        text = "Easy one two three | radio check | wind two seven zero at one two knots"
        res = segment_text(text)
        self.assertEqual(res.delimiter, "pipe")
        self.assertEqual(len(res.segments), 3)
        self.assertEqual(res.segments[0], "Easy one two three")
        self.assertEqual(res.segments[1], "radio check")

    def test_newline_splitting(self):
        text = "one two three\nradio check\nwind calm"
        res = segment_text(text)
        self.assertEqual(res.delimiter, "newline")
        self.assertEqual(res.segments, ["one two three", "radio check", "wind calm"])

    def test_punct_splitting_keeps_punct(self):
        text = "one two three. radio check! wind calm;"
        res = segment_text(text)
        self.assertEqual(res.delimiter, "punct")
        self.assertEqual(res.segments, ["one two three.", "radio check!", "wind calm;"])

    def test_long_segment_splits_at_words(self):
        words = ["alpha"] * 80
        text = " ".join(words)
        res = segment_text(text)
        self.assertGreater(len(res.segments), 1)
        self.assertTrue(all(len(s) <= 220 for s in res.segments))

    def test_split_segments_signature(self):
        segs = split_segments("a | b | c")
        self.assertEqual(segs, ["a", "b", "c"])


if __name__ == "__main__":
    unittest.main()

