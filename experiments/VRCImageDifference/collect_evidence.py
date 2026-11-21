#!/usr/bin/env python3
"""私有画像を公開せず、数値・画像整合・原本hashを集約する（読取専用の入力）。"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import struct
import zlib
from pathlib import Path


def digest(path):
    """ファイルを変更せずSHA-256を計算する。"""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def png_rgba(path):
    """64×64 RGBA PNGのCRC/payloadを検査し、上からの行順で復号する。"""
    data = path.read_bytes(); assert data[:8] == b'\x89PNG\r\n\x1a\n'
    pos = 8; compressed = bytearray(); header = ended = False
    while pos + 12 <= len(data):
        size = struct.unpack('>I', data[pos:pos+4])[0]; kind = data[pos+4:pos+8]; end = pos + 8 + size
        assert end + 4 <= len(data) and zlib.crc32(data[pos+4:end]) & 0xffffffff == struct.unpack('>I', data[end:end+4])[0]
        if kind == b'IHDR':
            assert struct.unpack('>IIBBBBB', data[pos+8:end]) == (64,64,8,6,0,0,0); header = True
        if kind == b'IDAT': compressed.extend(data[pos+8:end])
        pos = end + 4
        if kind == b'IEND': ended = True; break
    assert header and ended and pos == len(data)
    raw = zlib.decompress(compressed); assert len(raw) == 64 * 257
    prev = [0] * 256; pixels = []
    for y in range(64):
        mode = raw[y*257]; assert mode <= 4
        row = []
        for x, value in enumerate(raw[y*257+1:(y+1)*257]):
            a = row[x-4] if x >= 4 else 0; b = prev[x]; c = prev[x-4] if x >= 4 else 0
            p = a+b-c; pa,pb,pc = abs(p-a),abs(p-b),abs(p-c)
            predictor = (0,a,b,(a+b)//2,a if pa <= pb and pa <= pc else b if pb <= pc else c)[mode]
            row.append((value+predictor) % 256)
        pixels.extend(tuple(row[i:i+4]) for i in range(0,256,4)); prev = row
    return pixels


def image_audit(source):
    """PNGとfloat生データの全画素を照合し、元画像はTEMPに保持する。"""
    output = []
    for path in sorted((source / 'private-images').rglob('*.png')):
        raw_path = path.with_suffix('.rgba32f'); raw = raw_path.read_bytes(); assert len(raw) == 64*64*16
        values = list(struct.iter_unpack('<ffff', raw)); pixels = png_rgba(path)
        for y in range(64):
            for x in range(64):
                r,g,b,a = values[y*64+x]; assert all(math.isfinite(v) for v in (r,g,b,a))
                if a <= .5: expected = (0,0,0,0)
                elif path.name.startswith('primitive-'): expected = ((r*.1031+g*.37)%1,(r*.11369)%1,(r*.13787)%1,1)
                elif path.name.startswith('normal-'): expected = (r*.5+.5,g*.5+.5,b*.5+.5,1)
                else: expected = (r,g,b,a)
                expected = [round(max(0,min(1,v))*255) for v in expected]
                assert all(abs(v-w) <= 1 for v,w in zip(expected,pixels[(63-y)*64+x])), (path.name,x,y,expected,pixels[(63-y)*64+x])
        output.append({'relative_path':path.relative_to(source).as_posix(), 'png_sha256':digest(path),
                       'raw_sha256':digest(raw_path), 'width':64,'height':64,'crc_payload_raw_color_check':'PASS'})
    assert len(output) == 240
    return output


def collect(project, source, output, sdk):
    """指定した最終runから、原本/ソース照合と数値結果を新規ファイルへ保存する。"""
    here = Path(__file__).resolve().parent
    inputs = json.loads((project/'avatar-inputs.json').read_text())
    target = output.resolve()
    protected = [project.resolve(), source.resolve(), sdk.resolve()] + [Path(m['sourcePath']).resolve().parent for m in inputs['models']]
    if any(target == p or p in target.parents or target in p.parents for p in protected):
        raise ValueError('collection must be separate from every input project/model directory')
    files = ['inputs.json','sdk-inputs.json','environment.txt','shader-audit.json']
    for phase in ('EditMode','PlayMode'):
        files += [phase+suffix for suffix in ('-cases.json','-variants.json','-shader-messages.json','-clip-shader-messages.json')]
    if any((output/name).exists() for name in files+['packages-lock.json','source-evidence.json','private-image-evidence.json']):
        raise ValueError('refusing existing collection files')
    overlay = here / 'Overlay'; evidence = {}
    for p in overlay.rglob('*'):
        if not p.is_file(): continue
        copied = project / 'Assets/HeadFrame' / p.relative_to(overlay)
        assert digest(p) == digest(copied)
        evidence[p.relative_to(here).as_posix()] = digest(p)
    original_hashes = {}
    for model in inputs['models']:
        original = Path(model['sourcePath']); copied = project / model['assetPath']
        assert digest(original) == digest(copied) == model['sha256']
        original_hashes[model['name']] = model['sha256']
    # SDK originals are read only; no assets/settings/authentication state are read or copied here.
    sdk_inputs = json.loads((project/'sdk-experiment-inputs.json').read_text())
    for name, sha in sdk_inputs['sdk_files_sha256'].items():
        assert digest(sdk/'Packages'/name) == digest(project/'Packages'/name) == sha
    images = image_audit(source)
    output.mkdir(parents=True,exist_ok=True)
    for name in files:
        destination = output/name; assert not destination.exists(), destination
        destination.write_text((source/name).read_text(encoding='utf-8-sig'))
    (output/'packages-lock.json').write_text((project/'Packages/packages-lock.json').read_text(encoding='utf-8-sig'))
    (output/'source-evidence.json').write_text(json.dumps({'experiment':'P0-S1-R1','base_commit':'5022f58',
        'overlay_sha256':evidence,'original_fbx_sha256':original_hashes,'sdk_original_and_copy_sha256':sdk_inputs['sdk_files_sha256'],
        'sources_unchanged':True,'private_images_not_distributed':True,'actual_vrchat':'NOT RUN'},indent=2)+'\n')
    (output/'private-image-evidence.json').write_text(json.dumps(images,indent=2)+'\n')
    print('PASS: source/FBX/SDK hashes; 240 private PNG CRC/payload/all-pixel colors vs float buffers. Images remain in TEMP.')


def main():
    """project・最終結果・新規集約先を指定する。"""
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('project',type=Path);p.add_argument('source',type=Path);p.add_argument('output',type=Path)
    p.add_argument('--sdk-project',type=Path,required=True)
    a=p.parse_args();collect(a.project,a.source,a.output,a.sdk_project)


if __name__ == '__main__': main()
