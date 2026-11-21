#!/usr/bin/env python3
"""公開スナップショット（またはその1commitリポジトリ）を検査する。Private側の研究treeには使わない。

  python3 scripts/check_public_export.py <export-dir> [--git-history [--allow-email ADDRESS ...]]

読み取り専用。Gitleaks・権利確認・人による確認の代わりではない。1件でも問題があれば終了code 1。
値そのもの（パスやトークン）は表示せず、file・種類・件数だけを出す。

検査:
  1. 個人の絶対パス、認証情報らしき文字列、個人メール、Windows hostname、内部IPv4
  2. 公開しない種類のfile（モデル・PSD・鍵・Unityライセンス等）
  3. 商用アバター由来データの置き場所（docs/PUBLIC_EXPORT_LEDGER.json で除外済みであること）
  4. 商用アバター名を含むfileが、確認済みの文書に限られていること
  5. 所有者の判断で公開しない第三者画像（Seed-sanの撮影画像）が含まれていないこと
  6. Markdownの相対リンク切れ（SHA256SUMSで凍結された実験fileや証拠READMEから非配布fileへのリンクは、書き換えられないため警告）
  7. --git-history: 全refの全blobと、commitの作者/コミッターのメール（--allow-email で明示したものは許可）
"""
from __future__ import annotations

import argparse
import posixpath
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vlat_release  # noqa: E402

TEXT_SUFFIXES = {
    ".md", ".txt", ".log", ".json", ".jsonl", ".yaml", ".yml", ".xml", ".trx", ".csv", ".py", ".cs",
    ".shader", ".cginc", ".hlsl", ".uss", ".uxml", ".ps1", ".sh", ".toml", ".asmdef", ".asset", ".meta",
    ".unity", ".prefab", ".mat", ".anim", ".controller", ".csproj", ".gitignore", ".gitattributes", ".cfg",
}
TEXT_NAMES = {"SHA256SUMS", "SOURCE_SHA256SUMS", "ARCHIVED.sha256", "LICENSE", ".gitignore", ".gitattributes"}
DANGEROUS_SUFFIXES = {
    ".fbx", ".vrm", ".glb", ".gltf", ".blend", ".psd", ".unitypackage", ".zip", ".7z",
    ".p12", ".pfx", ".pem", ".key", ".keystore", ".ulf", ".env",
}
# 商用アバター由来の生データ置き場。公開版では除外台帳に記録され、実fileは存在しないこと。
RESTRICTED_PATHS = [
    re.compile(r"^experiments/VRCRealAvatars/results/"),
    re.compile(r"^experiments/VRCImageDifference/results/"),
    re.compile(r"^experiments/VRCClientValidation/results/[^/]+/regression-s1-r1/"),
    re.compile(r"^experiments/VRCViewContext/results/[^/]+/s1-carryover\.json$"),
    re.compile(r"^docs/experiments/VRC_G0_REAL_AVATARS\.md$"),
]
MODEL_NAMES = re.compile(r"(?i)shinano|komano|yumeka|しなの|狛乃|ゆめか")
# 商用アバター名を含めてよい（測定事実・集計値・出典だけを含むと確認した）file。
# 新しいfileで名前を使う場合は、モデル固有の詳細（構造・fingerprint・形状）がないことを確認してから追加する。
MODEL_NAME_REVIEWED = {
    "README.md",
    "THIRD_PARTY_NOTICES.md",
    "docs/PUBLIC_DISTRIBUTION.md",
    "docs/PUBLIC_EXPORT_LEDGER.json",
    "docs/experiments/REAL_AVATAR_VALIDATION_SUMMARY.md",
    "docs/experiments/VRC_P0_S1_HEAD_FRAME.md",
    "docs/experiments/VRC_P0_S1_IMAGE_DIFFERENCE_INVESTIGATION.md",
    "docs/experiments/VRC_P0_S2_VIEW_CONTEXT.md",
    "experiments/VRCRealAvatars/README.md",
    "experiments/VRCImageDifference/README.md",
    "scripts/check_head_frame_avatar_results.py",
    "scripts/check_public_export.py",
    "release/public-export.json",
}
# Seed-sanを撮影した試験画像。権利上は帰属表示で公開可能だが、所有者の判断（2026-11-21）で公開版に含めない。
SEED_SAN_EVIDENCE = re.compile(r"^docs/product/evidence/2026-10-25-editor-1e/(?:1e-avatar-|CREDITS\.md$)")
FROZEN_EVIDENCE = re.compile(r"^(?:docs/product/evidence/[^/]+/|experiments/[^/]+/results/)")
MD_LINK = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)")
# リンク検査の前に、fenced code block とインラインコード（数式 `p[j](v)` 等）を除く。
CODE_SPANS = re.compile(r"```.*?```|`[^`\n]*`", re.S)
IGNORE_DIRS = {".git"}


def is_text(path: str) -> bool:
    name = path.rsplit("/", 1)[-1]
    return name in TEXT_NAMES or ("." + name.rsplit(".", 1)[-1].lower() if "." in name else "") in TEXT_SUFFIXES


def scan_content(label: str, data: bytes, text_file: bool) -> list[str]:
    text = data.decode("utf-8", errors="replace") if text_file else data.decode("latin-1")
    found = list(vlat_release.find_personal_paths(text))
    if text_file:
        found += vlat_release.find_other_private_data(text)
    else:
        found += [(k, n) for k, n in vlat_release.find_other_private_data(text) if k in {
            "github-token", "aws-access-key", "private-key-block", "openai-like-token", "slack-token", "unity-license-file"}]
    return [f"{label}: {kind} ({count} match(es))" for kind, count in found]


def list_files(root: Path) -> list[Path]:
    """gitリポジトリなら追跡fileと未追跡（ignore対象外）のfile、そうでなければ全fileを返す。"""
    if (root / ".git").exists():
        listing = subprocess.run(["git", "-C", str(root), "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
                                 check=True, stdout=subprocess.PIPE).stdout.decode("utf-8")
        return sorted(root / rel for rel in dict.fromkeys(listing.split("\0")) if rel and (root / rel).is_file())
    return sorted(p for p in root.rglob("*") if p.is_file() and not any(part in IGNORE_DIRS for part in p.relative_to(root).parts))


def frozen_files(root: Path) -> set[str]:
    """実験の results/*/SHA256SUMS が凍結しているfile（実験root基準のpathをrepo相対に直したもの）。"""
    frozen: set[str] = set()
    for sums in root.glob("experiments/*/results/*/SHA256SUMS"):
        experiment = sums.parents[2].relative_to(root).as_posix()
        for line in sums.read_text(encoding="utf-8").splitlines():
            if line.strip():
                frozen.add(posixpath.normpath(posixpath.join(experiment, line.split("  ", 1)[1].strip())))
    return frozen


def check_tree(root: Path) -> tuple[list[str], list[str]]:
    failures: list[str] = []
    warnings: list[str] = []
    ledger = vlat_release.load_ledger(root)
    excluded = vlat_release.excluded_entries(ledger)
    if ledger is None:
        failures.append(f"{vlat_release.LEDGER_RELATIVE_PATH}: 除外台帳がない（build_public_export.py で作った公開版ではない）")
    files = list_files(root)
    present = {p.relative_to(root).as_posix() for p in files}
    frozen = frozen_files(root)
    for path in files:
        rel = path.relative_to(root).as_posix()
        data = path.read_bytes()
        failures += scan_content(rel, data, is_text(rel))
        suffix = path.suffix.lower()
        if suffix in DANGEROUS_SUFFIXES or path.name.startswith(".env"):
            failures.append(f"{rel}: 公開しない種類のfile（{suffix or path.name}）")
        if suffix == ".pyc" or "__pycache__" in path.parts:
            failures.append(f"{rel}: 生成物（Python bytecode）")
        if any(r.search(rel) for r in RESTRICTED_PATHS):
            failures.append(f"{rel}: 商用アバター由来データの置き場にfileがある（除外台帳で除外すること）")
        if is_text(rel) and rel not in MODEL_NAME_REVIEWED and MODEL_NAMES.search(data.decode("utf-8", errors="replace")):
            failures.append(f"{rel}: 商用アバター名を含む（内容を確認してから MODEL_NAME_REVIEWED に追加する）")
        if rel.endswith(".md"):
            prose = CODE_SPANS.sub("", data.decode("utf-8", errors="replace"))
            for target in MD_LINK.findall(prose):
                if re.match(r"^[a-z][a-z0-9+.-]*:", target, re.I) or target.startswith("#"):
                    continue
                resolved = posixpath.normpath(posixpath.join(posixpath.dirname(rel), target.split("#", 1)[0]))
                if resolved in present or any(p.startswith(resolved + "/") for p in present):
                    continue
                if resolved in excluded or any(p.startswith(resolved + "/") for p in excluded):
                    message = f"{rel}: 非配布fileへのリンク {target}"
                    (warnings if FROZEN_EVIDENCE.match(rel) or rel in frozen else failures).append(message)
                else:
                    failures.append(f"{rel}: リンク切れ {target}")
    for rel in excluded:
        if rel in present:
            failures.append(f"{rel}: 除外台帳にあるfileが公開版に存在する")
        if not re.fullmatch(r"[0-9a-f]{64}", excluded[rel].get("sha256", "")):
            failures.append(f"{vlat_release.LEDGER_RELATIVE_PATH}: {rel} のSHA-256形式が不正")
    for rel in sorted(p for p in present if SEED_SAN_EVIDENCE.match(p)):
        failures.append(f"{rel}: Seed-sanの試験画像・データは公開版に含めない（所有者の判断）")
    return failures, warnings


def check_history(root: Path, allowed_emails: set[str]) -> list[str]:
    """公開リポジトリの全ref・全blob・全commitの作者情報を検査する。allowed_emails は所有者が公開を認めた作者メール。"""
    failures: list[str] = []
    def git(*args: str) -> str:
        return subprocess.run(["git", "-C", str(root), *args], check=True, stdout=subprocess.PIPE, text=True).stdout
    refs = git("for-each-ref", "--format=%(refname)").split()
    commits = git("rev-list", "--all").split()
    if not commits:
        return ["git history: commitがない"]
    for line in git("log", "--all", "--format=%H%x09%ae%x09%ce").splitlines():
        sha, author, committer = line.split("\t")
        for role, email in (("author", author), ("committer", committer)):
            if email.lower() not in allowed_emails and vlat_release.find_other_private_data(email):
                failures.append(f"commit {sha[:12]}: {role} email が許可されていない個人アドレス（--allow-email で明示するか noreply を使う）")
    objects = git("rev-list", "--objects", "--all").splitlines()
    names = {}
    for line in objects:
        sha, _, name = line.partition(" ")
        if name:
            names.setdefault(sha, name)
    proc = subprocess.Popen(["git", "-C", str(root), "cat-file", "--batch"], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    assert proc.stdin and proc.stdout
    for sha, name in names.items():
        proc.stdin.write(sha.encode() + b"\n")
        proc.stdin.flush()
        header = proc.stdout.readline().split()
        size = int(header[2])
        data = proc.stdout.read(size)
        proc.stdout.read(1)
        if header[1] != b"blob":
            continue
        failures += [f"history blob {sha[:12]} ({name}): {m.split(': ', 1)[1]}" for m in scan_content(name, data, is_text(name))]
        if any(r.search(name) for r in RESTRICTED_PATHS):
            failures.append(f"history blob {sha[:12]} ({name}): 商用アバター由来データの置き場")
    proc.stdin.close()
    proc.wait()
    print(f"git history: {len(refs)} refs, {len(commits)} commits, {len(names)} paths/objects scanned")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("export", type=Path, help="公開スナップショットのディレクトリ（公開版リポジトリのroot）")
    parser.add_argument("--git-history", action="store_true", help="公開版リポジトリの全履歴も検査する")
    parser.add_argument("--allow-email", action="append", default=[],
                        help="commitの作者/コミッターとして公開してよいメールアドレス（file内容の検査には影響しない）")
    args = parser.parse_args()
    root = args.export.resolve()
    if not root.is_dir():
        parser.error(f"not a directory: {root}")
    failures, warnings = check_tree(root)
    if args.git_history:
        failures += check_history(root, {e.lower() for e in args.allow_email})
    for line in warnings:
        print("WARN:", line)
    for line in failures:
        print("FAIL:", line)
    if failures:
        print(f"{len(failures)} findings, {len(warnings)} warnings; no files modified")
        return 1
    print(f"PASS: public export checks ({len(warnings)} warnings; manual rights review is still required)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
