#!/usr/bin/env python3
"""VLat JSON Schema と検証 fixture の整合性を検査する。"""

from __future__ import annotations

import argparse
import json
import sys
from decimal import Decimal
from pathlib import Path
from typing import Any, Iterable

try:
    from jsonschema import Draft202012Validator, validators
    from jsonschema.exceptions import SchemaError
except ImportError as exc:  # pragma: no cover - 実行環境の構成エラー
    print(
        "error: Python package 'jsonschema>=4.10,<5' is required",
        file=sys.stderr,
    )
    raise SystemExit(2) from exc


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SCHEMA = REPOSITORY_ROOT / "schema" / "vlat.schema.json"
DEFAULT_VALID_DIRECTORY = (
    REPOSITORY_ROOT
    / "Packages"
    / "jp.vlat.core"
    / "Tests"
    / "EditMode"
    / "Fixtures"
    / "Valid"
)
DEFAULT_INVALID_DIRECTORY = (
    REPOSITORY_ROOT
    / "Packages"
    / "jp.vlat.core"
    / "Tests"
    / "EditMode"
    / "Fixtures"
    / "Invalid"
)


def is_exact_json_integer(checker: Any, instance: Any) -> bool:
    """JSON Schema の integer を Decimal の精度を落とさず判定する。"""

    del checker
    if isinstance(instance, bool):
        return False
    if isinstance(instance, int):
        return True
    return (
        isinstance(instance, Decimal)
        and instance.is_finite()
        and instance == instance.to_integral_value()
    )


EXACT_TYPE_CHECKER = Draft202012Validator.TYPE_CHECKER.redefine(
    "integer",
    is_exact_json_integer,
)
ExactDraft202012Validator = validators.extend(
    Draft202012Validator,
    type_checker=EXACT_TYPE_CHECKER,
)


def load_json(path: Path) -> Any:
    """数値の字句精度を保って UTF-8 JSON ファイルを読み込む。"""

    with path.open("r", encoding="utf-8") as stream:
        return json.load(
            stream,
            parse_float=Decimal,
            parse_constant=reject_nonstandard_constant,
        )


def reject_nonstandard_constant(value: str) -> None:
    """標準JSONに存在しないNaNおよびInfinityリテラルを拒否する。"""

    raise ValueError(f"non-standard JSON numeric constant: {value}")


def fixture_paths(directory: Path) -> list[Path]:
    """指定ディレクトリ配下の VLat JSON fixture を決定的な順序で返す。"""

    return sorted(directory.rglob("*.vlat.json"))


def format_json_path(parts: Iterable[Any]) -> str:
    """jsonschema のパス要素を読みやすい JSON Path 風文字列へ変換する。"""

    result = "$"
    for part in parts:
        if isinstance(part, int):
            result += f"[{part}]"
        else:
            result += f".{part}"
    return result


def validate_fixtures(
    schema_path: Path,
    valid_directory: Path,
    invalid_directory: Path,
) -> int:
    """Schema と正例・負例 fixture を検証し、プロセス用終了コードを返す。"""

    failure_count = 0

    try:
        schema = load_json(schema_path)
        ExactDraft202012Validator.check_schema(schema)
    except (OSError, ValueError, SchemaError) as exc:
        print(f"FAIL schema {schema_path}: {exc}", file=sys.stderr)
        return 1

    validator = ExactDraft202012Validator(schema)
    valid_paths = fixture_paths(valid_directory)
    invalid_paths = fixture_paths(invalid_directory)

    if not valid_paths:
        print(f"FAIL no valid fixtures found under {valid_directory}", file=sys.stderr)
        failure_count += 1
    if not invalid_paths:
        print(f"FAIL no invalid fixtures found under {invalid_directory}", file=sys.stderr)
        failure_count += 1

    for path in valid_paths:
        try:
            instance = load_json(path)
        except (OSError, ValueError) as exc:
            print(f"FAIL valid {path}: cannot load JSON: {exc}", file=sys.stderr)
            failure_count += 1
            continue

        errors = sorted(
            validator.iter_errors(instance),
            key=lambda item: format_json_path(item.absolute_path),
        )
        if errors:
            first_error = errors[0]
            location = format_json_path(first_error.absolute_path)
            print(
                f"FAIL valid {path}: {location}: {first_error.message}",
                file=sys.stderr,
            )
            failure_count += 1
        else:
            print(f"PASS valid {path}")

    for path in invalid_paths:
        try:
            instance = load_json(path)
        except (OSError, ValueError) as exc:
            print(f"FAIL invalid {path}: cannot load JSON: {exc}", file=sys.stderr)
            failure_count += 1
            continue

        errors = list(validator.iter_errors(instance))
        if errors:
            print(f"PASS invalid {path} ({len(errors)} schema error(s))")
        else:
            print(
                f"FAIL invalid {path}: fixture unexpectedly satisfies the schema",
                file=sys.stderr,
            )
            failure_count += 1

    if failure_count:
        print(f"Schema fixture validation failed: {failure_count} failure(s)", file=sys.stderr)
        return 1

    print(
        "Schema fixture validation succeeded: "
        f"{len(valid_paths)} valid and {len(invalid_paths)} invalid fixture(s)."
    )
    return 0


def parse_arguments() -> argparse.Namespace:
    """コマンドライン引数を解析する。"""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--schema", type=Path, default=DEFAULT_SCHEMA)
    parser.add_argument("--valid-dir", type=Path, default=DEFAULT_VALID_DIRECTORY)
    parser.add_argument("--invalid-dir", type=Path, default=DEFAULT_INVALID_DIRECTORY)
    return parser.parse_args()


def main() -> int:
    """CLI エントリポイント。"""

    arguments = parse_arguments()
    return validate_fixtures(
        arguments.schema.resolve(),
        arguments.valid_dir.resolve(),
        arguments.invalid_dir.resolve(),
    )


if __name__ == "__main__":
    raise SystemExit(main())
