#!/usr/bin/env python3
"""実アバターFBXの監査とHead Frame測定結果を再検査する（Unity実測の代替ではない）。"""

from __future__ import annotations

import argparse
import csv
import json
import math
from collections import Counter
from pathlib import Path


AVATARS = {"Shinano", "Komano", "Yumeka"}
METHODS = {"Baseline", "A_HeadRoot", "B_ChildProxyRoot", "B_VrcProxyRoot", "C_RebasedChild", "RendererOnly"}
POSES = {"Rest", "HeadYaw", "HeadPitchRoll", "BodyYaw", "NeckHead", "BlendShape", "HalfScale", "DoubleScale"}
VIEWS = {(-90.0, 0.0), (-45.0, 0.0), (0.0, 0.0), (45.0, 0.0), (90.0, 0.0), (45.0, 30.0), (-45.0, -30.0)}
CONTROLS = {"Baseline", "RendererOnly"}


def read_json(path: Path):
    """Unity出力のBOMも受け入れてJSONを読む。"""
    return json.loads(path.read_text(encoding="utf-8-sig"))


def check(directory: Path) -> None:
    """全条件、参照値との誤差、元構造、負対照、SDK版を検査する。"""
    inputs = read_json(directory / "inputs.json")["models"]
    if {model["name"] for model in inputs} != AVATARS or len(inputs) != 3:
        raise ValueError("expected three specified real avatars")
    for model in inputs:
        if model.get("sourcePath") or model["sourceMetaCopied"] or len(model["sha256"]) != 64 or model["bytes"] <= 0:
            raise ValueError("invalid/redaction failure in FBX inputs")
    sdk = read_json(directory / "sdk-inputs.json")
    if sdk["sdk_versions"] != {"com.vrchat.base": "3.10.5", "com.vrchat.avatars": "3.10.5"}:
        raise ValueError("unexpected SDK versions")
    errors = []
    surface_failures = []
    for phase in ("EditMode", "PlayMode"):
        with (directory / f"{phase}-angles.csv").open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        setup = {s["avatar"]: s for s in read_json(directory / f"{phase}-setup.json")}
        audits = {a["name"]: a for a in read_json(directory / f"{phase}-audit.json")}
        if len(rows) != 1008 or set(setup) != AVATARS or set(audits) != AVATARS:
            raise ValueError(f"incomplete {phase}: {len(rows)} cases")
        for avatar in AVATARS:
            a, s = audits[avatar], setup[avatar]
            if a["headResolution"] != "Humanoid" or not a["avatarValid"] or not a["humanoid"] or s["HeadResolution"] != "Humanoid":
                raise ValueError(f"Humanoid head was not resolved: {phase}/{avatar}")
            if len(a["meshes"]) != a["rendererCount"] or sum(m["vertexCount"] for m in a["meshes"]) != a["totalVertices"]:
                raise ValueError(f"inconsistent source renderer audit: {phase}/{avatar}")
            selected = [m for m in a["meshes"] if m["renderer"] == s["selectedRenderer"]]
            if len(selected) != 1:
                raise ValueError(f"source selection missing/ambiguous: {phase}/{avatar}")
            m = selected[0]
            if m["vertexCount"] != s["vertexCount"] or len(m["materials"]) != s["materialSlots"] or m["blendShapes"][s["BlinkIndex"]] != s["BlinkName"]:
                raise ValueError(f"source setup mismatch: {phase}/{avatar}")
            if m["weights"]["headBranchAffected"] <= 0 or m["boneCount"] != m["bindPoseCount"]:
                raise ValueError(f"invalid face skin metadata: {phase}/{avatar}")
        keys = set()
        for row in rows:
            key = (row["avatar"], row["pose"], row["method"], float(row["world_yaw"]), float(row["world_pitch"]))
            if key in keys or row["phase"] != phase or key[0] not in AVATARS or key[1] not in POSES or key[2] not in METHODS or key[3:] not in VIEWS:
                raise ValueError(f"duplicate/invalid {phase} case: {key}")
            keys.add(key)
            if row["status"] == "NOT RUN":
                if key[2] != "C_RebasedChild" or setup[key[0]]["rebase"] == "rigid converter accepted":
                    raise ValueError(f"unjustified NOT RUN: {phase}/{key}")
                continue
            fields = ("cpu_yaw", "cpu_pitch", "gpu_yaw", "gpu_pitch", "raw_yaw", "raw_pitch", "yaw_error", "pitch_error",
                      "proxy_position_error_m", "proxy_rotation_error_deg", "shader_root_rigid_error", "coverage_mismatch", "world_error_m", "covered_pixels")
            values = {field: float(row[field]) for field in fields}
            if not all(math.isfinite(value) for value in values.values()) or any(values[field] < 0 for field in fields[6:]) or values["covered_pixels"] <= 0:
                raise ValueError(f"invalid measurement: {phase}/{key}")
            yaw = abs((values["gpu_yaw"] - values["cpu_yaw"] + 180) % 360 - 180)
            pitch = abs(values["gpu_pitch"] - values["cpu_pitch"])
            if abs(yaw - values["yaw_error"]) > 0.0001 or abs(pitch - values["pitch_error"]) > 0.0001:
                raise ValueError(f"recorded error mismatch: {phase}/{key}")
            passed = yaw <= 0.05 and pitch <= 0.05 and values["shader_root_rigid_error"] <= 1e-5 and values["coverage_mismatch"] <= 2 and values["world_error_m"] <= 0.0001
            if row["status"] not in {"PASS", "FAIL"} or (row["status"] == "PASS") != passed:
                raise ValueError(f"status mismatch: {phase}/{key}")
            if key[2] not in CONTROLS:
                if yaw > 0.05 or pitch > 0.05 or values["shader_root_rigid_error"] > 1e-5:
                    errors.append((phase, key, yaw, pitch))
                if values["world_error_m"] > 0.0001 or values["coverage_mismatch"] > 2:
                    surface_failures.append((phase, key, values["world_error_m"], values["coverage_mismatch"]))
            if values["proxy_position_error_m"] > 0.0001 or values["proxy_rotation_error_deg"] > 0.05:
                raise ValueError(f"VRC proxy did not follow: {phase}/{key}")
        for avatar in AVATARS:
            for method in CONTROLS:
                control = [r for r in rows if r["avatar"] == avatar and r["method"] == method and r["pose"] == "HeadYaw"]
                if not any(r["status"] == "FAIL" for r in control):
                    raise ValueError(f"negative control did not fail: {phase}/{avatar}/{method}")
        print(f"{phase}: {len(rows)} cases, {dict(Counter(r['status'] for r in rows))}")
        with (directory / f"{phase}-vertices.csv").open(encoding="utf-8-sig", newline="") as stream:
            vertices = list(csv.DictReader(stream))
        vertex_keys = set()
        if len(vertices) != 144:
            raise ValueError(f"incomplete {phase} vertex probe")
        for row in vertices:
            key = (row["avatar"], row["pose"], row["method"])
            if key in vertex_keys or row["phase"] != phase or key[0] not in AVATARS or key[1] not in POSES or key[2] not in METHODS:
                raise ValueError(f"duplicate/invalid vertex probe: {phase}/{key}")
            vertex_keys.add(key)
            error = float(row["vertex_world_error_m"])
            if int(row["vertex_count"]) != setup[key[0]]["vertexCount"] or not math.isfinite(error) or error < 0:
                raise ValueError(f"invalid vertex count/position: {phase}/{key}")
            if row["status"] not in {"PASS", "FAIL"} or (row["status"] == "PASS") != (error <= 0.0001):
                raise ValueError(f"vertex status mismatch: {phase}/{key}")
            if error > 0.0001:
                errors.append((phase, key, "GPU vertex position", error))
        print(f"{phase}: {len(vertices)} all-vertex GPU probes, {dict(Counter(r['status'] for r in vertices))}")
    if errors:
        raise ValueError(f"{len(errors)} positive candidate failures, first={errors[0]}")
    print("PASS: Head frame angles/matrices, all-vertex GPU positions, VRC proxy, negative controls")
    print(f"Strict triangle surface-image comparison: {len(surface_failures)} positive-candidate FAIL retained (0.1 mm / 2 pixel thresholds unchanged)")
    print("Original product Prefabs / PhysBone / VRChat client / Head Chop / Stereo / Mirror: NOT RUN")


def main() -> int:
    """指定フォルダに保存された実測のみを検査する。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    check(args.directory)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
