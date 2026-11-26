"""Reproduce recorded template transformations without executing vendor scripts."""
import argparse
import codecs
import hashlib
import json
from pathlib import Path, PurePosixPath


def file_at(root, name):
    relative = PurePosixPath(name)
    if not name or '\\' in name or ':' in name or relative.is_absolute() or '..' in relative.parts:
        raise ValueError('Unsafe evidence path')
    path = Path(root).resolve(strict=True)
    for part in relative.parts:
        path /= part
        if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
            raise ValueError('Linked evidence path')
    return path.read_bytes()


def verify(record, inventory_raw, source_root, export_root):
    if record.get('schemaVersion') != 1 or hashlib.sha256(inventory_raw).hexdigest() != record.get('inventorySha256'):
        raise ValueError('Inventory identity mismatch')
    inventory = json.loads(inventory_raw.decode('utf-8-sig'))
    if inventory.get('schemaVersion') != 2 or inventory.get('coverage') != 'all-files' or inventory.get('platform') != record.get('platform'):
        raise ValueError('Unsupported inventory')
    indexed = {x['path']: x for x in inventory['files']}
    if len(indexed) != len(inventory['files']):
        raise ValueError('Duplicate inventory path')
    seen = set()
    if not record.get('files'):
        raise ValueError('Empty review')
    for item in record['files']:
        if item['path'] in seen:
            raise ValueError('Duplicate artifact')
        seen.add(item['path'])
        source = file_at(source_root, item['sourcePath'])
        baseline = indexed[item['sourcePath']]
        digest = hashlib.sha256(source).hexdigest()
        if digest != item['sourceSha256'] or digest != baseline['sha256'] or len(source) != baseline['bytes']:
            raise ValueError('Source changed')
        if item['sourceDecoding'] != 'utf-8-sig' or item['lineEndingNormalization'] != 'none':
            raise ValueError('Unsupported transformation')
        text = source.decode('utf-8-sig')
        for old, new in sorted(item['replacementMap'].items(), key=lambda x: -len(x[0])):
            if not old or old not in text or not isinstance(new, str):
                raise ValueError('Invalid substitution')
            text = text.replace(old, new)
        enc = item['outputEncoding']
        if enc not in ('utf-8 with BOM', 'utf-8 without BOM'):
            raise ValueError('Unsupported output encoding')
        expected = (codecs.BOM_UTF8 if enc == 'utf-8 with BOM' else b'') + text.encode('utf-8')
        actual = file_at(export_root, item['path'])
        if actual != expected or hashlib.sha256(actual).hexdigest() != item['sha256']:
            raise ValueError('Artifact differs from reconstruction')
    return len(seen)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('review', 'inventory', 'source-root', 'export-root'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    count = verify(json.loads(Path(args.review).read_text(encoding='utf-8-sig')),
                   Path(args.inventory).read_bytes(), args.source_root, args.export_root)
    print(f'{count} exact template reconstructions verified; runtime and full isolation NOT certified.')
