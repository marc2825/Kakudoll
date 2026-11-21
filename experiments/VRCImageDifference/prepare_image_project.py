#!/usr/bin/env python3
"""S1/S2を変更せず、実FBX画像差の調査overlayを新規TEMPへ組み立てる。"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import shutil
from pathlib import Path


def prepare(sdk_project: Path, output: Path, models: list[str]) -> None:
    """既存の非破壊prepareを再利用し、調査ソースと20条件だけを追加する。"""
    here = Path(__file__).resolve().parent
    helper = here.parent / 'VRCRealAvatars/prepare_avatar_project.py'
    spec = importlib.util.spec_from_file_location('avatar_prepare', helper)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.prepare(sdk_project, output, models)
    for source in (here / 'Overlay').rglob('*'):
        if not source.is_file():
            continue
        target = output / 'Assets/HeadFrame' / source.relative_to(here / 'Overlay')
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        guid = hashlib.md5(('vlat-image-r1-' + source.name).encode()).hexdigest()
        Path(str(target) + '.meta').write_text(f'fileFormatVersion: 2\nguid: {guid}\n')
    frozen = here.parent / 'VRCViewContext/results/2026-10-17-unity2022.3.22f1/s1-carryover.json'
    data = json.loads(frozen.read_text())
    assert data['count'] == 20 and data['status'] == 'FAIL' and data['threshold_m'] == 0.0001
    (output / 'image-fail-inputs.json').write_text(json.dumps(data, indent=2) + '\n')


def main() -> int:
    """元SDK/FBXを読取専用で指定し、存在しない出力先へコピーする。"""
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--sdk-project', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--model', action='append', required=True)
    a = p.parse_args()
    prepare(a.sdk_project.resolve(), a.output.resolve(), a.model)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
