import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("markers", Path(__file__).resolve().parents[1] / "tools/inspect-content-markers.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class MarkerTests(unittest.TestCase):
    def test_encodings_counts_offsets_and_missing_marker(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "artifact.bin"
            content = b"Douyin!" * 21 + "Douyin".encode("utf-16-le")
            path.write_bytes(content)
            report = module.inspect_file(path, ["Douyin", "WeChat"])
            self.assertEqual(report["bytes"], len(content))
            ascii_hit, utf16_hit, *missing = report["evidence"]
            self.assertEqual(ascii_hit["count"], 21)
            self.assertEqual(ascii_hit["firstOffsets"], list(range(0, 140, 7)))
            self.assertEqual(utf16_hit["count"], 1)
            self.assertEqual(utf16_hit["firstOffsets"], [147])
            self.assertTrue(all(h["count"] == 0 and h["firstOffsets"] == [] for h in missing))
            self.assertEqual(path.read_bytes(), content)
            for invalid in ("", "\n", "中文"):
                with self.assertRaises(ValueError):
                    module.inspect_file(path, [invalid])


if __name__ == "__main__":
    unittest.main()
