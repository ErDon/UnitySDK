"""Inspect existing mini-game export configuration without executing vendor JavaScript."""
import argparse
import hashlib
import json
import re
from pathlib import Path, PurePosixPath


def inspect(root):
    root = Path(root).resolve(strict=True)
    evidence, findings = [], []
    def read(name):
        path = root / name
        if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
            raise ValueError('Linked configuration is not supported')
        # Bound allocation even when an export accidentally contains a binary here.
        with path.open('rb') as stream:
            raw = stream.read(1048577)
        if len(raw) > 1048576:
            raise ValueError('Configuration exceeds inspection limit')
        evidence.append({'path': name, 'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()})
        return raw.decode('utf-8-sig')
    game = json.loads(read('game.json'))
    project = json.loads(read('project.config.json'))
    if not isinstance(game, dict) or not isinstance(project, dict):
        raise ValueError('Expected configuration objects')
    app_id = project.get('appid')
    if not isinstance(app_id, str) or not app_id.strip() or 'placeholder' in app_id.lower():
        findings.append('APP_ID_NOT_READY')
    if game.get('workers'):
        findings.append('WORKERS_DECLARED_REQUIRES_REVIEW')
    packages = game.get('subpackages', [])
    if not isinstance(packages, list):
        raise ValueError('Invalid subpackage list')
    seen = set()
    for package in packages:
        value = package.get('root') if isinstance(package, dict) else None
        if not isinstance(value, str) or not value or '\\' in value or ':' in value:
            raise ValueError('Unsafe subpackage root')
        relative = PurePosixPath(value)
        if relative.is_absolute() or '..' in relative.parts or not relative.parts:
            raise ValueError('Unsafe subpackage root')
        normalized = relative.as_posix()
        if normalized in seen:
            raise ValueError('Duplicate subpackage root')
        seen.add(normalized)
        path = root
        for part in relative.parts:
            path /= part
            if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
                raise ValueError('Linked subpackage root')
        if not path.is_dir():
            findings.append('SUBPACKAGE_DIRECTORY_MISSING:' + normalized)
    # This is a literal scan, not JavaScript evaluation. Vendor build metadata
    # (for example build-host Brotli compression) must be reviewed separately.
    if (root / 'unity-namespace.js').exists():
        namespace = read('unity-namespace.js')
        if re.search(r'\buseBrotliMT\s*:\s*true\b', namespace):
            findings.append('BROTLI_MT_LITERAL_REQUIRES_REVIEW')
        if 'player-connection-ip=' in namespace:
            findings.append('DEVELOPMENT_CONNECTION_CONFIG_PRESENT')
    return {'schemaVersion': 1, 'files': evidence, 'findings': findings,
            'subpackageRoots': sorted(seen), 'productionReadiness': 'not-certified',
            'limitation': 'Static configuration evidence only; worker declarations and JavaScript literals do not prove runtime thread execution.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    root, output = Path(args.root).resolve(strict=True), Path(args.output).resolve()
    if output == root or root in output.parents:
        raise ValueError('Report must be outside the export')
    result = inspect(root)
    output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"Configuration inspected: {len(result['findings'])} findings; production NOT certified")


if __name__ == '__main__':
    main()
