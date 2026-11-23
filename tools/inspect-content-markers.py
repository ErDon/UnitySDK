"""Record literal marker evidence in decoded artifacts and reference files, without certifying isolation."""
import argparse
import hashlib
import json
from pathlib import Path


def inspect_file(path, markers):
    path = Path(path)
    if path.is_symlink() or not path.is_file():
        raise ValueError("Expected a regular file, not a symbolic link")
    content = path.read_bytes()
    evidence = []
    for marker in markers:
        if not marker or not marker.isascii() or not marker.isprintable():
            raise ValueError("Markers must be nonempty printable ASCII")
        for encoding in ("ascii", "utf-16-le"):
            pattern = marker.encode(encoding)
            count, offsets, start = 0, [], 0
            while True:
                offset = content.find(pattern, start)
                if offset < 0:
                    break
                count += 1
                if len(offsets) < 20:
                    offsets.append(offset)
                start = offset + len(pattern)
            evidence.append({"marker": marker, "encoding": encoding,
                             "count": count, "firstOffsets": offsets})
    return {"fileName": path.name, "bytes": len(content),
            "sha256": hashlib.sha256(content).hexdigest(), "evidence": evidence}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact", action="append", required=True)
    parser.add_argument("--reference", action="append", default=[])
    parser.add_argument("--marker", action="append", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    destination = Path(args.output).resolve()
    if destination in [Path(p).resolve() for p in args.artifact + args.reference]:
        raise ValueError("Report cannot overwrite an input")
    report = {"schemaVersion": 1,
              "artifacts": [inspect_file(p, args.marker) for p in args.artifact],
              "references": [inspect_file(p, args.marker) for p in args.reference],
              "isolation": "not-assessed",
              "limitation": "Literal bytes only, not semantic attribution. A reference match does not prove which file produced the artifact. Missing markers do not prove absent code. Decoding and reference provenance must be verified separately."}
    destination.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Recorded {len(report['artifacts'])} artifacts and {len(report['references'])} references; isolation NOT assessed")


if __name__ == "__main__":
    main()
