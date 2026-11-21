#!/usr/bin/env python3
"""S1の20件のsurface FAILを読取集約し、別測定との再現性を検査する。"""
from __future__ import annotations
import argparse
import csv
import hashlib
import json
from pathlib import Path


def load(path: Path):
    """保存CSVを読取専用で開く。"""
    with path.open(encoding='utf-8-sig',newline='') as f:return list(csv.DictReader(f))


def track(source: Path, output: Path) -> None:
    """閾値も状態も変えずに、同一失敗条件とGPU頂点比較を関連付ける。"""
    if output.exists():raise ValueError('refusing existing output')
    records=[];hashes={}
    fields=('avatar','pose','method','world_yaw','world_pitch')
    for phase in ('EditMode','PlayMode'):
        current=source/(phase+'-angles.csv');initial=source/'initial-surface'/(phase+'-angles.csv');vertices=source/(phase+'-vertices.csv')
        for p in (current,initial,vertices):hashes[p.relative_to(source).as_posix()]=hashlib.sha256(p.read_bytes()).hexdigest()
        v={(r['avatar'],r['pose'],r['method']):r for r in load(vertices)}
        first={tuple(r[k] for k in fields):r for r in load(initial) if r['method'] not in {'Baseline','RendererOnly'} and r['status']=='FAIL'}
        measured={tuple(r[k] for k in fields):r for r in load(current) if r['method'] not in {'Baseline','RendererOnly'} and r['status']=='FAIL'}
        if set(first)!=set(measured) or len(measured)!=10:raise ValueError('S1 failing grid changed')
        for key,r in measured.items():
            if abs(float(r['world_error_m'])-float(first[key]['world_error_m']))>1e-10:raise ValueError('S1 failure magnitude changed')
            vr=v[key[:3]]
            records.append({'issue':'S1-SURFACE-20','phase':phase,**{k:r[k] for k in fields},
                'world_error_m':float(r['world_error_m']),'coverage_mismatch':int(r['coverage_mismatch']),
                'gpu_vertex_probe_error_m':float(vr['vertex_world_error_m']),'surface_status':'FAIL','vertex_probe_status':vr['status'],
                'cause':'INCONCLUSIVE','independent_run_reproduced':True})
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps({'issue':'S1-SURFACE-20','source_sha256':hashes,'count':len(records),
        'reference_scope':'surface: GPU baseline vs GPU candidate; vertex probe: GPU vs GPU, different point-topology draw. Real-FBX full-vertex CPU skin comparison NOT RUN.',
        'threshold_m':0.0001,'status':'FAIL','cause':'INCONCLUSIVE','records':records},indent=2)+'\n')
    print('PASS carryover integrity: 20 S1 surface FAIL retained; independent-run reproduction confirmed; cause INCONCLUSIVE')


def main() -> int:
    """既存のS1結果と新規出力JSONを指定する。"""
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('source',type=Path);p.add_argument('output',type=Path);a=p.parse_args();track(a.source,a.output);return 0

if __name__=='__main__':raise SystemExit(main())
