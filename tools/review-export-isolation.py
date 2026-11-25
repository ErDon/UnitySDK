"""Track hash-bound human review of export attribution; never certify from absent markers."""
import argparse
import hashlib
import json
from pathlib import Path


def baseline_evidence(report, inventories):
    """Resolve exact target-byte matches against the precise inventoried baselines."""
    expected = {(b['platform'], b['inventorySha256']) for b in report['baselines']}
    supplied, matches = set(), {}
    for source in inventories:
        raw = Path(source).read_bytes()
        data = json.loads(raw.decode('utf-8-sig'))
        identity = (data.get('platform'), hashlib.sha256(raw).hexdigest())
        if identity not in expected or identity in supplied or data.get('schemaVersion') != 2 or data.get('coverage') != 'all-files':
            raise ValueError('Vendor inventory is duplicate or differs from export baseline')
        supplied.add(identity)
        if identity[0] == report['platform']:
            for entry in data['files']:
                matches.setdefault((entry['sha256'], entry['bytes']), []).append(entry['path'])
    if supplied != expected:
        raise ValueError('Supply every exact baseline used by export inventory')
    return {item['path']: {'inventorySha256': next(h for p, h in supplied if p == report['platform']),
                           'sourcePaths': matches[(item['sha256'], item['bytes'])]}
            for item in report['files'] if item['bytes'] > 0 and (item['sha256'], item['bytes']) in matches}


def review(report, decisions):
    if report.get('schemaVersion') != 1 or report.get('platform') not in ('WeChat', 'Douyin', 'TikTok'):
        raise ValueError('Unsupported export inventory')
    files = report.get('files')
    if not isinstance(files, list) or not files:
        raise ValueError('Empty inventory')
    if decisions.get('schemaVersion') != 1 or decisions.get('platform') != report['platform']:
        raise ValueError('Review platform mismatch')
    entries = decisions.get('reviews', [])
    mapped = {}
    for entry in entries:
        path = entry.get('path')
        if not isinstance(path, str) or path in mapped:
            raise ValueError('Invalid or duplicate review path')
        mapped[path] = entry
    results, seen = [], set()
    for item in files:
        path, sha = item['path'], item['sha256']
        if path in seen or not isinstance(sha, str) or len(sha) != 64:
            raise ValueError('Invalid inventory entry')
        seen.add(path)
        decision = mapped.get(path)
        state = 'pending-review'
        if item['classification'] == 'foreign-only-exact-match':
            state = 'blocked-foreign-bytes'
        elif decision:
            if decision.get('sha256') != sha:
                state = 'stale-review'
            elif decision.get('verdict') == 'foreign':
                state = 'blocked-reviewed-foreign'
            elif decision.get('verdict') in ('target', 'shared', 'game-content') and all(
                isinstance(decision.get(k), str) and decision[k].strip()
                for k in ('reviewer', 'evidenceReference', 'reason')
            ):
                state = 'review-recorded'
        results.append({'path': path, 'sha256': sha, 'state': state, 'bytes': item['bytes']})
    removed = sorted(set(mapped) - seen)
    return {'schemaVersion': 1, 'platform': report['platform'], 'files': results,
            'reviewed': sum(x['state'] == 'review-recorded' for x in results),
            'pendingOrBlocked': sum(x['state'] != 'review-recorded' for x in results),
            'removedReviewPaths': removed,
            'reviewComplete': not removed and all(x['state'] == 'review-recorded' for x in results),
            'limitation': 'Records supplied human attribution only. Does not establish vendor authenticity, runtime behavior or semantic code absence.'}


def verify_files(root, report):
    root = Path(root).resolve(strict=True)
    found = {}
    for path in root.rglob('*'):
        if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
            raise ValueError('Linked export entries are not supported')
        if path.is_file():
            digest = hashlib.sha256()
            with path.open('rb') as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b''):
                    digest.update(block)
            found[path.relative_to(root).as_posix()] = (path.stat().st_size, digest.hexdigest())
    expected = {f['path']: (f['bytes'], f['sha256']) for f in report['files']}
    if len(expected) != len(report['files']) or found != expected:
        raise ValueError('Export changed: regenerate the inventory before reviewing')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inventory', required=True)
    parser.add_argument('--root', required=True)
    parser.add_argument('--reviews')
    parser.add_argument('--vendor-inventory', action='append', default=[])
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    output = Path(args.output).resolve()
    root = Path(args.root).resolve(strict=True)
    inputs = [Path(p).resolve() for p in [args.inventory, args.reviews, *args.vendor_inventory] if p]
    if output == root or root in output.parents or output in inputs:
        raise ValueError('Output must not overwrite export or review inputs')
    report = json.loads(Path(args.inventory).read_text(encoding='utf-8-sig'))
    verify_files(root, report)
    decisions = json.loads(Path(args.reviews).read_text(encoding='utf-8-sig')) if args.reviews else {
        'schemaVersion': 1, 'platform': report['platform'], 'reviews': []}
    result = review(report, decisions)
    if args.vendor_inventory:
        evidence = baseline_evidence(report, args.vendor_inventory)
        for item in result['files']:
            item['targetByteEvidence'] = evidence.get(item['path'])
        result['targetByteMatchCount'] = len(evidence)
    output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"{result['platform']}: reviewed={result['reviewed']}, pending/blocked={result['pendingOrBlocked']}")
    return 0 if result['reviewComplete'] else 2


if __name__ == '__main__':
    raise SystemExit(main())
