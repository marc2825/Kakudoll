#!/usr/bin/env python3
"""公開スナップショット（またはその公開リポジトリ）を検査する。Private側の研究treeには使わない。

  python3 scripts/check_public_export.py <export-dir> [--git-history [--allow-email ADDRESS[,ADDRESS...] ...]]

読み取り専用。Gitleaks・権利確認・人による確認の代わりではない。1件でも問題があれば終了code 1。
値そのもの（パスやトークン）は表示せず、file・種類・件数だけを出す。

検査（1〜5は現行treeと履歴で同じ判定 file_findings() を使う）:
  1. 個人の絶対パス、認証情報らしき文字列、個人メール、Windows hostname、内部IPv4
  2. 公開しない種類のfile（モデル・PSD・鍵・Unityライセンス・.env*・Python bytecode等）と、
     内容を監査できないもの（Git LFSポインタ・submodule・symlink）
  3. 商用アバター由来データの置き場と、docs/PUBLIC_EXPORT_LEDGER.json（除外台帳）で除外したpathにfileがないこと
  4. 商用アバター名を含むfileが、確認済みの文書に限られていること
  5. 所有者の判断で公開しない第三者画像（Seed-sanの撮影画像）が含まれていないこと
  6. Markdownの相対リンク切れ（現行treeだけ。SHA256SUMSで凍結された実験fileや証拠READMEから非配布fileへのリンクは、
     書き換えられないため警告）
  7. --git-history: 全ref（branch・tag・remote・notes・stash・replace等）から到達できる全commitの全 (path, blob) に1〜5を適用し、
     commit/tagの作者・コミッター・タガーのメールとメッセージも検査する（--allow-email で明示したメールは許可）
"""
from __future__ import annotations

import argparse
import collections
import os
import posixpath
import re
import subprocess
import sys
from collections.abc import Collection, Container
from pathlib import Path, PurePosixPath

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
# バイナリfileでも検出する認証情報の種類（メール・IPv4等はバイナリ中の偶然の一致が多いため除く）。
BINARY_SECRET_KINDS = {
    "github-token", "aws-access-key", "private-key-block", "openai-like-token", "slack-token", "unity-license-file"}
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

# Gitのtree entryのmode。通常file（100644/100755）以外は内容を監査できないか、公開版で使わない。
REGULAR_MODE = "100644"
SYMLINK_MODE = "120000"
GITLINK_MODE = "160000"
# Git LFSのポインタfile（1024 byte未満で、この行から始まる）。実体はLFS storageにあり、ここでは読めない。
LFS_POINTER = re.compile(rb"\Aversion https://(?:git-lfs|hawser)\.github\.com/spec/v1\n")
LFS_POINTER_MAX_BYTES = 1024
# git notes のblobはnote対象objectのSHA（fanoutの / を含む）をpathに持つ。内容は文章なのでtextとして検査する。
NOTES_PATH = re.compile(r"[0-9a-f]{40}|[0-9a-f]{64}")
# commit/tag objectの作者情報の行（mergetag内のtaggerは行頭が空白1つ）。
IDENTITY_LINE = re.compile(rb"^ ?(author|committer|tagger) [^\n<]*<([^>\n]*)>", re.M)


def is_text(path: str) -> bool:
    name = path.rsplit("/", 1)[-1]
    return name in TEXT_NAMES or ("." + name.rsplit(".", 1)[-1].lower() if "." in name else "") in TEXT_SUFFIXES


def content_findings(data: bytes, text_file: bool, allowed_emails: Collection[str] = ()) -> list[str]:
    """内容の検査結果を「種類 (件数)」で返す。値そのものは返さない。

    allowed_emails は commit/tag の作者情報・メッセージの検査だけで渡す（file内容には使わない）。
    """
    text = data.decode("utf-8", errors="replace") if text_file else data.decode("latin-1")
    found = list(vlat_release.find_personal_paths(text))
    if text_file:
        found += vlat_release.find_other_private_data(text, allowed_emails)
    else:
        found += [(k, n) for k, n in vlat_release.find_other_private_data(text) if k in BINARY_SECRET_KINDS]
    return [f"{kind} ({count} match(es))" for kind, count in found]


def scan_content(label: str, data: bytes, text_file: bool) -> list[str]:
    """content_findings() の結果にfile名を付ける（従来の呼び出し方との互換用）。"""
    return [f"{label}: {finding}" for finding in content_findings(data, text_file)]


def path_findings(rel: str, excluded: Container[str]) -> list[str]:
    """pathだけで決まる禁止判定（拡張子・.env*・bytecode・商用アバター由来データの置き場・除外台帳・Seed-san）。"""
    found = []
    path = PurePosixPath(rel)
    suffix = path.suffix.lower()
    if suffix in DANGEROUS_SUFFIXES or path.name.startswith(".env"):
        found.append(f"公開しない種類のfile（{suffix or path.name}）")
    if suffix == ".pyc" or "__pycache__" in path.parts:
        found.append("生成物（Python bytecode）")
    if any(r.search(rel) for r in RESTRICTED_PATHS):
        found.append("商用アバター由来データの置き場にfileがある（除外台帳で除外すること）")
    if rel in excluded:
        found.append("除外台帳で非配布としたfileがある")
    if SEED_SAN_EVIDENCE.match(rel):
        found.append("Seed-sanの試験画像・データは公開版に含めない（所有者の判断）")
    return found


def file_findings(rel: str, data: bytes | None, excluded: Container[str], mode: str = REGULAR_MODE,
                  text_file: bool | None = None) -> list[str]:
    """現行treeと履歴で共通の「公開してはいけないfile」の判定。

    rel はrepo相対のPOSIXパス。data はfileの内容（symlinkはリンク先の文字列。gitlinkは内容がないのでNone）。
    excluded は除外台帳のpath。text_file を省略すると拡張子から決める。
    返り値は所見の一覧で、file名以外の値（トークン・個人パス・メール等）は含まない。
    """
    found = path_findings(rel, excluded)
    if mode == GITLINK_MODE:
        found.append("submodule（gitlink）: 参照先は別リポジトリにあり、内容を監査できない")
        return found
    assert data is not None
    if text_file is None:
        text_file = is_text(rel)
    if mode == SYMLINK_MODE:
        found.append("symlink: 公開版では使わない（リンク先の実体は監査できない）")
        text_file = True
    elif len(data) < LFS_POINTER_MAX_BYTES and LFS_POINTER.match(data):
        found.append("Git LFSポインタ: 実体はLFS storageにあり、内容を監査できない")
    found += content_findings(data, text_file)
    if text_file and rel not in MODEL_NAME_REVIEWED and MODEL_NAMES.search(data.decode("utf-8", errors="replace")):
        found.append("商用アバター名を含む（内容を確認してから MODEL_NAME_REVIEWED に追加する）")
    return found


def metadata_findings(raw: bytes, allowed_emails: Collection[str], check_identities: bool = True) -> list[str]:
    """commit/tag objectの作者・コミッター・タガーのメールとメッセージを検査する。値そのものは返さない。

    check_identities=False は作者メールの許可リストが未設定のとき用（呼び出し側がNOT RUNと表示する）。
    """
    header, _, message = raw.partition(b"\n\n")
    found = []
    for role, email in (IDENTITY_LINE.findall(header) if check_identities else []):
        address = email.decode("utf-8", errors="replace")
        if address.lower() not in allowed_emails and vlat_release.find_other_private_data(address):
            found.append(f"{role.decode()} email が許可されていない個人アドレス（--allow-email で明示するか noreply を使う）")
    found += [f"message: {finding}" for finding in content_findings(message, True, allowed_emails)]
    return found


def _is_file_or_link(path: Path) -> bool:
    return path.is_symlink() or path.is_file()


def list_files(root: Path) -> list[Path]:
    """gitリポジトリなら追跡fileと未追跡（ignore対象外）のfile、そうでなければ全fileを返す。symlinkも含める。"""
    if (root / ".git").exists():
        listing = subprocess.run(["git", "-C", str(root), "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
                                 check=True, stdout=subprocess.PIPE).stdout.decode("utf-8")
        return sorted(root / rel for rel in dict.fromkeys(listing.split("\0")) if rel and _is_file_or_link(root / rel))
    return sorted(p for p in root.rglob("*") if _is_file_or_link(p) and not any(part in IGNORE_DIRS for part in p.relative_to(root).parts))


def list_gitlinks(root: Path) -> list[str]:
    """indexにあるsubmodule（gitlink）のpath。gitリポジトリでなければ空。"""
    if not (root / ".git").exists():
        return []
    listing = subprocess.run(["git", "-C", str(root), "ls-files", "-z", "--stage"],
                             check=True, stdout=subprocess.PIPE).stdout.decode("utf-8")
    return sorted({entry.split("\t", 1)[1] for entry in listing.split("\0") if entry.startswith(GITLINK_MODE + " ")})


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
        link = path.is_symlink()
        # symlinkはGitにリンク先の文字列として記録されるため、履歴と同じくその文字列を検査する。
        data = os.fsencode(os.readlink(path)) if link else path.read_bytes()
        failures += [f"{rel}: {finding}" for finding in file_findings(rel, data, excluded, SYMLINK_MODE if link else REGULAR_MODE)]
        if rel.endswith(".md") and not link:
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
    for rel in list_gitlinks(root):
        failures += [f"{rel}: {finding}" for finding in file_findings(rel, None, excluded, GITLINK_MODE)]
    for rel in excluded:
        if not re.fullmatch(r"[0-9a-f]{64}", excluded[rel].get("sha256", "")):
            failures.append(f"{vlat_release.LEDGER_RELATIVE_PATH}: {rel} のSHA-256形式が不正")
    return failures, warnings


class _ObjectReader:
    """`git cat-file --batch` で object を1つずつ読む（1 processを使い回す）。"""

    def __init__(self, git_command: list[str]):
        self._proc = subprocess.Popen([*git_command, "cat-file", "--batch"], stdin=subprocess.PIPE, stdout=subprocess.PIPE)

    def read(self, sha: str) -> tuple[str, bytes] | None:
        assert self._proc.stdin and self._proc.stdout
        self._proc.stdin.write(sha.encode() + b"\n")
        self._proc.stdin.flush()
        header = self._proc.stdout.readline().split()
        if len(header) != 3:  # "<sha> missing" など
            return None
        data = self._proc.stdout.read(int(header[2]))
        self._proc.stdout.read(1)
        return header[1].decode(), data

    def close(self) -> None:
        assert self._proc.stdin and self._proc.stdout
        self._proc.stdin.close()
        self._proc.wait()
        self._proc.stdout.close()


def check_history(root: Path, allowed_emails: Collection[str], check_identities: bool = True) -> list[str]:
    """公開リポジトリの全refから到達できる履歴を、現行treeと同じ判定（file_findings）で検査する。

    - 全ref（refs/heads・tags・remotes・notes・stash・replace等）とHEADから到達できる全commitについて、
      treeの全 (path, blob) 組を検査する。後で削除・リネームされたfileや、同じblobが複数のpathにある場合も、
      pathごとに判定する。tagが直接指すtree/blobのように、commitのtree外にあるblobも内容を検査する。
    - refs/replace による置き換えは無効にして（--no-replace-objects）元のobjectを読む。置き換え先も別refとして検査する。
    - commitの作者・コミッター、annotated tagのタガーのメールと、commit/tagのメッセージも検査する。
      allowed_emails（小文字）は所有者が公開を認めたメールで、file内容の検査には使わない。
    - どのrefからも到達できないobject（reflogだけにあるcommit、dangling object）は公開されないため対象外。
    - 除外台帳は現行treeのものを使う。
    """
    failures: list[str] = []
    allowed = {email.lower() for email in allowed_emails}
    base = ["git", "--no-replace-objects", "-C", str(root)]

    def git(*args: str, stdin: bytes | None = None) -> str:
        return subprocess.run([*base, *args], check=True, input=stdin,
                              stdout=subprocess.PIPE).stdout.decode("utf-8", errors="replace")

    if git("rev-parse", "--is-shallow-repository").strip() == "true":
        return ["git history: shallow cloneでは過去のcommitを検査できない（fetch-depth: 0 等で全履歴を取得する）"]
    partial = subprocess.run([*base, "config", "--get", "extensions.partialClone"], stdout=subprocess.PIPE)
    if partial.returncode == 0:
        return ["git history: partial cloneでは手元にないblobを検査できない（--filter を付けずにcloneする）"]
    refs = [ref for ref in git("for-each-ref", "--format=%(refname)").splitlines() if ref]
    commits = git("rev-list", "--all", "--topo-order", "--reverse").split()
    if not commits:
        return ["git history: commitがない"]
    excluded = vlat_release.excluded_entries(vlat_release.load_ledger(root))
    objects = [line.partition(" ")[::2] for line in git("rev-list", "--objects", "--all").splitlines() if line]
    types = dict(line.split(" ", 1) for line in git(
        "cat-file", "--batch-check=%(objectname) %(objecttype)",
        stdin="".join(f"{sha}\n" for sha, _ in objects).encode()).splitlines())
    tags = [sha for sha, _ in objects if types.get(sha) == "tag"]

    reader = _ObjectReader(base)
    try:
        # 1. commit・annotated tag の作者情報とメッセージ。treeは最初に現れたcommitを記録する。
        tree_first_commit: dict[str, str] = {}
        for kind, shas in (("commit", commits), ("tag", tags)):
            for sha in shas:
                obj = reader.read(sha)
                if obj is None:
                    failures.append(f"{kind} {sha[:12]}: objectを読めない（不完全なリポジトリ）")
                    continue
                if kind == "commit":
                    tree_first_commit.setdefault(obj[1].split(b"\n", 1)[0].split()[1].decode(), sha)
                failures += [f"{kind} {sha[:12]}: {finding}" for finding in metadata_findings(obj[1], allowed, check_identities)]

        # 2. 全commitのtreeにある (blob, path, mode) と、それが最初に現れたcommit
        pairs: dict[tuple[str, str, str], str] = {}
        for tree, commit in tree_first_commit.items():
            for entry in git("ls-tree", "-r", "-z", tree).split("\0"):
                if entry:
                    meta, _, path = entry.partition("\t")
                    mode, _, sha = meta.split(" ")
                    pairs.setdefault((sha, path, mode), f"commit {commit[:12]}")
        # 3. commitのtree外にあるblob（tagが直接指すtree/blob等）。pathは rev-list が付けた名前（ないこともある）
        covered = {sha for sha, _, _ in pairs}
        for sha, name in objects:
            if types.get(sha) == "blob" and sha not in covered:
                pairs.setdefault((sha, name, REGULAR_MODE), "commit外のblob")
                covered.add(sha)

        # 4. blobは1回だけ読み、pathごとに判定する
        by_blob: dict[str, list[tuple[str, str, str]]] = collections.defaultdict(list)
        for (sha, path, mode), where in pairs.items():
            by_blob[sha].append((path, mode, where))
        file_failures = []
        for sha, entries in by_blob.items():
            data = None
            if any(mode != GITLINK_MODE for _, mode, _ in entries):
                obj = reader.read(sha)
                data = obj[1] if obj is not None and obj[0] == "blob" else None
            for path, mode, where in entries:
                label = f"history {path or '(pathなし)'} (blob {sha[:12]}, {where})"
                if mode != GITLINK_MODE and data is None:
                    findings = path_findings(path, excluded) + ["blobを読めない（不完全なリポジトリ）"]
                else:
                    notes = NOTES_PATH.fullmatch(path.replace("/", "")) is not None
                    findings = file_findings(path, data, excluded, mode, True if notes else None)
                file_failures += [(path, sha, f"{label}: {finding}") for finding in findings]
        failures += [message for _, _, message in sorted(file_failures)]
    finally:
        reader.close()

    namespaces = collections.Counter(ref.split("/")[1] if ref.count("/") else ref for ref in refs)
    print(f"git history: {len(refs)} refs ({', '.join(f'{k} {v}' for k, v in sorted(namespaces.items()))}), "
          f"{len(commits)} commits, {len(tags)} tag objects, {len(pairs)} path/blob pairs ({len(by_blob)} objects) scanned")
    return failures


def parse_emails(values: list[str]) -> set[str]:
    """--allow-email の値（複数指定・カンマ区切り可）を小文字の集合にする。空要素は無視する。"""
    return {email.strip().lower() for value in values for email in value.split(",") if email.strip()}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("export", type=Path, help="公開スナップショットのディレクトリ（公開版リポジトリのroot）")
    parser.add_argument("--git-history", action="store_true", help="公開版リポジトリの全履歴も検査する")
    parser.add_argument("--allow-email", action="append", default=[],
                        help="commit/tagの作者・コミッター・メッセージに公開してよいメールアドレス。複数指定・カンマ区切り可"
                             "（file内容の検査には影響しない）")
    parser.add_argument("--skip-author-emails", action="store_true",
                        help="作者・コミッター・タガーのメール検査だけを NOT RUN にする（許可リスト未設定のCI用。PASSには数えない）")
    args = parser.parse_args()
    root = args.export.resolve()
    if not root.is_dir():
        parser.error(f"not a directory: {root}")
    failures, warnings = check_tree(root)
    if args.git_history:
        failures += check_history(root, parse_emails(args.allow_email), not args.skip_author_emails)
        if args.skip_author_emails:
            print("NOT RUN: commit/tag author email check (allow-list not configured; not counted as PASS)")
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
