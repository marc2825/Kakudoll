#!/usr/bin/env python3
"""Non-mutating VLat environment preflight.

Run from the repository root:
    python path/to/preflight_env.py

Optionally set UNITY_EXECUTABLE to the exact Unity binary.
"""
from __future__ import annotations

import os
import platform
import shutil
import subprocess
import sys
from importlib import metadata
from pathlib import Path

TARGET_UNITY = "2022.3.62f3"


def command_version(cmd: list[str]) -> str:
    try:
        p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                           text=True, timeout=10, check=False)
        return (p.stdout or "").strip().splitlines()[0] if p.stdout else f"exit={p.returncode}"
    except Exception as e:  # diagnostic script
        return f"unavailable ({e})"


def find_repo_root(start: Path) -> Path | None:
    p = start.resolve()
    for candidate in [p, *p.parents]:
        # 公開版にはAGENTS.mdがないため、package定義で判定する。
        if (candidate / "VLatProject/ProjectSettings/ProjectVersion.txt").is_file() and (candidate / "Packages/jp.vlat.core/package.json").is_file():
            return candidate
    return None


def unity_candidates() -> list[Path]:
    raw = os.environ.get("UNITY_EXECUTABLE")
    candidates: list[Path] = []
    if raw:
        candidates.append(Path(raw))
    candidates += [
        Path(f"C:/Program Files/Unity/Hub/Editor/{TARGET_UNITY}/Editor/Unity.exe"),
        Path(f"/mnt/c/Program Files/Unity/Hub/Editor/{TARGET_UNITY}/Editor/Unity.exe"),
        Path(f"/Applications/Unity/Hub/Editor/{TARGET_UNITY}/Unity.app/Contents/MacOS/Unity"),
        Path(f"/opt/unity/Editor/{TARGET_UNITY}/Editor/Unity"),
        Path("/opt/unity/Editor/Unity"),
    ]
    seen = set()
    out = []
    for p in candidates:
        key = str(p)
        if key not in seen:
            seen.add(key)
            out.append(p)
    return out


def main() -> int:
    repo = find_repo_root(Path.cwd())
    if repo is None:
        print("[FAIL] VLat repository root not found from current directory")
        return 2

    print(f"repo: {repo}")
    print(f"os: {platform.platform()}")
    print(f"arch: {platform.machine()}")
    print(f"python: {sys.version.split()[0]}")
    print(f"git: {command_version(['git', '--version']) if shutil.which('git') else 'unavailable'}")

    version_file = repo / "VLatProject/ProjectSettings/ProjectVersion.txt"
    version_text = version_file.read_text(encoding="utf-8", errors="replace")
    print(f"project version exact target present: {TARGET_UNITY in version_text}")

    found_unity = None
    for p in unity_candidates():
        if p.is_file():
            found_unity = p
            break
    print(f"unity executable: {found_unity if found_unity else 'NOT FOUND'}")

    lock = repo / "VLatProject/Packages/packages-lock.json"
    print(f"packages-lock.json: {'present' if lock.is_file() else 'absent'}")

    meta_count = sum(1 for _ in repo.rglob("*.meta"))
    print(f".meta files: {meta_count}")

    vrms = list((repo / "VLatProject/Assets").rglob("*.vrm")) if (repo / "VLatProject/Assets").exists() else []
    print(f"sample VRM files: {len(vrms)}")
    for p in vrms[:10]:
        print(f"  - {p.relative_to(repo)}")

    vlat_json = list((repo / "VLatProject/Assets").rglob("*.vlat.json")) if (repo / "VLatProject/Assets").exists() else []
    print(f"sample .vlat.json files: {len(vlat_json)}")
    for p in vlat_json[:10]:
        print(f"  - {p.relative_to(repo)}")

    try:
        print(f"jsonschema: {metadata.version('jsonschema')}")
    except metadata.PackageNotFoundError:
        print("jsonschema: unavailable")

    print("git status:")
    if (repo / ".git").exists() and shutil.which("git"):
        p = subprocess.run(["git", "status", "--short"], cwd=repo, text=True,
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
        print(p.stdout.rstrip() or "  clean")
    else:
        print("  Git metadata unavailable (for example, ZIP snapshot)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
