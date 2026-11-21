#!/usr/bin/env python3
"""SDK/私有FBX/認証情報を持ち込まず、合成AvatarとUnity側の実測証拠を保存する。"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil


def collect(project: Path, r1_results: Path, output: Path):
    here=Path(__file__).resolve().parent
    if output.exists(): raise ValueError('refusing existing evidence folder')
    if project.resolve()==output.resolve() or project.resolve() in output.resolve().parents:
        raise ValueError('evidence must be separate from staging')
    for p in (here/'Overlay').rglob('*'):
        if p.is_file() and p.read_bytes()!=(project/'Assets/HeadFrame'/p.relative_to(here/'Overlay')).read_bytes():
            raise ValueError('staging source differs: '+str(p))
    preflight=project/'results-client-preflight'
    if (preflight/'FAILED.txt').exists(): raise ValueError('final preflight failed')
    record=json.loads((preflight/'preflight.json').read_text())
    if record['off_reference_different_channels']!=0 or not record['unity_player_assetbundle_built']:
        raise ValueError('preflight incomplete')
    output.mkdir(parents=True)
    for p in preflight.iterdir():
        if p.is_file(): shutil.copy2(p,output/p.name)
    for source,name in [('client-experiment-inputs.json','inputs.json'),('sdk-experiment-inputs.json','sdk-inputs.json'),('Packages/packages-lock.json','packages-lock.json')]:
        shutil.copy2(project/source,output/name)
    shutil.copy2(here/'client-results-template.json',output/'client-results.json')
    shutil.copytree(project/'Assets/VLatClientGenerated',output/'generated')
    for label,source in [('regression-s2',project/'regression-s2'),('regression-s1-r1',r1_results)]:
        target=output/label;target.mkdir()
        # R1 private-images deliberately stay in the original TEMP, as in the previous task.
        for p in source.iterdir():
            if p.is_file() and p.suffix in ('.csv','.json','.txt'): shutil.copy2(p,target/p.name)
        if label=='regression-s2':shutil.copytree(source/'images',target/'images')
    first=output/'initial-off-harness-failure';first.mkdir()
    for name in ('preflight.json','FAILED.txt','effect-off.png','source-reference.png'):
        shutil.copy2(project/'attempt1-results-client-preflight'/name,first/name)
    spec=importlib.util.spec_from_file_location('card_decoder',here/'decode_card.py');decoder=importlib.util.module_from_spec(spec);spec.loader.exec_module(decoder)
    a=decoder.read_png(output/'effect-off.png');b=decoder.read_png(output/'source-reference.png')
    if a!=b or a[:2]!=(256,256):raise ValueError('OFF PNG mismatch')
    if len(set(tuple(a[3][j:j+3]) for j in range(0,len(a[3]),a[2])))<4:raise ValueError('empty OFF reference')
    for i in range(3):
        value=decoder.decode(output/f'card{i}.png');value['evidence_source']='UnityEditorPreflight';value['input_png']=f'card{i}.png'
        if value['card_id']!=i or value['status']!=(5 if i==2 else 0) or value['vertex_fragment_weight_error']!=0:
            raise ValueError('card metadata mismatch')
        if i<2 and value['reported_frame_angle_error_deg']>.01:raise ValueError('reported-frame angle error')
        (output/f'card{i}-decoded.json').write_text(json.dumps(value,indent=2,allow_nan=False)+'\n')
    # Archive only relevant lines rather than an SDK log with unrelated package/account noise.
    logs={}
    for name,p in [('preflight',project/'client-preflight.log'),('s2-regression',project/'regression-s2.log'),('s1-r1-regression',r1_results.parent/'regression-client-r1.log')]:
        logs[name]={'sha256':hashlib.sha256(p.read_bytes()).hexdigest(), 'retained_in_local_temp':True}
    sources={p.relative_to(here).as_posix():hashlib.sha256(p.read_bytes()).hexdigest()
             for p in here.rglob('*') if p.is_file() and 'results' not in p.relative_to(here).parts and '__pycache__' not in p.parts}
    sources.update({'shared/'+p.name:hashlib.sha256(p.read_bytes()).hexdigest()
                    for p in (here.parent/'VRCViewContext/Overlay').rglob('*') if p.is_file()})
    (output/'source-evidence.json').write_text(json.dumps({'sources':sources,'full_logs':logs,
        'client_capture_count':0,'actual_sdk_build_and_test':'NOT RUN',
        'unity_assetbundle_sha256':hashlib.sha256((project/'client-player-smoke/vlat-client-preflight').read_bytes()).hexdigest()},indent=2)+'\n')


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--project',type=Path,required=True)
    p.add_argument('--r1-results',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();collect(a.project,a.r1_results,a.output)


if __name__=='__main__':main()
