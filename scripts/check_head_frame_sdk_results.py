#!/usr/bin/env python3
"""実Unity SDK測定CSVの網羅性、誤差、負対照、変換結果を検査する。"""

from __future__ import annotations

import argparse
import csv
import json
import math
from collections import Counter
from pathlib import Path


def load_rows(path: Path) -> list[dict[str, str]]:
    """CSVの数値文字列を保持して行を読み込む。"""
    with path.open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def check(directory: Path) -> None:
    """観測したShader値をCPU値と再比較し、SDK candidateの合格条件を確認する。"""
    expected_backends = {"Unity", "VrcDirect", "VrcConverted", "VrcDisabled"}
    expected_methods = {"B_ChildProxyRoot", "B_ConstraintProxyRoot", "C_RebasedConstraint"}
    expected_poses = {"Rest", "HeadYaw", "HeadPitchRoll", "BodyYaw", "NeckHead", "BlendShape", "HalfScale", "DoubleScale"}
    expected_views = {(-90.0, 0.0), (-45.0, 0.0), (0.0, 0.0), (45.0, 0.0), (90.0, 0.0), (45.0, 30.0), (-45.0, -30.0)}
    all_failures = []
    inputs = json.loads((directory / "inputs.json").read_text(encoding="utf-8-sig"))
    if inputs["sdk_versions"] != {"com.vrchat.base": "3.10.5", "com.vrchat.avatars": "3.10.5"}:
        raise ValueError("SDK versions are not pinned to 3.10.5")
    for phase in ("EditMode", "PlayMode"):
        rows = load_rows(directory / f"{phase}-angles.csv")
        setup = load_rows(directory / f"{phase}-setup.csv")
        if len(rows) != 1344 or len(setup) != 8:
            raise ValueError(f"incomplete {phase}: {len(rows)} angle / {len(setup)} setup rows")
        keys = set()
        for row in rows:
            key = (row["backend"], row["axes"], row["pose"], row["method"], float(row["world_yaw"]), float(row["world_pitch"]))
            if key in keys:
                raise ValueError(f"duplicate {phase} case: {key}")
            keys.add(key)
            if row["phase"] != phase or key[0] not in expected_backends or key[1] not in {"0", "1"} or key[2] not in expected_poses or key[3] not in expected_methods or key[4:] not in expected_views:
                raise ValueError(f"unexpected case: {phase} {key}")
            numeric = {field: float(row[field]) for field in (
                "cpu_yaw", "cpu_pitch", "gpu_yaw", "gpu_pitch", "yaw_error", "pitch_error", "proxy_position_error_m",
                "proxy_rotation_error_deg", "shader_root_rigid_error", "coverage_mismatch", "world_error_m")}
            if not all(math.isfinite(value) for value in numeric.values()):
                raise ValueError(f"non-finite measurement: {phase} {key}")
            yaw = abs((numeric["gpu_yaw"] - numeric["cpu_yaw"] + 180) % 360 - 180)
            pitch = abs(numeric["gpu_pitch"] - numeric["cpu_pitch"])
            if abs(yaw - numeric["yaw_error"]) > 0.0001 or abs(pitch - numeric["pitch_error"]) > 0.0001:
                raise ValueError(f"recorded error mismatch: {phase} {key}")
            measured_pass = (yaw <= 0.05 and pitch <= 0.05 and numeric["shader_root_rigid_error"] <= 1e-5
                             and numeric["coverage_mismatch"] <= 2 and numeric["world_error_m"] <= 0.0001)
            if (row["status"] == "PASS") != measured_pass or row["status"] not in {"PASS", "FAIL"}:
                raise ValueError(f"status mismatch: {phase} {key}")
            negative = row["backend"] == "VrcDisabled" and row["method"] != "B_ChildProxyRoot"
            if not negative and not measured_pass:
                all_failures.append((phase, key, yaw, pitch))
            if row["backend"] != "VrcDisabled" and (numeric["proxy_position_error_m"] > 0.0001 or numeric["proxy_rotation_error_deg"] > 0.05):
                raise ValueError(f"positive proxy did not follow Head frame: {phase} {key}")
        setup_keys = set()
        for row in setup:
            key = (row["backend"], row["axes"])
            if key in setup_keys or row["phase"] != phase or key[0] not in expected_backends or key[1] not in {"0", "1"}:
                raise ValueError(f"duplicate/invalid setup: {phase} {key}")
            setup_keys.add(key)
            if row["conversion_issues"] != "False" or int(row["source_count"]) != 1:
                raise ValueError(f"invalid constraint configuration: {phase} {key}")
            if row["backend"] == "Unity":
                if row["unity_constraint_remaining"] != "1" or row["vrc_constraint_count"] != "0":
                    raise ValueError(f"invalid Unity control: {phase} {key}")
            elif row["unity_constraint_remaining"] != "0" or row["vrc_constraint_count"] != "1":
                raise ValueError(f"invalid VRC substitution: {phase} {key}")
            active = "False" if row["backend"] == "VrcDisabled" else "True"
            if row["is_active"] != active or row["locked"] != "True" or row["solve_local"] != "False" or float(row["global_weight"]) != 1:
                raise ValueError(f"invalid active/lock/solve/weight: {phase} {key}")
        for axes in ("0", "1"):
            for method in ("B_ConstraintProxyRoot", "C_RebasedConstraint"):
                control = [r for r in rows if r["backend"] == "VrcDisabled" and r["axes"] == axes and r["method"] == method and r["pose"] == "HeadYaw"]
                if not any(r["status"] == "FAIL" for r in control):
                    raise ValueError(f"disabled constraint did not fail: {phase}/{axes}/{method}")
        print(f"{phase}: {len(rows)} measured cases, {dict(Counter(r['status'] for r in rows))}")
    if all_failures:
        raise ValueError(f"{len(all_failures)} positive candidate failures; first={all_failures[0]}")
    print("PASS: direct/converted VRC constraints, child proxy control, geometry, negative controls")
    print("VRChat client / standalone Player / Stereo / Mirror / Head Chop: NOT RUN")


def main() -> int:
    """既存測定結果を検査する。Unity実行を省略して成功扱いにはしない。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    check(args.directory)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
