#!/usr/bin/env python3
"""Rebuild the JingHua source archive from base64 text parts."""
from __future__ import annotations

import argparse
import base64
import json
import lzma
import tarfile
from pathlib import Path

EXPECTED = [f"part-{i:02d}" for i in range(6)]
XZ_MAGIC = b"\xfd7zXZ\x00"


def reconstruct(
    parts_dir: Path,
    output: Path,
    report: Path | None = None,
    allow_missing: bool = False,
    extract_dir: Path | None = None,
) -> dict:
    present = sorted(p.name for p in parts_dir.glob("part-*") if p.is_file())
    missing = [name for name in EXPECTED if name not in present]
    result = {
        "expected": EXPECTED,
        "present": present,
        "missing": missing,
        "complete": not missing,
        "output": str(output),
    }

    if missing:
        result["status"] = "missing_parts"
        if report:
            report.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        if allow_missing:
            print(json.dumps(result, ensure_ascii=False, indent=2))
            return result
        raise SystemExit(f"Missing source parts: {', '.join(missing)}")

    encoded = "".join(
        (parts_dir / name).read_text(encoding="utf-8").strip()
        for name in EXPECTED
    )
    try:
        data = base64.b64decode(encoded, validate=True)
    except Exception as exc:
        raise SystemExit(f"Invalid base64 source parts: {exc}") from exc

    if not data.startswith(XZ_MAGIC):
        raise SystemExit("Decoded payload is not an XZ archive")

    try:
        decompressed = lzma.decompress(data)
    except lzma.LZMAError as exc:
        raise SystemExit(f"XZ integrity check failed: {exc}") from exc

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(data)
    result.update(
        {
            "status": "ok",
            "archive_bytes": len(data),
            "decompressed_bytes": len(decompressed),
        }
    )

    if extract_dir:
        extract_dir.mkdir(parents=True, exist_ok=True)
        with tarfile.open(output, "r:xz") as archive:
            archive.extractall(extract_dir)
        result["extract_dir"] = str(extract_dir)

    if report:
        report.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--parts-dir", type=Path, default=Path("source_parts"))
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("build/JingHuaV4_0_1-source.tar.xz"),
    )
    parser.add_argument(
        "--report",
        type=Path,
        default=Path("build/source-integrity.json"),
    )
    parser.add_argument("--allow-missing", action="store_true")
    parser.add_argument("--extract-dir", type=Path)
    args = parser.parse_args()

    args.report.parent.mkdir(parents=True, exist_ok=True)
    reconstruct(
        args.parts_dir,
        args.output,
        args.report,
        args.allow_missing,
        args.extract_dir,
    )


if __name__ == "__main__":
    main()
