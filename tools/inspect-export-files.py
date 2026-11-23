"""Inventory exported bytes and identify exact vendor matches; not an isolation certificate."""
import argparse
import hashlib
import json
from pathlib import Path

PLATFORMS = {"WeChat", "Douyin", "TikTok"}


def inspect(root, target, inventories):
    root = Path(root).resolve(strict=True)
    if not root.is_dir() or target not in PLATFORMS:
        raise ValueError("Expected an export directory and supported target")
    signatures, baselines = {}, []
    for source in inventories:
        raw = Path(source).read_bytes()
        data = json.loads(raw.decode("utf-8-sig"))
        if data.get("schemaVersion") != 2 or data.get("coverage") != "all-files" or data.get("platform") not in PLATFORMS:
            raise ValueError("Expected full v2 platform inventory")
        if not isinstance(data.get("files"), list) or not data["files"]:
            raise ValueError("Empty or malformed vendor inventory")
        baselines.append({"platform": data["platform"], "packageName": data["packageName"],
                          "packageVersion": data["packageVersion"], "inventorySha256": hashlib.sha256(raw).hexdigest()})
        for entry in data["files"]:
            digest, size = entry["sha256"], entry["bytes"]
            if not isinstance(digest, str) or len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest.lower()):
                raise ValueError("Invalid vendor hash")
            if type(size) is not int or size < 0:
                raise ValueError("Invalid vendor byte count")
            signatures.setdefault((digest.lower(), size), set()).add(data["platform"])
    if {b["platform"] for b in baselines} != PLATFORMS:
        raise ValueError("Supply baselines for all three platforms to distinguish shared files")
    files = []
    for path in sorted(root.rglob("*")):
        # Reparse directories can hide files or lead outside the selected artifact.
        if path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction()):
            raise ValueError("Export contains a symbolic link or junction")
        if not path.is_file():
            continue
        digest, size = hashlib.sha256(), 0
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                size += len(block)
                digest.update(block)
        sha = digest.hexdigest()
        matches = signatures.get((sha, size), set())
        status = "unmatched-requires-review"
        if size == 0:
            status = "empty-file-no-platform-attribution"
        elif matches:
            status = "target-or-shared-exact-match" if target in matches else "foreign-only-exact-match"
        files.append({"path": path.relative_to(root).as_posix(), "bytes": size, "sha256": sha,
                      "matchingPlatforms": sorted(matches), "classification": status})
    if not files:
        raise ValueError("Empty export cannot be inspected")
    return {"schemaVersion": 1, "platform": target, "baselines": baselines, "files": files,
            "fileCount": len(files), "totalBytes": sum(f["bytes"] for f in files),
            "foreignOnlyMatchCount": sum(f["classification"] == "foreign-only-exact-match" for f in files),
            "isolation": "not-assessed", "authenticityVerified": False,
            "limitation": "Exact bytes only. Renaming is detected; transformed, merged or compressed code requires separate review. Shared or unmatched bytes do not prove isolation."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True)
    parser.add_argument("--platform", required=True, choices=sorted(PLATFORMS))
    parser.add_argument("--inventory", required=True, action="append")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    destination = Path(args.output).resolve()
    root = Path(args.root).resolve(strict=True)
    if destination == root or root in destination.parents or destination in [Path(p).resolve() for p in args.inventory]:
        raise ValueError("Report must be outside export and must not overwrite a vendor inventory")
    report = inspect(root, args.platform, args.inventory)
    destination.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Inspected {report['fileCount']} files; foreign-only exact matches: {report['foreignOnlyMatchCount']}; isolation NOT assessed")


if __name__ == "__main__":
    main()
