#!/usr/bin/env python3
"""Unityが出力したCobertura XMLのCore行カバレッジ下限を検査する。"""

from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as element_tree
from pathlib import Path


def find_cobertura_files(path: Path) -> list[Path]:
    """Cobertura形式のcoverageルートを持つXMLファイルを返す。"""

    candidates = [path] if path.is_file() else sorted(path.rglob("*.xml"))
    results: list[Path] = []
    for candidate in candidates:
        try:
            root = element_tree.parse(candidate).getroot()
        except (OSError, element_tree.ParseError):
            continue
        if root.tag.rsplit("}", 1)[-1] == "coverage" and "line-rate" in root.attrib:
            results.append(candidate)
    return results


def read_core_rate(path: Path, assembly: str) -> float | None:
    """1つのCobertura XMLから対象アセンブリの行カバレッジを返す。"""

    root = element_tree.parse(path).getroot()
    packages = root.findall(".//package")
    covered_lines = 0
    valid_lines = 0
    for package in packages:
        name = package.attrib.get("name", "")
        if name == assembly or name.startswith(assembly + "."):
            for line in package.findall(".//line"):
                valid_lines += 1
                if int(line.attrib.get("hits", "0")) > 0:
                    covered_lines += 1

    if valid_lines:
        return covered_lines / valid_lines

    # package要素のない、Coreだけに絞ったフラットなレポートではルート値を使う。
    # 他assemblyのpackageしかないレポートのルート値を採用すると、Core未計測でも
    # 高カバレッジとして誤判定するため、その場合は候補から除外する。
    return float(root.attrib["line-rate"]) if not packages else None


def main() -> int:
    """CLI引数を解析し、カバレッジ下限を検査する。"""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("coverage_path", type=Path)
    parser.add_argument("--minimum", type=float, default=0.80)
    parser.add_argument("--assembly", default="VLat.Core")
    arguments = parser.parse_args()

    if not 0.0 <= arguments.minimum <= 1.0:
        parser.error("--minimum must be between 0 and 1")

    files = find_cobertura_files(arguments.coverage_path)
    if not files:
        print(
            f"FAIL: no Cobertura coverage XML found under {arguments.coverage_path}",
            file=sys.stderr,
        )
        return 1

    rates = [read_core_rate(path, arguments.assembly) for path in files]
    measured_rates = [rate for rate in rates if rate is not None]
    if not measured_rates:
        print(
            f"FAIL: no coverage data found for assembly {arguments.assembly}",
            file=sys.stderr,
        )
        return 1

    best_rate = max(measured_rates)
    print(f"{arguments.assembly} line coverage: {best_rate:.2%}")
    if best_rate + 1e-12 < arguments.minimum:
        print(
            f"FAIL: required line coverage is {arguments.minimum:.2%}",
            file=sys.stderr,
        )
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
