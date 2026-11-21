#!/usr/bin/env python3
"""V1〜V10記録の不足/改ざん/模擬結果混入を検出する。実機成功を推測しない。"""
from __future__ import annotations
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

REQUIRED = {
    'V1': ['desktop_sign','head_rotation','proxy_child_vs_constraint','effect_off'],
    'V2': ['mirror_position','different_from_main','asymmetric_sign'],
    'V3': ['left_eye','right_eye','same_frame','shared_center','vertex_weight_equal'],
    'V4': ['left_eye','right_eye','same_frame','mirror_center','vertex_weight_equal'],
    'V5': ['handheld_desktop','handheld_vr','screenshot','independent_from_main'],
    'V6': ['face_flag','distinct_from_regular_mirror','evaluation_policy_verified'],
    'V7': ['first_person','mirror_control','keep_head_off','keep_head_on','proxy_tracking','effect_off'],
    'V8': ['observer_a','observer_b','same_avatar','same_time','independent_angles'],
    'V9': ['actual_path_without_arrays','explicit_invalid_status','no_silent_fallback'],
    'V10': ['head_rotation','body_rotation','avatar_scale','proxy_tracking'],
}


def inspect(record: Path):
    data=json.loads(record.read_text(),parse_constant=lambda value: (_ for _ in ()).throw(ValueError(value)))
    if data.get('evidence_kind') != 'ActualVRChatClient': raise ValueError('not an actual-client record')
    tests=data['tests']
    if len(tests)!=10 or {t['id'] for t in tests}!=set(REQUIRED): raise ValueError('require unique V1..V10')
    for test in tests:
        status=test['status']; identity=test['id']
        if status not in ('PASS','FAIL','NOT RUN','INCONCLUSIVE'): raise ValueError('status')
        if status=='NOT RUN':
            if test.get('evidence'): raise ValueError(identity+': NOT RUN must not claim captured evidence')
            continue
        provenance=test.get('provenance') or {}
        if not all(provenance.get(k) for k in ('client_version','session_label','capture_method','human_operator_attestation')):
            raise ValueError(identity+': missing runtime provenance')
        if provenance.get('source')!='VRChatClient': raise ValueError(identity+': simulated evidence')
        if not test.get('evidence'): raise ValueError(identity+': missing evidence')
        for evidence in test['evidence']:
            relative=Path(evidence['path'])
            if relative.is_absolute() or '..' in relative.parts: raise ValueError('evidence path must stay inside record folder')
            path=record.parent/relative
            if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest()!=evidence['sha256']:
                raise ValueError(identity+': missing/hash-mismatched evidence')
        if status=='PASS':
            if set(test['checks'])!=set(REQUIRED[identity]) or not all(v is True for v in test['checks'].values()):
                raise ValueError(identity+': incomplete acceptance conditions')
            if identity=='V8' and provenance.get('avatar_distribution')!='private_uploaded_with_explicit_approval':
                raise ValueError('V8 cannot PASS with Build & Test local-only avatar')
            if identity in ('V3','V4'):
                pair=test.get('stereo_pair') or {}
                if pair.get('same_render_frame') is not True: raise ValueError('stereo pair must be synchronous')
                decoded=[]
                for eye in ('left','right'):
                    relative=pair.get(eye+'_decoded')
                    if not relative or relative not in [e['path'] for e in test['evidence']]: raise ValueError('missing hashed eye telemetry')
                    value=json.loads((record.parent/relative).read_text())
                    if value['evidence_source']!='VRChatClient' or value['status']!=0 or value['shared_stereo']!=1:
                        raise ValueError('invalid/unshared stereo evaluation')
                    decoded.append(value)
                if {v['eye'] for v in decoded}!={0,1}: raise ValueError('requires both native eye indices')
                if abs(decoded[0]['vertex_weight']-decoded[1]['vertex_weight'])>1e-6:
                    raise ValueError('left/right vertex weights differ')
                if max(abs(a-b) for a,b in zip(decoded[0]['selected_position'],decoded[1]['selected_position']))>1e-5:
                    raise ValueError('left/right evaluation positions differ')
    counts=Counter(t['status'] for t in tests)
    expected='PASS' if counts['PASS']==10 else ('PARTIAL PASS' if counts['PASS'] else 'NO-GO')
    if data['overall']!=expected: raise ValueError('overall must match statuses; any unverified mode blocks complete PASS')
    return dict(counts)


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('record',type=Path);args=parser.parse_args()
    print(json.dumps({'integrity':'PASS','results':inspect(args.record),'scope':'record integrity only; human evidence still requires review'}))


if __name__=='__main__': main()
