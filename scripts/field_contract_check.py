#!/usr/bin/env python3
"""ADR 0013 §5.1 — integrity gate for the published device-mapping field contract.

The *deep* drift checks live in the C# test suite and run under `make test`:
  - the committed MQTT envelope schema vs. its C# source (EnvelopeSchemaTests), and
  - runtime schema_version enforcement + example-mapping conformance (loader tests).

This gate guards the *shipped* schema files themselves — the artefacts that go into
the release bundle (`make release-assets`) — which `make schema-*` (Postgres DDL only)
does not touch: every schema in the published set is present, parses as a JSON object,
and declares a $schema dialect; the three protocol-mapping schemas pin schema_version
to the v1 major; and the bundle CHANGELOG is present. Stdlib only — no network, so it
runs in a bare python container.
"""
import json
import pathlib
import sys

SCHEMA_DIR = pathlib.Path("config/schema")

# The published contract set: these MUST ship in every release bundle.
REQUIRED_SCHEMAS = (
    "device-point.schema.json",
    "modbus-mapping.schema.json",
    "mqtt-mapping.schema.json",
    "opcua-mapping.schema.json",
    "mqtt-telemetry-envelope.schema.json",
)

# The protocol-mapping schemas whose schema_version pins the contract major.
MAPPING_SCHEMAS = (
    "modbus-mapping.schema.json",
    "mqtt-mapping.schema.json",
    "opcua-mapping.schema.json",
)

CONTRACT_MAJOR = ["v1"]


def main() -> int:
    errors: list[str] = []

    if not (SCHEMA_DIR / "CHANGELOG.md").is_file():
        errors.append(f"{SCHEMA_DIR / 'CHANGELOG.md'}: schema-bundle CHANGELOG is missing")

    # Every *.schema.json in the dir must at least parse — a malformed schema would
    # ship a broken contract even if it is not in the required set.
    parsed: dict[str, object] = {}
    for path in sorted(SCHEMA_DIR.glob("*.schema.json")):
        try:
            doc = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            errors.append(f"{path}: invalid JSON — {exc}")
            continue
        if not isinstance(doc, dict):
            errors.append(f"{path}: top-level value must be a JSON object")
            continue
        if "$schema" not in doc:
            errors.append(f"{path}: missing $schema dialect declaration")
        parsed[path.name] = doc

    for name in REQUIRED_SCHEMAS:
        if not (SCHEMA_DIR / name).is_file():
            errors.append(f"{SCHEMA_DIR / name}: required contract schema is missing")

    for name in MAPPING_SCHEMAS:
        doc = parsed.get(name)
        if not isinstance(doc, dict):
            continue  # missing/invalid already reported above
        props = doc.get("properties")
        if not isinstance(props, dict) or "schema_version" not in props:
            errors.append(f"{SCHEMA_DIR / name}: must declare a schema_version property")
            continue
        required = doc.get("required")
        if not isinstance(required, list) or "schema_version" not in required:
            errors.append(f"{SCHEMA_DIR / name}: schema_version must be required")
        enum = props["schema_version"].get("enum")
        if enum != CONTRACT_MAJOR:
            errors.append(
                f"{SCHEMA_DIR / name}: schema_version.enum must be {CONTRACT_MAJOR!r}, got {enum!r}"
            )

    if errors:
        print("field-contract-check FAILED:", file=sys.stderr)
        for err in errors:
            print(f"  - {err}", file=sys.stderr)
        return 1

    print(
        f"field-contract-check OK — {len(parsed)} schemas parse, "
        f"{len(REQUIRED_SCHEMAS)} required present, schema_version pinned to "
        f"{CONTRACT_MAJOR[0]}, CHANGELOG present"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
