#!/usr/bin/env python3
"""S1/S2を凍結したまま、合成Avatarの実機診断projectを新規TEMPへ用意する。"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import shutil
from pathlib import Path


def prepare(sdk_project: Path, output: Path) -> None:
    """SDK packageだけを複製し、元Assets/設定/アカウント/FBXを持ち込まない。"""
    here = Path(__file__).resolve().parent
    helper = here.parent / 'VRCViewContext/prepare_view_project.py'
    spec = importlib.util.spec_from_file_location('view_prepare', helper)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.prepare(sdk_project, output)
    asmdef = output/'Assets/HeadFrame/Editor/VLat.HeadFrame.Experiment.Editor.asmdef'
    definition = json.loads(asmdef.read_text())
    definition['references'].append('VRC.SDKBase.Editor')
    asmdef.write_text(json.dumps(definition, indent=2)+'\n')
    for source in (here/'Overlay').rglob('*'):
        if not source.is_file(): continue
        target = output/'Assets/HeadFrame'/source.relative_to(here/'Overlay')
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(source, target)
        guid = hashlib.md5(('vlat-client-r1-'+source.name).encode()).hexdigest()
        Path(str(target)+'.meta').write_text(f'fileFormatVersion: 2\nguid: {guid}\n')
    (output/'client-experiment-inputs.json').write_text(json.dumps({
        'experiment':'P0-S2-R1', 'base_commit':'6023a9e7d0194d6ad9771334462643cd123def15',
        'base_branch':'spike/vrc-s1-image-fail-investigation',
        'branch':'spike/vrc-s2-client-validation', 'sdk':'3.10.5', 'unity':'2022.3.22f1',
        'source_avatar':'generated synthetic humanoid; no FBX or private avatar',
        'client_results':'NOT RUN', 'authentication':'human only', 'upload':'not authorized'
    }, indent=2)+'\n')


def main():
    """存在しない隔離出力先を指定する。"""
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--sdk-project', type=Path, required=True); p.add_argument('--output', type=Path, required=True)
    a = p.parse_args(); prepare(a.sdk_project.resolve(), a.output.resolve())


if __name__ == '__main__': main()
