#!/usr/bin/env python3
"""Create an unsigned JingHua V4.0.7 recovery APK from V4.0.4.

Minimal binary patch:
- preserve all original DEX/resources except four documented entries;
- upgrade manifest versionCode 11 -> 13 and versionName 4.0.4 -> 4.0.7;
- upgrade BuildVersion.VERSION_CODE 11 -> 13 and VERSION_NAME;
- replace the subtitle-repair fragment shader.
"""
from __future__ import annotations

import argparse
import hashlib
import zlib
from pathlib import Path
from zipfile import ZipFile, ZipInfo

OLD_VERSION = b"4.0.4"
NEW_VERSION = b"4.0.7"
OLD_VERSION_CODE = 11
NEW_VERSION_CODE = 13
BUILD_VERSION_PATTERN = b"\x02\x04\x0b\x37"
BUILD_VERSION_REPLACEMENT = b"\x02\x04\x0d\x37"


def clone_info(info: ZipInfo) -> ZipInfo:
    new = ZipInfo(info.filename, info.date_time)
    for attr in (
        "compress_type", "comment", "extra", "internal_attr", "external_attr",
        "create_system", "create_version", "extract_version", "flag_bits",
    ):
        setattr(new, attr, getattr(info, attr))
    new.flag_bits &= ~0x08
    return new


def patch_manifest(data: bytes) -> bytes:
    patched = data.replace(OLD_VERSION.decode().encode("utf-16le"), NEW_VERSION.decode().encode("utf-16le"))
    if patched == data:
        raise RuntimeError("Manifest versionName 4.0.4 not found")
    buf = bytearray(patched)
    chunk = buf.find(b"\x02\x01\x10\x00")
    if chunk < 0:
        raise RuntimeError("AndroidManifest start-tag chunk not found")
    attr_start = int.from_bytes(buf[chunk + 24:chunk + 26], "little")
    first_attr = chunk + 16 + attr_start
    version_code_data = first_attr + 16
    old = int.from_bytes(buf[version_code_data:version_code_data + 4], "little")
    if old != OLD_VERSION_CODE:
        raise RuntimeError(f"Expected versionCode {OLD_VERSION_CODE}, found {old}")
    buf[version_code_data:version_code_data + 4] = NEW_VERSION_CODE.to_bytes(4, "little")
    return bytes(buf)


def patch_dex(data: bytes) -> bytes:
    if data.count(OLD_VERSION) < 1:
        raise RuntimeError("classes3.dex version string not found")
    if data.count(BUILD_VERSION_PATTERN) != 1:
        raise RuntimeError("Unexpected BuildVersion static-value pattern count")
    patched = data.replace(OLD_VERSION, NEW_VERSION)
    patched = patched.replace(BUILD_VERSION_PATTERN, BUILD_VERSION_REPLACEMENT, 1)
    buf = bytearray(patched)
    buf[12:32] = hashlib.sha1(bytes(buf[32:])).digest()
    buf[8:12] = (zlib.adler32(bytes(buf[12:])) & 0xFFFFFFFF).to_bytes(4, "little")
    return bytes(buf)


def patch(base_apk: Path, shader_path: Path, output_apk: Path) -> None:
    shader = shader_path.read_bytes()
    if b"JingHua V4.0.7 recovery shader" not in shader:
        raise RuntimeError("V4.0.7 shader marker missing")
    changed: list[str] = []
    with ZipFile(base_apk, "r") as zin, ZipFile(output_apk, "w") as zout:
        for info in zin.infolist():
            name = info.filename
            upper = name.upper()
            if upper.startswith("META-INF/") and (
                upper.endswith((".SF", ".RSA", ".DSA")) or upper.endswith("MANIFEST.MF")
            ):
                continue
            data = zin.read(name)
            original = data
            if name == "res/raw/fragment_region_es2.glsl":
                data = shader
            elif name == "resources.arsc":
                data = data.replace(OLD_VERSION, NEW_VERSION)
                if data == original:
                    raise RuntimeError("resources.arsc version string not found")
            elif name == "AndroidManifest.xml":
                data = patch_manifest(data)
            elif name == "classes3.dex":
                data = patch_dex(data)
            if data != original:
                changed.append(name)
            zout.writestr(clone_info(info), data)
    expected = {
        "AndroidManifest.xml", "classes3.dex", "resources.arsc",
        "res/raw/fragment_region_es2.glsl",
    }
    if set(changed) != expected:
        raise RuntimeError(f"Unexpected changed files: {changed}")
    print("changed:", ", ".join(changed))
    print(output_apk)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("base_apk", type=Path)
    parser.add_argument("shader", type=Path)
    parser.add_argument("output_apk", type=Path)
    args = parser.parse_args()
    patch(args.base_apk, args.shader, args.output_apk)


if __name__ == "__main__":
    main()
