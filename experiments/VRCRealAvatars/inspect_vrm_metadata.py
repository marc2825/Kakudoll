#!/usr/bin/env python3
"""VRMをUnityへimportせず、GLB JSONの版とHead関連mappingだけを読み取る。"""

from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path
from prepare_avatar_project import digest


def inspect(path: Path) -> dict:
    """モデルを変更せず、公開する数値・fingerprintだけを返す。"""
    with path.open("rb") as stream:
        magic, version, length = struct.unpack("<4sII", stream.read(12))
        chunk_length, kind = struct.unpack("<II", stream.read(8))
        if magic != b"glTF" or version != 2 or kind != 0x4E4F534A or length != path.stat().st_size or chunk_length > length - 20:
            raise ValueError(f"invalid GLB2 JSON header: {path.name}")
        gltf = json.loads(stream.read(chunk_length))
    extensions = gltf.get("extensions", {})
    if "VRM" in extensions:
        ext = extensions["VRM"]
        mapping = {bone["bone"]: bone["node"] for bone in ext.get("humanoid", {}).get("humanBones", [])}
        vrm_version = "VRM 0 / " + str(ext.get("specVersion"))
    elif "VRMC_vrm" in extensions:
        ext = extensions["VRMC_vrm"]
        mapping = {bone: record["node"] for bone, record in ext.get("humanoid", {}).get("humanBones", {}).items()}
        vrm_version = "VRM 1 / " + str(ext.get("specVersion"))
    else:
        raise ValueError(f"no supported VRM extension: {path.name}")
    return {"fileName": path.name, "bytes": length, "sha256": digest(path), "glbVersion": version,
            "extensionsUsed": gltf.get("extensionsUsed", []), "vrmVersion": vrm_version,
            "meshes": len(gltf.get("meshes", [])), "materials": len(gltf.get("materials", [])),
            "humanoid": {bone: mapping[bone] for bone in ("head", "neck", "leftEye", "rightEye", "jaw") if bone in mapping},
            "unityImport": "NOT RUN", "readOnly": True}


def main() -> int:
    """指定VRMを読取専用で監査し、JSONを標準出力へ表示する。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", type=Path, nargs="+")
    args = parser.parse_args()
    print(json.dumps([inspect(path) for path in args.paths], indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
