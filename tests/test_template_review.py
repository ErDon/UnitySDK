import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('verify', Path(__file__).resolve().parents[1]/'tools/verify-template-review.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
sha = lambda raw: hashlib.sha256(raw).hexdigest()


class TemplateReviewTests(unittest.TestCase):
    def test_exact_bytes_and_tampering(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source, target = b'const x=$VALUE;\r\n', b'const x=true;\r\n'
            (root/'source.js').write_bytes(source)
            (root/'output.js').write_bytes(target)
            inventory = json.dumps({'schemaVersion':2,'coverage':'all-files','platform':'WeChat',
                'files':[{'path':'source.js','sha256':sha(source),'bytes':len(source)}]}).encode()
            item = {'path':'output.js','sha256':sha(target),'sourcePath':'source.js',
                'sourceSha256':sha(source),'sourceDecoding':'utf-8-sig',
                'outputEncoding':'utf-8 without BOM','lineEndingNormalization':'none',
                'replacementMap':{'$VALUE':'true'}}
            record = {'schemaVersion':1,'platform':'WeChat','inventorySha256':sha(inventory),'files':[item]}
            self.assertEqual(module.verify(record, inventory, root, root), 1)
            for field, value in [('sha256','0'*64), ('sourceSha256','0'*64),
                                 ('replacementMap',{'$VALUE':'false'}), ('sourcePath','../source.js'),
                                 ('outputEncoding','utf-8 with BOM')]:
                changed = copy.deepcopy(record)
                changed['files'][0][field] = value
                with self.subTest(field=field), self.assertRaises(ValueError):
                    module.verify(changed, inventory, root, root)
            with self.assertRaises(ValueError): module.verify(record, inventory+b' ', root, root)
            (root/'output.js').write_bytes(target.replace(b'\r\n', b'\n'))
            with self.assertRaises(ValueError): module.verify(record, inventory, root, root)
            (root/'output.js').write_bytes(target)
            (root/'source.js').write_bytes(source+b' ')
            with self.assertRaises(ValueError): module.verify(record, inventory, root, root)


if __name__ == '__main__': unittest.main()
