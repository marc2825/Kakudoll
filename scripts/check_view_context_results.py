#!/usr/bin/env python3
"""P0-S2実測のgrid・選択位置・CPU角度・Stereo pair・負対照・画像を独立に検査する。"""
from __future__ import annotations
import argparse
import csv
import json
import math
import struct
import zlib
from collections import Counter
from pathlib import Path

# context, source, status, warnings, shared, directional: executable experiment contract.
CONTRACT = {
    'NativeMain':(0,1,0,1,0,0), 'NativeObserverB':(0,1,0,1,0,0), 'NativeHeadRelative':(0,1,0,1,0,0),
    'NativeOrthoPosition':(0,1,0,9,0,0), 'NativeOrthoDirection':(0,4,0,1,0,1),
    'InjectedStereo':(1,2,0,0,1,0), 'InjectedMirrorVR':(2,3,0,0,1,0), 'InjectedMirrorDesktop':(3,3,0,0,1,0),
    'InjectedHandheldVR':(4,2,0,0,1,0), 'InjectedHandheldDesktop':(5,1,0,1,0,0),
    'InjectedScreenshot':(6,1,0,1,0,0), 'InjectedScreenshotStereo':(6,2,0,0,1,0),
    'InjectedFaceWithMirror':(7,3,0,16,1,0), 'InjectedFaceUndefined':(7,0,4,0,0,0),
    'InjectedMirrorZero':(2,3,3,0,1,0), 'InjectedUnknownMirror':(0,0,1,0,0,0),
    'InjectedConflicting':(0,0,2,0,0,0), 'NativeStrictStereoMissing':(0,1,5,1,0,0),
    'InjectedEmptyStereo':(1,0,9,0,0,0), 'InjectedNonFinite':(0,1,6,1,0,0), 'InjectedDegenerate':(0,1,7,1,0,0),
    'InjectedOrigin':(0,1,0,1,0,0), 'InjectedPhotoInactive':(4,1,0,3,0,0), 'InjectedScreenDecoy':(0,1,0,5,0,0),
    'NegativePerEye':(1,2,0,0,1,0), 'NegativeMirrorSign':(2,3,0,0,1,0),
}
POSES={'Rest','HeadYaw','BodyYaw','NeckHead'}
METHODS={'B_ChildProxyRoot','B_ConstraintProxyRoot'}
VIEWS={(-90.,0.),(-45.,0.),(0.,0.),(45.,0.),(90.,0.),(45.,30.),(-45.,-30.)}
META=('context','source','status','warnings','shared','directional')


def rows(path: Path) -> list[dict[str,str]]:
    """BOMも受け入れCSVを読む。"""
    with path.open(encoding='utf-8-sig',newline='') as stream:return list(csv.DictReader(stream))


def read_json(path: Path):
    """JSONを読み込む。"""
    return json.loads(path.read_text(encoding='utf-8-sig'))


def vector(row: dict[str,str], prefix: str) -> tuple[float,float,float]:
    """CSVのworld vectorを読む。"""
    return tuple(float(row[prefix+'_'+axis]) for axis in 'xyz')


def distance(a,b) -> float:
    """二点間の距離を返す。"""
    return math.sqrt(sum((x-y)**2 for x,y in zip(a,b)))


def local_vector(world, frame, directional=False):
    """保存されたHead quaternionを独立に反転し、方向または点を変換する。"""
    p=list(world) if directional else [v-float(frame[a]) for v,a in zip(world,'xyz')]
    q=[float(frame[a]) for a in ('qx','qy','qz','qw')];n=math.sqrt(sum(x*x for x in q));q=[x/n for x in q]
    u=[-q[0],-q[1],-q[2]]
    def cross(a,b):return [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]]
    t=[2*x for x in cross(u,p)];c=cross(u,t)
    return [p[i]+q[3]*t[i]+c[i] for i in range(3)]


def check_png(path: Path) -> dict:
    """実画像のchunk CRCとRGB payloadを検査し、切れた出力や改変を拒否する。"""
    data=path.read_bytes()
    if data[:8]!=b'\x89PNG\r\n\x1a\n':raise ValueError('invalid PNG signature')
    offset=8;compressed=bytearray();ended=False;header=False
    while offset+12<=len(data):
        length=struct.unpack('>I',data[offset:offset+4])[0];kind=data[offset+4:offset+8];end=offset+8+length
        if end+4>len(data) or zlib.crc32(data[offset+4:end]) & 0xffffffff != struct.unpack('>I',data[end:end+4])[0]:raise ValueError('PNG CRC/length mismatch')
        if kind==b'IHDR':
            width,height,bits,color,compression,filter_mode,interlace=struct.unpack('>IIBBBBB',data[offset+8:end])
            if (width,height,bits,color,compression,filter_mode,interlace)!=(64,64,8,2,0,0,0):raise ValueError('unexpected PNG format')
            header=True
        elif kind==b'IDAT':compressed.extend(data[offset+8:end])
        elif kind==b'IEND':ended=True
        offset=end+4
        if ended:break
    if not header or not ended or offset!=len(data):raise ValueError('incomplete PNG')
    raw=zlib.decompress(compressed)
    if len(raw)!=64*(64*3+1) or any(raw[i*(64*3+1)]>4 for i in range(64)):raise ValueError('invalid PNG RGB payload')
    previous=[0]*192;colors=Counter()
    for y in range(64):
        kind=raw[y*193];line=raw[y*193+1:(y+1)*193];decoded=[]
        for x,value in enumerate(line):
            a=decoded[x-3] if x>=3 else 0;b=previous[x];c=previous[x-3] if x>=3 else 0
            p=a+b-c;pa=abs(p-a);pb=abs(p-b);pc=abs(p-c)
            predictor=(0,a,b,(a+b)//2,a if pa<=pb and pa<=pc else b if pb<=pc else c)[kind]
            decoded.append((value+predictor)%256)
        colors.update(tuple(decoded[x:x+3]) for x in range(0,192,3));previous=decoded
    return dict(colors)


def check(directory: Path) -> None:
    """模擬条件を実clientのPASSへ昇格させず、測定値と契約の整合を検証する。"""
    sdk=read_json(directory/'sdk-inputs.json')
    if sdk['sdk_versions']!={'com.vrchat.base':'3.10.5','com.vrchat.avatars':'3.10.5'}:raise ValueError('SDK version mismatch')
    inputs=read_json(directory/'inputs.json')
    if inputs['experiment']!='P0-S2' or inputs['live_client']!='NOT RUN':raise ValueError('simulation promoted to client evidence')
    totals=Counter();maximum_angle=0.;maximum_weight=0.;maximum_position=0.;stereo_delta=0.;image_rows={}
    for phase in ('EditMode','PlayMode'):
        data=rows(directory/(phase+'-views.csv'));frames_data=rows(directory/(phase+'-frames.csv'))
        frames={(f['axes'],f['pose']):f for f in frames_data}
        if len(data)!=5824 or len(frames_data)!=8 or len(frames)!=8:raise ValueError('incomplete '+phase)
        for f in frames_data:
            if f['phase']!=phase or f['axes'] not in {'0','1'} or f['pose'] not in POSES:raise ValueError('bad Head frame')
            if not all(math.isfinite(float(f[k])) for k in ('x','y','z','qx','qy','qz','qw')):raise ValueError('nonfinite Head frame')
        keys=set();pairs={};relative={}
        for r in data:
            key=(r['axes'],r['method'],r['pose'],float(r['world_yaw']),float(r['world_pitch']),r['scenario'],r['eye'])
            if key in keys or r['phase']!=phase or key[0] not in {'0','1'} or key[1] not in METHODS or key[2] not in POSES or key[3:5] not in VIEWS or key[5] not in CONTRACT or key[6] not in {'0','1'}:raise ValueError('duplicate/unexpected '+str(key))
            keys.add(key);contract=CONTRACT[r['scenario']]
            if phase=='EditMode' and r['axes']=='0' and r['method']=='B_ChildProxyRoot' and r['pose']=='Rest' and float(r['world_pitch'])==0:
                image_rows[(r['scenario'],int(r['eye']),float(r['world_yaw']))]=r
            if tuple(int(r['expected_'+name]) for name in META)!=contract:raise ValueError('altered experiment contract')
            negative=r['scenario'].startswith('Negative')
            if r['negative']!=('1' if negative else '0') or r['evidence']!=('UnityCamera' if r['scenario'].startswith('Native') else 'Injected'):raise ValueError('evidence misclassification')
            names=('cpu_yaw','cpu_pitch','gpu_yaw','gpu_pitch','cpu_weight','vertex_weight','fragment_weight','vertex_status','yaw_error','pitch_error','position_error_m','weight_error')
            values={n:float(r[n]) for n in names}
            if not all(math.isfinite(v) for v in values.values()):raise ValueError('nonfinite result')
            actual_meta=tuple(float(r['gpu_'+name]) for name in META)
            if not all(math.isfinite(v) for v in actual_meta):raise ValueError('nonfinite metadata')
            yerr=abs((values['gpu_yaw']-values['cpu_yaw']+180)%360-180);perr=abs(values['gpu_pitch']-values['cpu_pitch'])
            werr=max(abs(values['vertex_weight']-values['cpu_weight']),abs(values['fragment_weight']-values['cpu_weight']))
            poserr=0.
            if contract[2]==0:
                selected=vector(r,'expected');gpu=vector(r,'gpu')
                if not all(math.isfinite(v) for v in selected+gpu):raise ValueError('nonfinite position')
                source=contract[1]
                if source==2:expected=tuple((a+b)*.5 for a,b in zip(vector(r,'left'),vector(r,'right')))
                elif source==3:expected=vector(r,'mirror')
                else:expected=vector(r,'draw')
                if distance(selected,expected)>2e-5:raise ValueError('CPU selected source mismatch')
                local=local_vector(vector(r,'direction') if contract[5] else selected,frames[(r['axes'],r['pose'])],bool(contract[5]))
                length=math.sqrt(sum(v*v for v in local));yaw=math.degrees(math.atan2(local[0],local[2]));yaw=yaw+360 if yaw<=-180 else yaw
                pitch=math.degrees(math.asin(max(-1,min(1,local[1]/length))))
                if abs((yaw-values['cpu_yaw']+180)%360-180)>0.0002 or abs(pitch-values['cpu_pitch'])>0.0002:raise ValueError('independent Head angle reference mismatch')
                t=max(0,min(1,yaw/60));weight=t*t*(3-2*t)
                if abs(weight-values['cpu_weight'])>2e-5:raise ValueError('CPU asymmetric weight mismatch')
                poserr=distance(selected,gpu)
            elif any(r[n] for n in ('gpu_x','gpu_y','gpu_z','expected_x','expected_y','expected_z')):raise ValueError('invalid mode supplied a usable position')
            if abs(yerr-values['yaw_error'])>0.0002 or abs(perr-values['pitch_error'])>0.0002 or abs(werr-values['weight_error'])>2e-6 or abs(poserr-values['position_error_m'])>2e-6:raise ValueError('recorded error mismatch')
            passed=yerr<=0.05 and perr<=0.05 and werr<=0.0001 and poserr<=1e-5 and all(abs(a-b)<0.01 for a,b in zip(actual_meta,contract)) and abs(values['vertex_status']-contract[2])<0.01
            if r['status'] not in {'PASS','FAIL'} or (r['status']=='PASS')!=passed:raise ValueError('false status')
            if negative and passed:raise ValueError('negative control unexpectedly passed')
            if not negative and not passed:raise ValueError('positive candidate failed: '+str(key))
            if not negative:
                maximum_angle=max(maximum_angle,yerr,perr);maximum_weight=max(maximum_weight,werr);maximum_position=max(maximum_position,poserr)
            if not negative and contract[4] and contract[2]==0:pairs.setdefault(key[:-1],{})[r['eye']]=r
            if r['scenario']=='NativeHeadRelative':
                group=(r['axes'],r['method'],r['world_yaw'],r['world_pitch'],r['eye']);relative.setdefault(group,[]).append(r)
            totals[(r['evidence'],r['status'])]+=1
        for pair in pairs.values():
            if set(pair)!={'0','1'}:raise ValueError('missing shared-eye pair')
            delta=max(abs(float(pair['0'][n])-float(pair['1'][n])) for n in ('gpu_yaw','gpu_pitch','vertex_weight','fragment_weight'))
            stereo_delta=max(stereo_delta,delta)
            if delta>1e-5:raise ValueError('eye-dependent shared evaluation')
        for rr in relative.values():
            if len(rr)!=4 or max(float(r['gpu_yaw']) for r in rr)-min(float(r['gpu_yaw']) for r in rr)>0.01 or max(float(r['gpu_pitch']) for r in rr)-min(float(r['gpu_pitch']) for r in rr)>0.01:raise ValueError('Head/camera co-rotation invariance failed')
        if read_json(directory/(phase+'-shader-messages.json')):raise ValueError('Shader compile messages require review')
        print(phase,len(data),'cases',dict(Counter(r['status'] for r in data)),len(pairs),'shared-eye pairs')
    images=read_json(directory/'images.json')
    if len(images)!=84 or len({r['file'] for r in images})!=84:raise ValueError('incomplete/duplicate image evidence')
    for r in images:
        p=directory/r['file']
        if not r['file'].startswith('images/') or '..' in Path(r['file']).parts:raise ValueError('invalid image evidence')
        colors=check_png(p)
        if r['evidence'] not in {'UnityCamera','Injected'}:raise ValueError('image evidence misclassification')
        measured=image_rows[(r['scenario'],r['eye'],r['worldYaw'])]
        if r['evidence']!=measured['evidence']:raise ValueError('image source mismatch')
        if float(measured['gpu_status'])!=0:expected=(255,0,255)
        elif r['mode']=='view color':
            weight=float(measured['fragment_weight']);blue=max(0,min(1,float(measured['gpu_pitch'])/90+0.5))
            expected=tuple(round(255*x) for x in (weight,1-weight,blue))
        elif r['mode']=='context palette':
            expected=((128,128,128),(0,178,76),(153,0,255),(0,178,255),(255,102,0),(255,255,0),(0,51,255),(255,51,128))[int(float(measured['gpu_context']))]
        else:raise ValueError('unknown image mode')
        covered={c:n for c,n in colors.items() if c!=(0,0,0)}
        if not covered or any(max(abs(a-b) for a,b in zip(color,expected))>1 for color in covered):raise ValueError('PNG color disagrees with float measurement')
    if (directory/'s1-carryover.json').exists():
        carry=read_json(directory/'s1-carryover.json')
        if carry['count']!=20 or carry['status']!='FAIL' or carry['cause']!='INCONCLUSIVE' or carry['threshold_m']!=0.0001 or len(carry['records'])!=20:raise ValueError('S1 gate silently changed')
        if any(r['surface_status']!='FAIL' or not r['independent_run_reproduced'] or r['world_error_m']<=0.0001 for r in carry['records']):raise ValueError('S1 failure discarded')
    if (directory/'client-results.json').exists():
        client=read_json(directory/'client-results.json')
        if client['overall']!='NOT RUN' or any(t['status']!='NOT RUN' or t['evidence'] is not None for t in client['tests']):raise ValueError('unproven client result')
    print('PASS result integrity / CPU reference / shared pairs / invariance / negative controls;',dict(totals))
    print('max angle(deg)=',maximum_angle,'position(m)=',maximum_position,'weight=',maximum_weight,'shared-eye delta=',stereo_delta)
    print('Actual VRChat client / XR stereo binding / Mirror / Camera / Head Chop: NOT RUN')


def main() -> int:
    """保存結果を検証する。UnityやVRChatを実行したことにはしない。"""
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);args=p.parse_args();check(args.directory);return 0

if __name__=='__main__':raise SystemExit(main())
