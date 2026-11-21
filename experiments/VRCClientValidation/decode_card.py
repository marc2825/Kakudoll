#!/usr/bin/env python3
"""診断cardのPNGを標準ライブラリだけで復号する。復号成功は実機PASSを意味しない。"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import zlib


def read_png(path: Path):
    data = path.read_bytes()
    if data[:8] != b'\x89PNG\r\n\x1a\n': raise ValueError('PNG signature')
    offset = 8; packed = bytearray(); header = None; ended = False
    while offset < len(data):
        length = struct.unpack_from('>I', data, offset)[0]; kind = data[offset+4:offset+8]
        payload = data[offset+8:offset+8+length]
        crc = struct.unpack_from('>I', data, offset+8+length)[0]
        if zlib.crc32(kind+payload) & 0xffffffff != crc: raise ValueError('PNG CRC')
        if kind == b'IHDR': header = struct.unpack('>IIBBBBB', payload)
        elif kind == b'IDAT': packed.extend(payload)
        elif kind == b'IEND': ended = True; break
        offset += 12+length
    if not ended or header is None: raise ValueError('incomplete PNG')
    width, height, depth, color, compression, filtering, interlace = header
    if depth != 8 or color not in (2, 6) or compression or filtering or interlace:
        raise ValueError('only noninterlaced 8-bit RGB/RGBA PNG supported; export lossless PNG')
    if width < 34 or height < 64 or width*height > 32_000_000: raise ValueError('PNG size')
    channels = 3 if color == 2 else 4; stride = width*channels
    raw = zlib.decompress(packed)
    if len(raw) != height*(stride+1): raise ValueError('PNG raw size')
    pixels = bytearray(width*height*channels); prior = bytearray(stride)
    for y in range(height):
        start = y*(stride+1); mode = raw[start]; row = bytearray(raw[start+1:start+1+stride])
        if mode > 4: raise ValueError('PNG filter')
        for x in range(stride):
            left = row[x-channels] if x >= channels else 0; up = prior[x]
            upper_left = prior[x-channels] if x >= channels else 0
            if mode == 1: predictor = left
            elif mode == 2: predictor = up
            elif mode == 3: predictor = (left+up)//2
            elif mode == 4:
                p = left+up-upper_left; distances = [abs(p-left), abs(p-up), abs(p-upper_left)]
                predictor = [left, up, upper_left][distances.index(min(distances))]
            else: predictor = 0
            row[x] = (row[x]+predictor) & 255
        pixels[y*stride:(y+1)*stride] = row; prior = row
    return width, height, channels, pixels


def solve(matrix, values):
    a = [list(row)+[value] for row, value in zip(matrix, values)]; n = len(a)
    for i in range(n):
        pivot = max(range(i, n), key=lambda j: abs(a[j][i]))
        a[i], a[pivot] = a[pivot], a[i]
        divisor = a[i][i]
        if abs(divisor) < 1e-12: raise ValueError('degenerate corners/frame')
        a[i] = [v/divisor for v in a[i]]
        for j in range(n):
            if i == j: continue
            factor = a[j][i]; a[j] = [v-factor*w for v, w in zip(a[j], a[i])]
    return [row[-1] for row in a]


def homography(corners):
    rows = []; values = []
    for (u, v), (x, y) in zip(((0,0), (1,0), (1,1), (0,1)), corners):
        rows += [[u,v,1,0,0,0,-x*u,-x*v], [0,0,0,u,v,1,-y*u,-y*v]]; values += [x,y]
    h = solve(rows, values)
    def point(u, v):
        denominator = h[6]*u+h[7]*v+1
        if abs(denominator) < 1e-12: raise ValueError('homography denominator')
        return (h[0]*u+h[1]*v+h[2])/denominator, (h[3]*u+h[4]*v+h[5])/denominator
    return point


def decode(path: Path, corners=None):
    width, height, channels, pixels = read_png(path)
    if corners is None: corners = [(-.5,-.5),(width-.5,-.5),(width-.5,height-.5),(-.5,height-.5)]
    mapping = homography(corners); matches = []
    # Try all quadrilateral orientations, including the horizontal reflection in mirrors.
    for rotate in range(4):
        for reflected in (False, True):
            def sample(u, v):
                if reflected: u = 1-u
                for _ in range(rotate): u, v = 1-v, u
                x, y = mapping(u, v); x, y = int(round(x)), int(round(y))
                if not (0 <= x < width and 0 <= y < height): raise ValueError('card outside PNG')
                i = (y*width+x)*channels
                rgb = pixels[i:i+3]
                if max(rgb)-min(rgb) > 30: raise ValueError('colored payload cell: wrong corners/occlusion')
                level = sum(rgb)/3
                if 80 < level < 175: raise ValueError('ambiguous cell: increase capture resolution')
                return int(level >= 175)
            try:
                words = [sum(sample((col+1.5)/34, (row+8.5)/64) << col for col in range(32)) for row in range(56)]
                checksum = 2166136261
                for word in words[:55]: checksum = ((checksum ^ word)*16777619) & 0xffffffff
                if words[:2] != [0x564c4331,1] or words[55] != checksum: continue
                matches.append((words, rotate, reflected))
            except ValueError: continue
    if len(matches) != 1: raise ValueError(f'valid orientation count {len(matches)}; checksum/magic/corners invalid')
    words, rotate, reflected = matches[0]
    floats = [struct.unpack('<f', struct.pack('<I', w))[0] for w in words]
    nonfinite=[index for index in range(5,55) if not math.isfinite(floats[index])]
    # Preserve raw invalid globals as text plus original words; never emit nonstandard JSON NaN.
    for index in nonfinite: floats[index]=str(floats[index])
    keys = ['mirror_mode','camera_mode','face_mode','context','source','status','warnings','shared_stereo',
            'weight','yaw','pitch','vertex_weight','vertex_status']
    result = dict(zip(keys, floats[5:18]))
    result.update(card_id=words[2], stereo_available=words[3], eye=words[4],
                  selected_position=floats[18:21], draw_position=floats[21:24], mirror_position=floats[24:27],
                  screen_position=floats[27:30], photo_position=floats[30:33], left_position=floats[33:36],
                  right_position=floats[36:39], orthographic=floats[39], effect=floats[40], require_stereo=floats[41],
                  frame_origin=floats[42:45], frame_x=floats[45:48], frame_y=floats[48:51], frame_z=floats[51:54],
                  shader_time=floats[54], orientation=dict(quarter_turns=rotate, reflected=reflected),
                  png_sha256=hashlib.sha256(path.read_bytes()).hexdigest(), words=words, nonfinite_float_word_indices=nonfinite)
    if not all(isinstance(result[k],(int,float)) for k in ('status','weight','vertex_weight')):
        raise ValueError('nonfinite evaluation metadata')
    result['vertex_fragment_weight_error'] = abs(result['weight']-result['vertex_weight'])
    if result['status'] == 0:
        columns = [result['frame_x'], result['frame_y'], result['frame_z']]
        direction = solve([[columns[c][r] for c in range(3)] for r in range(3)],
                          [result['selected_position'][i]-result['frame_origin'][i] for i in range(3)])
        length = math.sqrt(sum(v*v for v in direction))
        if length < 1e-6: raise ValueError('degenerate selected direction')
        yaw = math.degrees(math.atan2(direction[0],direction[2])); pitch = math.degrees(math.asin(max(-1,min(1,direction[1]/length))))
        result['reported_frame_cpu_yaw'] = yaw; result['reported_frame_cpu_pitch'] = pitch
        result['reported_frame_angle_error_deg'] = max(abs((yaw-result['yaw']+180)%360-180),abs(pitch-result['pitch']))
        result['reference_scope'] = 'same reported shader frame; NOT independent actual Head bone evidence'
    result['acceptance_status'] = 'UNASSESSED: decode and checksum only; human provenance and mode evidence required'
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__);parser.add_argument('png',type=Path)
    parser.add_argument('--corners',nargs=8,type=float,metavar='XY',help='card outer corners TL TR BR BL in screenshot pixels')
    parser.add_argument('--source',choices=['UnityEditorPreflight','VRChatClient'],required=True)
    parser.add_argument('--output',type=Path,required=True); args = parser.parse_args()
    corners = list(zip(args.corners[::2],args.corners[1::2])) if args.corners else None
    result = decode(args.png,corners);result['evidence_source']=args.source;result['input_png']=str(args.png)
    if args.output.exists(): raise ValueError('refusing existing output')
    args.output.write_text(json.dumps(result,indent=2,allow_nan=False)+'\n'); print(f'decoded card={result["card_id"]} status={result["status"]} yaw={result["yaw"]:.6f}; UNASSESSED ({args.source})')


if __name__ == '__main__': main()
