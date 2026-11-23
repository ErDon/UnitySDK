import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("export_files", Path(__file__).resolve().parents[1] / "tools/inspect-export-files.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ExportFilesTests(unittest.TestCase):
    def test_exact_matches_distinguish_foreign_shared_and_transformed_bytes(self):
        with tempfile.TemporaryDirectory(prefix="erdon-export-") as directory:
            base = Path(directory)
            root = base / "export"
            root.mkdir()
            inventories = []
            for platform in sorted(module.PLATFORMS):
                records = []
                for name, content in [("shared.meta", b"shared"), ("vendor.js", platform.encode())]:
                    records.append({"path": name, "bytes": len(content), "sha256": hashlib.sha256(content).hexdigest()})
                if platform == "Douyin":
                    records.append({"path": "empty.js", "bytes": 0, "sha256": hashlib.sha256(b"").hexdigest()})
                inventory = base / (platform + ".json")
                inventory.write_text(json.dumps({"schemaVersion": 2, "coverage": "all-files", "platform": platform,
                    "packageName": "test.sdk", "packageVersion": "1", "files": records}), encoding="utf-8")
                inventories.append(inventory)
            payloads = {"renamed.js": b"Douyin", "shared.meta": b"shared", "target.js": b"WeChat", "merged.wasm": b"WeChat+Douyin", "empty.js": b""}
            for name, content in payloads.items():
                (root / name).write_bytes(content)
            report = module.inspect(root, "WeChat", inventories)
            files = {f["path"]: f for f in report["files"]}
            self.assertEqual(report["fileCount"], 5)
            self.assertEqual(report["totalBytes"], sum(map(len, payloads.values())))
            self.assertEqual(report["foreignOnlyMatchCount"], 1)
            self.assertEqual(files["renamed.js"]["classification"], "foreign-only-exact-match")
            self.assertEqual(files["shared.meta"]["matchingPlatforms"], sorted(module.PLATFORMS))
            self.assertEqual(files["target.js"]["classification"], "target-or-shared-exact-match")
            self.assertEqual(files["merged.wasm"]["classification"], "unmatched-requires-review")
            self.assertEqual(files["empty.js"]["classification"], "empty-file-no-platform-attribution")
            self.assertEqual(report["isolation"], "not-assessed")
            self.assertFalse(report["authenticityVerified"])
            with self.assertRaises(ValueError):
                module.inspect(root, "WeChat", inventories[:2])
            empty = base / "empty"
            empty.mkdir()
            with self.assertRaises(ValueError):
                module.inspect(empty, "WeChat", inventories)


if __name__ == "__main__":
    unittest.main()
