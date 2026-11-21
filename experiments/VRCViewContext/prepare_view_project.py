#!/usr/bin/env python3
"""S1を保持したまま、新規の独立SDK projectへS2 overlayを追加する。"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import shutil
from pathlib import Path


def prepare(sdk_project: Path, output: Path) -> None:
    """取得元のSDKだけを新規出力先へ複製し、既存Assetsと設定を持ち込まない。"""
    here = Path(__file__).resolve().parent
    helper = here.parent / "VRCHeadFrameSdk/prepare_sdk_project.py"
    spec = importlib.util.spec_from_file_location("vlat_sdk_prepare", helper)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.prepare(sdk_project, output)
    destination = output.resolve() / "Assets/HeadFrame"
    for source in (here / "Overlay").rglob("*"):
        if not source.is_file():
            continue
        target = destination / source.relative_to(here / "Overlay")
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        guid = hashlib.md5(("vlat-view-context-" + source.relative_to(here / "Overlay").as_posix()).encode()).hexdigest()
        Path(str(target) + ".meta").write_text(f"fileFormatVersion: 2\nguid: {guid}\n")
    sample = sdk_project / "Packages/com.vrchat.base/Runtime/VRCSDK/Sample Assets/Shaders/Mobile/ToonStandard/CG/Helpers.cginc"
    evidence = {"base_commit": "4eec47d", "experiment": "P0-S2", "sdk_sample_sha256": hashlib.sha256(sample.read_bytes()).hexdigest(),
                "live_client": "NOT RUN", "simulation": "explicit injected shader; no VRChat globals overwritten"}
    (output / "view-experiment-inputs.json").write_text(json.dumps(evidence, indent=2) + "\n")


def main() -> int:
    """存在しない新規TEMPを指定して実行する。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk-project", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    prepare(args.sdk_project.resolve(), args.output.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
