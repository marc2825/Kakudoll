#!/usr/bin/env python3
"""Unityライセンス不要の回帰検証を一括実行する。

CIの `license-free` ジョブとローカル実行の共通入口。Unity EditMode/PlayMode、
VRChatクライアント、HMDの代替ではない。各checkerは保存済み結果の再検査であり、
GPU sweepや実機試験を再実行しない。

公開版（docs/PUBLIC_EXPORT_LEDGER.json がある）では、除外台帳に記録された非配布fileを
FAILでもPASSでもなく NOT RUN（非配布）として数え、公開版の検査（check_public_export.py）も実行する。

使い方:
    python3 scripts/run_license_free_checks.py [--skip-dotnet] [--log PATH]

終了コードは、1件でも失敗すれば1。全件の結果を最後に一覧表示する。
ログでは個人の絶対パスとリポジトリの場所を伏字にする（結果の判定には影響しない）。
"""
from __future__ import annotations

import argparse
import hashlib
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import vlat_release  # noqa: E402

# (表示名, 実行コマンド, 入力として必須のpath, 存在すれば検査に使うpath)。保存済み証拠の再検査であり、元データは変更しない。
PYTHON_CHECKS = [
    ("schema v0.1", [sys.executable, "scripts/validate_schema.py"], None, ()),
    ("schema v0.2 keyform", [sys.executable, "scripts/validate_keyform_schema.py"], None, ()),
    ("client evidence tools", [sys.executable, "-m", "unittest", "discover",
                               "-s", "experiments/VRCClientValidation", "-p", "test_*.py"], None, ()),
    ("S1 head frame results", [sys.executable, "scripts/check_head_frame_results.py",
                               "experiments/VRCHeadFrame/results/2026-10-16-unity2022.3.22f1"],
     "experiments/VRCHeadFrame/results/2026-10-16-unity2022.3.22f1", ()),
    ("S1 SDK results", [sys.executable, "scripts/check_head_frame_sdk_results.py",
                        "experiments/VRCHeadFrameSdk/results/2026-10-16-sdk3.10.5"],
     "experiments/VRCHeadFrameSdk/results/2026-10-16-sdk3.10.5", ()),
    ("G0 real avatar results", [sys.executable, "scripts/check_head_frame_avatar_results.py",
                                "experiments/VRCRealAvatars/results/2026-10-16-unity2022.3.22f1"],
     "experiments/VRCRealAvatars/results/2026-10-16-unity2022.3.22f1", ()),
    ("S2 view context results", [sys.executable, "scripts/check_view_context_results.py",
                                 "experiments/VRCViewContext/results/2026-10-17-unity2022.3.22f1"],
     "experiments/VRCViewContext/results/2026-10-17-unity2022.3.22f1",
     # checkerはこのfileがあるときだけS1失敗記録の保持を検査する。非配布なら別行でNOT RUNと示す。
     ("experiments/VRCViewContext/results/2026-10-17-unity2022.3.22f1/s1-carryover.json",)),
    ("S1-R1 image difference results", [sys.executable, "scripts/check_image_difference_results.py",
                                        "experiments/VRCImageDifference/results/2026-10-17-unity2022.3.22f1"],
     "experiments/VRCImageDifference/results/2026-10-17-unity2022.3.22f1", ()),
    ("client ledger NOT RUN integrity", [sys.executable, "experiments/VRCClientValidation/check_client_evidence.py",
                                         "experiments/VRCClientValidation/results/2026-10-17-unity2022.3.22f1/client-results.json"],
     "experiments/VRCClientValidation/results/2026-10-17-unity2022.3.22f1/client-results.json", ()),
    # 公開版は開発時の試験証拠を配布しないため、証拠ディレクトリが非配布ならNOT RUNになる。
    ("evidence policy", [sys.executable, "scripts/curate_evidence.py", "check"], "docs/product/evidence", ()),
]

DOTNET_CHECKS = [
    ("Core NUnit (.NET)", ["dotnet", "test", "tests/CoreHarness/Core.Tests.csproj",
                           "--logger", "trx;LogFileName=core.trx", "--results-directory", "artifacts/core"]),
    ("Core netstandard2.1 build", ["dotnet", "build", "tests/CoreHarness/Compatibility/Core.Compatibility.csproj",
                                   "-warnaserror"]),
]


def redact(text: str) -> str:
    """ログ用。リポジトリの場所と個人homeを伏字にする。"""
    text = text.replace(str(ROOT), "<repo>")
    return vlat_release.redact_personal_paths(text)[0]


def verify_experiment_hashes(ledger: dict | None) -> tuple[bool, int, int, str]:
    """各実験の `results/*/SHA256SUMS` を実験ルート基準で照合する。

    返り値: (失敗なし, 照合したentry数, 非配布entry数, 報告)。非配布は台帳と同じSHA-256のときだけ認める。
    """
    excluded = vlat_release.excluded_entries(ledger)
    lines = []
    ok = True
    verified = 0
    private = 0
    for sums in sorted(ROOT.glob("experiments/*/results/*/SHA256SUMS")):
        experiment_root = sums.parents[2]
        entries = 0
        withheld = 0
        bad = []
        for raw in sums.read_text(encoding="utf-8").splitlines():
            if not raw.strip():
                continue
            digest, _, rel = raw.partition("  ")
            digest = digest.strip()
            target = experiment_root / rel
            entries += 1
            if not target.is_file():
                key = target.relative_to(ROOT).as_posix()
                if key in excluded and excluded[key]["sha256"] == digest:
                    withheld += 1
                    continue
                bad.append(f"missing {rel}" + (" (ledger hash differs)" if key in excluded else ""))
                continue
            if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
                bad.append(f"mismatch {rel}")
        verified += entries - withheld
        private += withheld
        status = "PASS" if not bad else "FAIL"
        ok &= not bad
        note = f", {withheld} not distributed" if withheld else ""
        lines.append(f"{sums.relative_to(ROOT)}: {status} ({entries - withheld} verified{note})")
        lines.extend(f"  {b}" for b in bad[:20])
    lines.append(f"total entries verified: {verified}; not distributed: {private}")
    return ok, verified, private, "\n".join(lines)


def run(name: str, cmd: list[str], log) -> bool:
    log.write(f"\n$ {' '.join(cmd)}\n")
    log.flush()
    proc = subprocess.run(cmd, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    output = redact(proc.stdout)
    log.write(output)
    log.write(f"exit={proc.returncode}\n")
    log.flush()
    tail = output.strip().splitlines()[-1:] or [""]
    print(f"[{'PASS' if proc.returncode == 0 else 'FAIL'}] {name}: {tail[0][:160]}")
    return proc.returncode == 0


def not_run(name: str, reason: str, log, results: list[tuple[str, str]]) -> None:
    log.write(f"\n# {name}: NOT RUN ({reason})\n")
    print(f"[NOT RUN] {name}: {reason}")
    results.append((name, "NOT RUN"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--skip-dotnet", action="store_true", help=".NET SDKがない環境でCore試験を省略する（省略はPASS扱いしない）")
    parser.add_argument("--log", type=Path, default=ROOT / "artifacts" / "license-free-checks.log")
    args = parser.parse_args()
    args.log.parent.mkdir(parents=True, exist_ok=True)
    ledger = vlat_release.load_ledger(ROOT)
    results: list[tuple[str, str]] = []
    with args.log.open("w", encoding="utf-8") as log:
        if ledger:
            log.write(f"# public export (ledger: {len(ledger.get('excluded', []))} files not distributed)\n")
            print(f"public export: {len(ledger.get('excluded', []))} files are recorded as not distributed")
            results.append(("public export privacy/rights scan",
                            "PASS" if run("public export privacy/rights scan",
                                          [sys.executable, "-B", "scripts/check_public_export.py", "."], log) else "FAIL"))
        for name, cmd, required, optional in PYTHON_CHECKS:
            if required and not (ROOT / required).exists():
                withheld = vlat_release.excluded_under(ledger, required)
                if withheld:
                    not_run(name, f"input not distributed ({len(withheld)} files, reason: {withheld[0]['reason']})", log, results)
                else:
                    print(f"[FAIL] {name}: input missing: {required}")
                    results.append((name, "FAIL"))
                continue
            results.append((name, "PASS" if run(name, cmd, log) else "FAIL"))
            for path in optional:
                if not (ROOT / path).exists():
                    withheld = vlat_release.excluded_under(ledger, path)
                    if withheld:
                        not_run(f"{name}: {Path(path).name} sub-check", f"not distributed (reason: {withheld[0]['reason']})", log, results)
                    else:
                        print(f"[FAIL] {name}: optional input missing and not in ledger: {path}")
                        results.append((f"{name}: {Path(path).name} sub-check", "FAIL"))
        ok, verified, private, report = verify_experiment_hashes(ledger)
        log.write("\n$ verify experiment SHA256SUMS\n" + report + "\n")
        print(f"[{'PASS' if ok else 'FAIL'}] experiment SHA256SUMS: {verified} entries verified")
        results.append(("experiment SHA256SUMS" + (" (distributed files)" if ledger else ""), "PASS" if ok else "FAIL"))
        if private:
            not_run("experiment SHA256SUMS (not distributed)", f"{private} entries recorded in the ledger", log, results)
        if args.skip_dotnet:
            for name, _ in DOTNET_CHECKS:
                results.append((name, "NOT RUN"))
        elif shutil.which("dotnet") is None:
            print("[FAIL] dotnet not found; use --skip-dotnet to record NOT RUN explicitly")
            for name, _ in DOTNET_CHECKS:
                results.append((name, "FAIL"))
        else:
            for name, cmd in DOTNET_CHECKS:
                results.append((name, "PASS" if run(name, cmd, log) else "FAIL"))
    print("\nSummary:")
    for name, status in results:
        print(f"  {status:8} {name}")
    print(f"log: {redact(str(args.log))}")
    return 0 if all(s != "FAIL" for _, s in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
