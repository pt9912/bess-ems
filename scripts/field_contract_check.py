#!/usr/bin/env python3
"""ADR 0013 §5.1 — integrity + conformance gate for the published device-mapping field contract.

Runs in the `field-contract-check` Docker stage, which provides `jsonschema`.

Checks (a deliberately broken schema OR example mapping fails this gate):
  1. Every config/schema/*.schema.json parses as a JSON object and declares $schema.
  2. Every schema is a valid JSON Schema (Draft 2020-12 meta-validation).
  3. The required contract set is present.
  4. The three protocol-mapping schemas pin schema_version to the v1 major.
  5. Every shipped example under config/examples/ validates against its schema
     ($refs across schemas resolve through a $id registry).
  6. The schema-bundle CHANGELOG is present.

The one contract check deliberately NOT here is the MQTT envelope C#<->schema drift:
it needs the C# source (MqttPayloads.cs) and lives in EnvelopeSchemaTests under
`make test`, in the same `make gates` aggregate.
"""
import json
import pathlib
import sys

from jsonschema import Draft202012Validator
from jsonschema.exceptions import SchemaError
from referencing import Registry, Resource

SCHEMA_DIR = pathlib.Path("config/schema")
EXAMPLES_DIR = pathlib.Path("config/examples")

# Must all ship in every release bundle.
REQUIRED_SCHEMAS = (
    "device-point.schema.json",
    "modbus-mapping.schema.json",
    "mqtt-mapping.schema.json",
    "opcua-mapping.schema.json",
    "mqtt-telemetry-envelope.schema.json",
)

# Protocol-mapping schemas whose schema_version pins the contract major.
MAPPING_SCHEMAS = (
    "modbus-mapping.schema.json",
    "mqtt-mapping.schema.json",
    "opcua-mapping.schema.json",
)

CONTRACT_MAJOR = ["v1"]

# Which schema each shipped example validates against, matched against the path
# relative to config/examples/. A shipped example matched by no rule is an error,
# so a new example cannot silently escape the gate.
EXAMPLE_RULES = (
    ("adapters/modbus.*.json", "modbus-mapping.schema.json"),
    ("adapters/mqtt.*.json", "mqtt-mapping.schema.json"),
    ("adapters/opcua.*.json", "opcua-mapping.schema.json"),
    ("asset.*.json", "asset.schema.json"),
    ("assets.*.json", "assets.schema.json"),
    ("retention.json", "retention.schema.json"),
)


def load_json(path, errors):
    """Parse JSON, appending a clean diagnostic (never a traceback) on failure."""
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except UnicodeDecodeError as exc:
        errors.append(f"{path}: not valid UTF-8 — {exc}")
    except json.JSONDecodeError as exc:
        errors.append(f"{path}: invalid JSON — {exc}")
    return None


def match_schema(rel):
    for pattern, target in EXAMPLE_RULES:
        if rel.match(pattern):
            return target
    return None


def main() -> int:
    errors = []

    if not (SCHEMA_DIR / "CHANGELOG.md").is_file():
        errors.append(f"{SCHEMA_DIR / 'CHANGELOG.md'}: schema-bundle CHANGELOG is missing")

    # Parse every schema; build a $id -> resource registry so cross-schema $refs
    # (e.g. the mapping schemas' $ref into device-point.json) resolve.
    schemas = {}
    resources = []
    for path in sorted(SCHEMA_DIR.glob("*.schema.json")):
        doc = load_json(path, errors)
        if doc is None:
            continue
        if not isinstance(doc, dict):
            errors.append(f"{path}: top-level value must be a JSON object")
            continue
        if "$schema" not in doc:
            errors.append(f"{path}: missing $schema dialect declaration")
        schemas[path.name] = doc
        schema_id = doc.get("$id")
        if isinstance(schema_id, str):
            resources.append((schema_id, Resource.from_contents(doc)))
    registry = Registry().with_resources(resources)

    # Meta-validate every schema (Draft 2020-12); track which are themselves broken
    # so example validation does not later choke on them with a raw traceback.
    invalid_schemas = set()
    for name, doc in schemas.items():
        try:
            Draft202012Validator.check_schema(doc)
        except SchemaError as exc:
            invalid_schemas.add(name)
            errors.append(f"{SCHEMA_DIR / name}: not a valid JSON Schema — {exc.message}")

    for name in REQUIRED_SCHEMAS:
        if not (SCHEMA_DIR / name).is_file():
            errors.append(f"{SCHEMA_DIR / name}: required contract schema is missing")

    # schema_version pinned to the contract major on the protocol mappings.
    for name in MAPPING_SCHEMAS:
        doc = schemas.get(name)
        if not isinstance(doc, dict):
            continue  # missing/invalid already reported
        props = doc.get("properties")
        if not isinstance(props, dict) or not isinstance(props.get("schema_version"), dict):
            errors.append(f"{SCHEMA_DIR / name}: must declare a schema_version property (object)")
            continue
        required = doc.get("required")
        if not isinstance(required, list) or "schema_version" not in required:
            errors.append(f"{SCHEMA_DIR / name}: schema_version must be required")
        enum = props["schema_version"].get("enum")
        if enum != CONTRACT_MAJOR:
            errors.append(
                f"{SCHEMA_DIR / name}: schema_version.enum must be {CONTRACT_MAJOR!r}, got {enum!r}"
            )

    # Every shipped example validates against its schema.
    example_count = 0
    if EXAMPLES_DIR.is_dir():
        for example in sorted(EXAMPLES_DIR.rglob("*.json")):
            rel = example.relative_to(EXAMPLES_DIR)
            schema_name = match_schema(rel)
            if schema_name is None:
                errors.append(f"{example}: shipped example has no validation rule (add it to EXAMPLE_RULES)")
                continue
            schema = schemas.get(schema_name)
            if not isinstance(schema, dict):
                errors.append(f"{example}: schema {schema_name} unavailable to validate against")
                continue
            if schema_name in invalid_schemas:
                errors.append(f"{example}: cannot validate — schema {schema_name} is not a valid JSON Schema")
                continue
            instance = load_json(example, errors)
            if instance is None:
                continue
            try:
                found = list(Draft202012Validator(schema, registry=registry).iter_errors(instance))
            except Exception as exc:  # unresolvable $ref, malformed schema, … — fail cleanly, never a traceback
                errors.append(f"{example}: validation against {schema_name} could not run — {type(exc).__name__}: {exc}")
                continue
            for err in found:
                loc = "/".join(str(p) for p in err.path) or "(root)"
                errors.append(f"{example}: fails {schema_name} at {loc}: {err.message}")
            example_count += 1

    if errors:
        print("field-contract-check FAILED:", file=sys.stderr)
        for err in errors:
            print(f"  - {err}", file=sys.stderr)
        return 1

    print(
        f"field-contract-check OK — {len(schemas)} schemas valid (Draft 2020-12), "
        f"{len(REQUIRED_SCHEMAS)} required present, {example_count} examples conform, "
        f"schema_version pinned to {CONTRACT_MAJOR[0]}, CHANGELOG present"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
