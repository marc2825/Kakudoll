#!/usr/bin/env python3
"""公開スナップショットと証拠収集で共有する、個人パス検出・伏字化・除外台帳の補助関数。

scriptsディレクトリから `import vlat_release` で読み込む（パッケージ化しない）。
この module は検出・置換の規則を1か所に集めるためのもので、ファイルを書き換えない。
"""
from __future__ import annotations

import hashlib
import json
import re
from collections.abc import Collection
from pathlib import Path

# 汎用プレースホルダー（<user>, USER, username, Public, Default）とCI runnerのhomeは個人情報ではない。
_NOT_PERSONAL = r"(?!<[^>]+>|USER\b|user\b|username\b|Public\b|Default\b|runner\b)"
_SEP = r"(?:\\\\|\\|/)+"

PERSONAL_PATH_PATTERNS: list[tuple[str, re.Pattern[str]]] = [
    # Windowsのhome（区切りは \ と \\ と / のいずれも対象）
    ("personal-windows-home", re.compile(r"\b[A-Za-z]:" + _SEP + r"Users" + _SEP + _NOT_PERSONAL + r"[A-Za-z0-9_.-]+", re.I)),
    # WSLから見たWindowsのhome
    ("personal-wsl-home", re.compile(r"/mnt/[a-z]/Users/" + _NOT_PERSONAL + r"[A-Za-z0-9_.-]+", re.I)),
    # Linux/macOSのhome（直後に / が続くもの）
    ("personal-posix-home", re.compile(r"(?<![A-Za-z0-9_.:-])/(?:home|Users)/" + _NOT_PERSONAL + r"[A-Za-z0-9_.-]+(?=/)")),
]

SECRET_PATTERNS: list[tuple[str, re.Pattern[str]]] = [
    ("github-token", re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b")),
    ("aws-access-key", re.compile(r"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")),
    ("private-key-block", re.compile(r"-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----")),
    ("openai-like-token", re.compile(r"\bsk-[A-Za-z0-9_-]{20,}\b")),
    ("slack-token", re.compile(r"\bxox[abposr]-[A-Za-z0-9-]{10,}\b")),
    ("unity-license-file", re.compile(r"<License\s+id=|<(?:Developer)Data\b|<(?:Machine)Bindings\b")),
]

# 公開して差し支えない連絡先・機械生成アドレス。個人のメールアドレスは列挙しない。
# noreply@github.com は、GitHub が PR の検査用マージや Web 画面でのマージで作る commit の committer。
_ALLOWED_EMAIL = re.compile(r"(?i)^(?:noreply@(?:anthropic|github)\.com|[^@]+@(?:example\.(?:com|org|net)|users\.noreply\.github\.com))$")
_EMAIL = re.compile(r"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}\b")
_WINDOWS_HOSTNAME = re.compile(r"\b(?:DESKTOP|LAPTOP)-[A-Z0-9]{6,}\b")
# VRChatのユーザー・アバター・ワールド・ファイルID。インスタンス文字列などに個人のIDが混ざるのを防ぐ。
_VRCHAT_ID = re.compile(r"\b(?:usr|avtr|wrld|file)_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", re.I)
_PRIVATE_IPV4 = re.compile(r"\b(?:10\.\d{1,3}|192\.168|172\.(?:1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}\b")

LEDGER_RELATIVE_PATH = "docs/PUBLIC_EXPORT_LEDGER.json"
LEDGER_FORMAT = "vlat-public-export-ledger/1"


def find_personal_paths(text: str) -> list[tuple[str, int]]:
    """個人の絶対パスの種類ごとの件数を返す。値そのものは返さない。"""
    found = []
    for kind, regex in PERSONAL_PATH_PATTERNS:
        count = len(regex.findall(text))
        if count:
            found.append((kind, count))
    return found


def find_other_private_data(text: str, allowed_emails: Collection[str] = ()) -> list[tuple[str, int]]:
    """認証情報らしき文字列、個人メール、Windows hostname、内部IPv4、VRChatのIDの件数を返す。

    allowed_emails は所有者が公開を認めたメール（大文字小文字を区別しない）。commit/tagの作者情報と
    メッセージの検査だけで渡し、file内容の検査では渡さない。
    """
    allowed = {email.lower() for email in allowed_emails}
    found = []
    for kind, regex in SECRET_PATTERNS:
        count = len(regex.findall(text))
        if count:
            found.append((kind, count))
    emails = [m for m in _EMAIL.findall(text) if not _ALLOWED_EMAIL.match(m) and m.lower() not in allowed]
    if emails:
        found.append(("email-address", len(emails)))
    for kind, regex in (("windows-hostname", _WINDOWS_HOSTNAME), ("private-ipv4", _PRIVATE_IPV4), ("vrchat-id", _VRCHAT_ID)):
        count = len(regex.findall(text))
        if count:
            found.append((kind, count))
    return found


def redact_personal_paths(text: str) -> tuple[str, int]:
    """個人homeを汎用表記へ置き換える。返り値は（置換後, 置換件数）。

    生の証拠ファイルには使わない（整合性が壊れる）。新しく収集・出力するログとtest XMLだけに使う。
    """
    total = 0

    def windows(match: re.Match[str]) -> str:
        # 区切り文字（\, \\, /）は後続部分にそのまま残るため、home部分だけを置き換える。
        nonlocal total
        total += 1
        return "%USERPROFILE%"

    text = PERSONAL_PATH_PATTERNS[0][1].sub(windows, text)

    def wsl(match: re.Match[str]) -> str:
        nonlocal total
        total += 1
        return match.group(0).rsplit("/", 1)[0] + "/<user>"

    text = PERSONAL_PATH_PATTERNS[1][1].sub(wsl, text)

    def posix(match: re.Match[str]) -> str:
        nonlocal total
        total += 1
        return "~"

    text = PERSONAL_PATH_PATTERNS[2][1].sub(posix, text)
    return text, total


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def load_ledger(root: Path) -> dict | None:
    """公開スナップショットの除外台帳を読む。Private側（台帳なし）ではNoneを返す。"""
    path = root / LEDGER_RELATIVE_PATH
    if not path.is_file():
        return None
    ledger = json.loads(path.read_text(encoding="utf-8"))
    if ledger.get("format") != LEDGER_FORMAT:
        raise ValueError(f"{LEDGER_RELATIVE_PATH}: unsupported format {ledger.get('format')!r}")
    return ledger


def excluded_entries(ledger: dict | None) -> dict[str, dict]:
    """除外台帳を path -> entry の辞書にする。台帳がなければ空。"""
    if not ledger:
        return {}
    return {entry["path"]: entry for entry in ledger.get("excluded", [])}


def excluded_under(ledger: dict | None, prefix: str) -> list[dict]:
    """prefix（ディレクトリまたはファイル）以下で公開対象から除外されたfileを返す。"""
    prefix = prefix.rstrip("/")
    return [entry for path, entry in excluded_entries(ledger).items() if path == prefix or path.startswith(prefix + "/")]
