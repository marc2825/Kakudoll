#!/usr/bin/env python3
"""v0.2 Keyform schemaと合成fixture・構造負対照を検証する。意味/binary検証はC#で行う。"""
from copy import deepcopy
from pathlib import Path

from jsonschema import RefResolver
from validate_schema import ExactDraft202012Validator, load_json


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    schema_path = root / "schema/vlat.keyform-v0.2.schema.json"
    schema = load_json(schema_path)
    ExactDraft202012Validator.check_schema(schema)
    validator = ExactDraft202012Validator(
        schema,
        resolver=RefResolver(
            base_uri=schema_path.as_uri(),
            referrer=schema,
            store={(root / "schema/vlat.schema.json").as_uri(): load_json(root / "schema/vlat.schema.json")},
        ),
    )
    fixture = load_json(root / "Packages/jp.vlat.core/Tests/EditMode/Fixtures/Keyform/three-keys.vlat.json")
    validator.validate(fixture)
    mutations = [
        lambda d: d["vlat"].update(version="0.1.0"),
        lambda d: d.update(unknown=True),
        lambda d: d["keyformGroups"][0].update(interpolation="rbf"),
        lambda d: d["keyformGroups"][0].update(coordinateSpace="world"),
        lambda d: d["keyformGroups"][0].update(topologyHash="invalid"),
        lambda d: d["keyformGroups"][0].update(keys=[]),
        lambda d: d["keyformGroups"][0].update(keys=[d["keyformGroups"][0]["keys"][0]] * 17),
        lambda d: d["keyformGroups"][0]["keys"][1].pop("delta"),
        lambda d: d["keyformGroups"][0]["keys"][1].update(yaw="0"),
        lambda d: d["keyformGroups"][0]["keys"][0]["delta"].update(normals={"bufferView": 1, "componentType": "FLOAT32"}),
        lambda d: d["keyformGroups"].append(deepcopy(d["keyformGroups"][0])),
        lambda d: d["keyformGroups"][0].update(supportStart=180),
    ]
    for i, mutate in enumerate(mutations):
        candidate = deepcopy(fixture)
        mutate(candidate)
        if validator.is_valid(candidate):
            raise AssertionError(f"negative fixture {i} was accepted")
    legacy = ExactDraft202012Validator(load_json(root / "schema/vlat.schema.json"))
    assert not legacy.is_valid(fixture), "v0.1 schema must reject the extension"
    print(f"PASS keyform schema: 1 valid, {len(mutations)} negative mutations; legacy rejects v0.2 extension")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
