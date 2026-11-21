#!/usr/bin/env python3
"""既存VRC projectを読取専用のSDK取得元とし、新しいS1検証projectを用意する。"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from pathlib import Path


def prepare(sdk_project: Path, output: Path) -> None:
    """SDKだけを複製し、元projectのAssets・設定・アカウント情報をコピーしない。"""
    source = sdk_project.resolve()
    target = output.resolve()
    here = Path(__file__).resolve().parent
    baseline = here.parent / "VRCHeadFrame"
    if target.exists():
        raise ValueError(f"refusing existing output: {target}")
    if target == source or source in target.parents or target in source.parents:
        raise ValueError("output must be separate from the source SDK project")
    repository = here.parents[1]
    if target == repository or repository in target.parents:
        raise ValueError("SDK staging must be outside the repository")
    source_manifest = json.loads((source / "Packages/manifest.json").read_text(encoding="utf-8-sig"))
    source_lock = json.loads((source / "Packages/packages-lock.json").read_text(encoding="utf-8-sig"))["dependencies"]
    versions = {}
    for name in ("com.vrchat.base", "com.vrchat.avatars"):
        package = source / "Packages" / name
        metadata = json.loads((package / "package.json").read_text(encoding="utf-8-sig"))
        versions[name] = metadata["version"]
    if set(versions.values()) != {"3.10.5"}:
        raise ValueError(f"this experiment is pinned to SDK 3.10.5, got {versions}")
    target.mkdir(parents=True)
    for folder in ("Assets", "ProjectSettings", "Packages"):
        shutil.copytree(baseline / folder, target / folder)
    # SDK binaries/source remain local, outside the repository's distributable experiment.
    for name in versions:
        shutil.copytree(source / "Packages" / name, target / "Packages" / name)
    dependencies = {
        name: version for name, version in source_manifest["dependencies"].items()
        if name.startswith("com.unity.modules.")
    }
    # SDK 3.10.5 editor sources include NUnit tests, so its existing Test Framework is a compile dependency.
    # Pin effective UPM versions from the resolved SDK environment, excluding IDE tooling only.
    for name, metadata in source_lock.items():
        if metadata["source"] == "registry" and not name.startswith("com.unity.ide."):
            dependencies[name] = metadata["version"]
    dependencies["com.unity.ugui"] = "1.0.0"
    (target / "Packages/manifest.json").write_text(json.dumps({"dependencies": dependencies}, indent=2) + "\n")
    (target / "Packages/packages-lock.json").unlink()
    editor = target / "Assets/HeadFrame/Editor"
    overlay = here / "Overlay/Editor/SdkHeadFrameSweep.cs"
    shutil.copy2(overlay, editor / overlay.name)
    guid = hashlib.md5(b"vlat-sdk-head-frame-overlay-v1").hexdigest()
    (editor / (overlay.name + ".meta")).write_text(f"fileFormatVersion: 2\nguid: {guid}\n")
    asmdef = editor / "VLat.HeadFrame.Experiment.Editor.asmdef"
    definition = json.loads(asmdef.read_text())
    definition["references"] = ["VRC.SDK3A.Editor"]
    asmdef.write_text(json.dumps(definition, indent=2) + "\n")
    fingerprints = {}
    for name in (
        "com.vrchat.base/Runtime/VRCSDK/Plugins/VRC.SDK3.Dynamics.Constraint.dll",
        "com.vrchat.base/Runtime/VRCSDK/Plugins/VRC.Dynamics.dll",
        "com.vrchat.avatars/Editor/VRCSDK/SDK3A/AvatarDynamicsSetup.cs",
    ):
        original = source / "Packages" / name
        copied = target / "Packages" / name
        digest = hashlib.sha256(original.read_bytes()).hexdigest()
        if hashlib.sha256(copied.read_bytes()).hexdigest() != digest:
            raise ValueError(f"SDK copy mismatch: {name}")
        fingerprints[name] = digest
    inputs = {"sdk_versions": versions, "sdk_files_sha256": fingerprints, "registry_versions": {
        name: version for name, version in dependencies.items() if not name.startswith("com.unity.modules.")
    }, "baseline_sources_sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                   for p in (baseline / "Assets/HeadFrame/Editor").glob("*.cs")}}
    (target / "sdk-experiment-inputs.json").write_text(json.dumps(inputs, indent=2) + "\n")
    print(target)


def main() -> int:
    """CLIから、存在しない新規出力先だけを作成する。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk-project", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    prepare(args.sdk_project, args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
