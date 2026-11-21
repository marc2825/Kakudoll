#!/usr/bin/env python3
"""S1-R1の元FAIL再現・画素/三角形証拠を独立に照合する。FAILを自動昇格しない。"""
from __future__ import annotations
import argparse
import json
import math
import struct
from collections import Counter
from pathlib import Path

THRESHOLD = 0.0001


def load(path):
    """非有限JSONを拒否して読取専用で読む。"""
    def invalid(value):
        raise ValueError('nonfinite JSON: ' + value)
    return json.loads(path.read_text(encoding='utf-8-sig'), parse_constant=invalid)


def distance(a, b):
    """保存されたRGB位置からEuclidean距離を再計算する。"""
    f32 = lambda x: struct.unpack('<f', struct.pack('<f', x))[0]
    return math.sqrt(sum(f32(f32(x) - f32(y)) ** 2 for x, y in zip(a[:3], b[:3])))


def key(row):
    """phaseごとの20件を一意に識別する。"""
    return (row['phase'], row['avatar'], row['pose'], row['method'], float(row.get('yaw', row.get('world_yaw'))), float(row.get('pitch', row.get('world_pitch'))))


def inspect(folder: Path, frozen: Path):
    """記録と元の失敗条件・閾値を照合し、観測した現象だけを分類する。"""
    expected = {key(r): r for r in load(frozen)['records']}
    assert len(expected) == 20
    rows = []; variants = []
    for phase in ('EditMode', 'PlayMode'):
        measured = load(folder / (phase + '-cases.json'))
        assert len(measured) == 10 and all(r['phase'] == phase for r in measured)
        rows += measured
        variants += load(folder / (phase + '-variants.json'))
        assert load(folder / (phase + '-shader-messages.json')) == []
        assert load(folder / (phase + '-clip-shader-messages.json')) == []
    assert len({key(r) for r in rows}) == 20 and {key(r) for r in rows} == set(expected)
    causes = Counter(); max_original = max_probe = max_same = max_cpu = 0
    for r in rows:
        e = expected[key(r)]
        assert r['originalError_m'] > THRESHOLD and r['originalCoverage'] == 0
        assert abs(r['originalError_m'] - e['world_error_m']) < 1e-9, ('original reproduction', key(r))
        assert abs(r['originalExpected_m'] - e['world_error_m']) < 1e-9
        assert r['viewProjectionMatrixError'] == 0 and r['solidMax'] == 0 and r['coverage'] == 0
        assert r['vertexMax_m'] < THRESHOLD and max(r['cpuBaselineMax_m'], r['cpuCandidateMax_m']) < THRESHOLD
        assert r['largePixels'] == len(r['pixels']) > 0
        assert r['matchedPixels'] == r['covered'] and r['switchedPixels'] == 0
        assert r['matchedBarycentricAllMax_m'] < THRESHOLD and r['matchedBarycentricStatus'] == 'PASS'
        assert max(p['matchedBarycentricError_m'] for p in r['pixels']) <= r['matchedBarycentricAllMax_m'] + 1e-12
        for p in r['pixels']:
            assert p['index'] == p['y'] * 64 + p['x'] and 0 <= p['index'] < 4096
            assert abs(distance(p['baselineWorld'], p['candidateWorld']) - p['probe_error_m']) < 3e-10
            assert p['probe_error_m'] > THRESHOLD or p['original_error_m'] > THRESHOLD
            assert p['primitiveVertexError_m'] < THRESHOLD
            same = p['baselinePrimitive'] == p['candidatePrimitive'] and p['baselineSlot'] == p['candidateSlot']
            assert p['samePrimitive'] == same
            a0, a1, b0, b1 = (p[n][3] > .5 for n in ('primitiveA_baseline', 'primitiveA_candidate', 'primitiveB_baseline', 'primitiveB_candidate'))
            if same:
                ra, rb = p['rasterA'], p['rasterB']
                assert ra['flippedY'] == rb['flippedY'] == True
                assert ra['snapped'] != rb['snapped'], ('no snap crossing', key(r))
                for model in (ra, rb):
                    for screen, snapped in zip(model['screen'], model['snapped']):
                        assert all(round(x * 256) / 256 == y for x, y in zip(screen, snapped))
                    assert abs(sum(model['weights']) - 1) < 1e-12
                    assert min(model['weights']) >= -0.01 and max(model['weights']) <= 1.01
                    assert model['predictedError_m'] < 1e-6
                    assert model['unsnappedError_m'] > model['predictedError_m'] * 10
                # One grid step must explain the large raster difference, not merely correlate with it.
                predicted_delta = math.dist(ra['predicted'], rb['predicted'])
                assert abs(predicted_delta - p['probe_error_m']) <= ra['predictedError_m'] + rb['predictedError_m'] + 3e-10
                assert p['matchedBarycentricError_m'] < THRESHOLD
                cause = 'D3D_SUBPIXEL_SNAP_INTERPOLATION'
            elif a0 and b1 and (not a1 or not b0):
                cause = 'PRIMITIVE_COVERAGE_SWITCH'
            elif a0 and a1 and b0 and b1:
                cause = 'DEPTH_WINNER_SWITCH'
            else:
                cause = 'UNEXPLAINED'
            causes[cause] += 1
        max_original = max(max_original, r['originalError_m'])
        max_probe = max(max_probe, r['probeError_m'])
        max_same = max(max_same, r['samePrimitiveMax_m'])
        max_cpu = max(max_cpu, r['cpuBaselineMax_m'], r['cpuCandidateMax_m'])
    assert len(variants) == 222
    assert len({(r['phase'], r['key'], r['variant']) for r in variants}) == 222
    common_variants = {'BaselineRepeat', 'ProbeVsOriginalBaseline', 'ProbeVsOriginalCandidate', 'WorldClip', 'CullBack',
                       'ZAlways', 'BakedStatic', 'Resolution128', 'Resolution256', 'HeadOnly', 'NegativeMeshShift1mm'}
    required = set()
    for r in rows:
        case_name = f"{r['avatar']}-{r['pose']}-{r['method']}-{r['yaw']:g}-{r['pitch']:g}"
        names = common_variants | ({'BlinkOff'} if r['pose'] == 'BlendShape' else set())
        required.update((r['phase'], case_name, name) for name in names)
    assert {(r['phase'], r['key'], r['variant']) for r in variants} == required
    for r in variants:
        assert r['covered'] > 0 and math.isfinite(r['maximum_m'])
        status = 'PASS' if r['coverage'] <= 2 and r['maximum_m'] <= THRESHOLD else 'FAIL'
        assert r['strictSurfaceStatus'] == status
        if r['variant'] == 'BaselineRepeat':
            assert r['coverage'] == 0 and r['maximum_m'] == 0
        if r['variant'] == 'NegativeMeshShift1mm':
            assert status == 'FAIL' and r['vertexWorldError_m'] > THRESHOLD
    print('PASS integrity: original 20 FAIL reproduced; thresholds/records preserved')
    print('bad-pixel observations:', dict(causes))
    print('max(m): original', max_original, 'probe', max_probe, 'same primitive', max_same, 'CPU LBS', max_cpu)
    print('matched barycentric all-covered max(m):', max(r['matchedBarycentricAllMax_m'] for r in rows))
    print('diagnostic variants:', dict(Counter((r['variant'],r['strictSurfaceStatus']) for r in variants)))
    print('No automatic surface FAIL reclassification. Actual VRChat / actual Player: NOT RUN')
    return rows, variants


def main():
    """実測結果ディレクトリを指定して整合検査する。"""
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('folder', type=Path)
    p.add_argument('--frozen', type=Path, default=Path(__file__).resolve().parents[1] / 'experiments/VRCViewContext/results/2026-10-17-unity2022.3.22f1/s1-carryover.json')
    a = p.parse_args(); inspect(a.folder, a.frozen)


if __name__ == '__main__':
    main()
