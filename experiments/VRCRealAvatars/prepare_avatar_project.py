#!/usr/bin/env python3
"""利用者指定のFBXを新規SDK検証projectへ複製する。原本・モデル配布物をrepoへ書かない。"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import re
import shutil
from pathlib import Path


def digest(path: Path) -> str:
    """ファイルを変更せず、分割読み込みでSHA-256を返す。"""
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def prepare(sdk_project: Path, output: Path, models: list[str]) -> None:
    """検証用のコピーと原本照合情報を作成する。SDK取得元とFBX原本は読み取り専用で扱う。"""
    here = Path(__file__).resolve().parent
    parsed = []
    names = set()
    for item in models:
        name, separator, raw_path = item.partition("=")
        path = Path(raw_path).resolve()
        if not separator or not re.fullmatch(r"[A-Za-z0-9_-]+", name) or name in names:
            raise ValueError(f"expected unique Name=path.fbx: {item}")
        if not path.is_file() or path.suffix.lower() != ".fbx":
            raise ValueError(f"FBX not found: {path}")
        names.add(name)
        parsed.append((name, path, digest(path)))
    if not parsed:
        raise ValueError("at least one model is required")
    target = output.resolve()
    for _, original, _ in parsed:
        if target in original.parents or original.parent in target.parents or target == original.parent:
            raise ValueError("output must be separate from every source model directory")
    helper = here.parent / "VRCHeadFrameSdk/prepare_sdk_project.py"
    spec = importlib.util.spec_from_file_location("vlat_sdk_prepare", helper)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.prepare(sdk_project, output)
    output = output.resolve()
    entries = []
    for name, original, sha256 in parsed:
        folder = output / "Assets/Models" / name
        folder.mkdir(parents=True)
        target = folder / original.name
        shutil.copy2(original, target)
        if digest(target) != sha256 or digest(original) != sha256:
            raise ValueError(f"FBX changed during copy: {name}")
        entries.append({"name": name, "fileName": original.name, "assetPath": target.relative_to(output).as_posix(),
                        "sourcePath": str(original), "sha256": sha256, "bytes": original.stat().st_size,
                        "sourceMetaCopied": False})
    editor = output / "Assets/HeadFrame/Editor"
    for script in (here / "Overlay/Editor").glob("*.cs"):
        shutil.copy2(script, editor / script.name)
        guid = hashlib.md5(("vlat-real-avatars-" + script.name).encode()).hexdigest()
        (editor / (script.name + ".meta")).write_text(f"fileFormatVersion: 2\nguid: {guid}\n")
    for shader in (here / "Overlay").glob("*.shader"):
        target = output / "Assets/HeadFrame" / shader.name
        shutil.copy2(shader, target)
        guid = hashlib.md5(("vlat-real-avatars-" + shader.name).encode()).hexdigest()
        Path(str(target) + ".meta").write_text(f"fileFormatVersion: 2\nguid: {guid}\n")
    (output / "avatar-inputs.json").write_text(json.dumps({"models": entries}, indent=2) + "\n")
    print("Models copied:", ", ".join(names))


def main() -> int:
    """CLIから新規stagingだけを作成する。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk-project", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--model", action="append", required=True)
    arguments = parser.parse_args()
    prepare(arguments.sdk_project, arguments.output, arguments.model)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
