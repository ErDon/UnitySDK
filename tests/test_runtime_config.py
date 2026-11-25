import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('runtime_config', Path(__file__).resolve().parents[1]/'tools/inspect-runtime-config.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ConfigTests(unittest.TestCase):
    def test_development_and_worker_findings(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root/'game.json').write_text(json.dumps({'workers':'workers','subpackages':[{'root':'wasm/'}]}))
            (root/'project.config.json').write_text('{"appid":"build-only-placeholder"}')
            (root/'unity-namespace.js').write_text("const x={useBrotliMT: true, bootConfig:'player-connection-ip=private'}")
            report = module.inspect(root)
            self.assertEqual(len(report['findings']), 5)
            self.assertNotIn('private', json.dumps(report))
            self.assertEqual(report['productionReadiness'], 'not-certified')

    def test_traversal_and_duplicate_roots_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            (root/'project.config.json').write_text('{"appid":"app"}')
            for packages in [[{'root':'../escape'}], [{'root':'C:/outside'}], [{'root':'x'}, {'root':'x/'}]]:
                (root/'game.json').write_text(json.dumps({'subpackages': packages}))
                with self.assertRaises(ValueError): module.inspect(root)

    def test_clean_configuration_is_not_release_certification(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root/'project.config.json').write_text('{"appid":"configured-app"}')
            (root/'game.json').write_text('{"subpackages":[{"root":"data"}]}')
            (root/'data').mkdir()
            report = module.inspect(root)
            self.assertEqual(report['findings'], [])
            self.assertEqual(report['productionReadiness'], 'not-certified')
            self.assertEqual(len(report['files']), 2)

    def test_invalid_or_oversize_configuration_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root/'project.config.json').write_text('{}')
            for content in ['[]', '{"subpackages":{}}', ' ' * 1048577]:
                (root/'game.json').write_text(content)
                with self.assertRaises(ValueError): module.inspect(root)


if __name__ == '__main__': unittest.main()
