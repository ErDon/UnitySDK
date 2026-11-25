import importlib.util
from pathlib import Path
import tempfile
import hashlib
import json
import unittest

spec = importlib.util.spec_from_file_location('export_review', Path(__file__).resolve().parents[1] / 'tools/review-export-isolation.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReviewTests(unittest.TestCase):
    def test_baseline_hash_binding_rejects_changed_and_duplicate_inventory(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder)/'vendor.json'
            source.write_text(json.dumps({'schemaVersion': 2, 'coverage': 'all-files', 'platform': 'WeChat',
                'files': [{'path': 'vendor.js', 'bytes': 1, 'sha256': 'a'*64}]}))
            report = {'platform': 'WeChat', 'baselines': [{'platform': 'WeChat',
                'inventorySha256': hashlib.sha256(source.read_bytes()).hexdigest()}],
                'files': [{'path': 'renamed.js', 'bytes': 1, 'sha256': 'a'*64}]}
            self.assertEqual(module.baseline_evidence(report, [source])['renamed.js']['sourcePaths'], ['vendor.js'])
            with self.assertRaises(ValueError): module.baseline_evidence(report, [source, source])
            source.write_text(source.read_text()+' ')
            with self.assertRaises(ValueError): module.baseline_evidence(report, [source])

    def test_missing_stale_and_foreign_cannot_pass(self):
        report = {'schemaVersion': 1, 'platform': 'WeChat', 'files': [
            {'path': 'game.js', 'bytes': 1, 'sha256': 'a'*64, 'classification': 'unmatched-requires-review'}]}
        decisions = {'schemaVersion': 1, 'platform': 'WeChat', 'reviews': []}
        self.assertFalse(module.review(report, decisions)['reviewComplete'])
        entry = {'path': 'game.js', 'sha256': 'a'*64, 'verdict': 'target', 'reviewer': 'tester',
                 'evidenceReference': 'source-map-review-1', 'reason': 'Reviewed generated source mapping'}
        decisions['reviews'] = [entry]
        self.assertTrue(module.review(report, decisions)['reviewComplete'])
        entry['sha256'] = 'b'*64
        self.assertEqual(module.review(report, decisions)['files'][0]['state'], 'stale-review')
        entry['sha256'] = 'a'*64
        report['files'][0]['classification'] = 'foreign-only-exact-match'
        self.assertFalse(module.review(report, decisions)['reviewComplete'])

    def test_removed_duplicate_and_platform(self):
        report = {'schemaVersion': 1, 'platform': 'WeChat', 'files': [
            {'path': 'x', 'bytes': 1, 'sha256': 'a'*64, 'classification': 'unmatched-requires-review'}]}
        decisions = {'schemaVersion': 1, 'platform': 'WeChat', 'reviews': [{'path': 'gone'}]}
        self.assertEqual(module.review(report, decisions)['removedReviewPaths'], ['gone'])
        decisions['reviews'] *= 2
        with self.assertRaises(ValueError): module.review(report, decisions)
        decisions['platform'] = 'TikTok'
        with self.assertRaises(ValueError): module.review(report, decisions)

    def test_live_bytes_and_extra_files_invalidate_inventory(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root/'x').write_bytes(b'a')
            report = {'files': [{'path': 'x', 'bytes': 1, 'sha256': hashlib.sha256(b'a').hexdigest()}]}
            module.verify_files(root, report)
            (root/'x').write_bytes(b'b')
            with self.assertRaises(ValueError): module.verify_files(root, report)
            (root/'x').write_bytes(b'a')
            (root/'extra').write_bytes(b'')
            with self.assertRaises(ValueError): module.verify_files(root, report)


if __name__ == '__main__': unittest.main()
