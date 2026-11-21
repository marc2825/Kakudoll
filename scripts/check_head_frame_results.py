#!/usr/bin/env python3
"""P0-S1の実Unity CSVを検査する。Unity実行そのものの代替にはしない。"""

from __future__ import annotations

import argparse
import csv
import math
from collections import Counter
from pathlib import Path


def check(directory: Path) -> None:
    """候補の成立、負対照、不確定条件、行列測定の整合性を確認する。"""
    with (directory / "angles.csv").open(newline="", encoding="utf-8-sig") as stream:
        angles = list(csv.DictReader(stream))
    with (directory / "matrices.csv").open(newline="", encoding="utf-8-sig") as stream:
        matrices = list(csv.DictReader(stream))
    candidates = {
        "A_HeadRoot", "B_ChildProxyRoot", "B_ConstraintProxyRoot",
        "C_RebasedChild", "C_RebasedConstraint",
    }
    methods = candidates | {"Baseline", "B_RendererOnly"}
    poses = {
        "Rest", "HeadYaw", "HeadPitchRoll", "BodyYaw", "NeckHead", "BlendShape",
        "HalfScale", "DoubleScale", "NonUniform", "NegativeScale",
    }
    views = {(-90.0, 0.0), (-45.0, 0.0), (0.0, 0.0), (45.0, 0.0),
             (90.0, 0.0), (45.0, 30.0), (-45.0, -30.0)}
    special = {"NonUniform", "NegativeScale"}
    assert len(angles) == 1960, f"incomplete angle sweep: {len(angles)}"
    assert len(matrices) == 280, f"incomplete matrix sweep: {len(matrices)}"
    angle_keys = set()
    matrix_keys = set()
    for row in angles:
        prefix = (row["gpu_skinning"], row["axes"], row["pose"], row["method"])
        view = (float(row["world_yaw"]), float(row["world_pitch"]))
        key = prefix + view
        assert key not in angle_keys, f"duplicate: {key}"
        angle_keys.add(key)
        assert prefix[0] in {"0", "1"} and prefix[1] in {"0", "1"}
        assert prefix[2] in poses and prefix[3] in methods and view in views
        for field in ("cpu_yaw", "cpu_pitch", "gpu_yaw", "gpu_pitch", "yaw_error", "pitch_error", "world_error_m"):
            assert math.isfinite(float(row[field])), (key, field)
        assert int(row["common_pixels"]) > 0, key
        if row["pose"] in special:
            assert row["status"] == "INCONCLUSIVE", key
        elif row["method"] in candidates:
            assert row["status"] == "PASS", key
            assert float(row["yaw_error"]) <= 0.05 and float(row["pitch_error"]) <= 0.05, key
            assert float(row["world_error_m"]) <= 0.0001, key
            assert int(row["coverage_mismatch"]) <= 2, key
    for row in matrices:
        key = (row["gpu_skinning"], row["axes"], row["pose"], row["method"])
        assert key not in matrix_keys, f"duplicate matrix: {key}"
        matrix_keys.add(key)
        if row["pose"] not in special:
            assert float(row["shader_vs_root_rigid"]) <= 1e-5, key
            assert float(row["cpu_bake_rigid_error_m"]) <= 0.0001, key
        for field in ("object_to_world_column_major", "object_to_frame_rest_column_major"):
            values = [float(v) for v in row[field].split()]
            assert len(values) == 16 and all(math.isfinite(v) for v in values), (key, field)
    for gpu in ("0", "1"):
        for axes in ("0", "1"):
            for method in ("Baseline", "B_RendererOnly"):
                control = [r for r in angles if r["gpu_skinning"] == gpu and r["axes"] == axes and r["method"] == method]
                assert any(r["pose"] == "HeadYaw" and r["status"] == "FAIL" for r in control), (gpu, axes, method)
    counts = Counter(r["status"] for r in angles)
    print(f"PASS: 1960 angle cases / 280 matrix records; {dict(counts)}")
    print("PASS: 5 Head frame candidates, CPU skin reference, GPU shape preservation, negative controls")
    print("VRChat client / actual player GPU skinning / real avatars: NOT RUN")


def main() -> int:
    """保存した実測結果ディレクトリを検査する。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    check(args.directory)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
