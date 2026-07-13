#!/usr/bin/env python3
"""ADR 0013 §5.1 — integrity + conformance gate for the published device-mapping field contract.

Runs in the `field-contract-check` Docker stage, which provides `jsonschema`.

Checks (a deliberately broken schema OR example mapping OR vector manifest fails
this gate):
  1. Every config/schema/*.schema.json parses as a JSON object and declares $schema.
  2. Every schema is a valid JSON Schema (Draft 2020-12 meta-validation).
  3. The required contract set is present.
  4. The three protocol-mapping schemas pin schema_version to the v1 major.
  5. Every shipped example under config/examples/ validates against its schema
     ($refs across schemas resolve through a $id registry).
  6. The schema-bundle CHANGELOG is present.
  7. ADR 0013 §5.2: both golden-vector manifests are present, validate against
     the manifest schema, and every telemetry/command/command_ack payload
     validates against its envelope definition WITH key-set discipline (no key
     beyond properties, all required present — exact set for telemetry, where
     properties == required). The envelope has no additionalProperties:false,
     so validation alone cannot catch an ADDED producer field. Python mirror
     of the C#/Go asserts — runs without .NET.

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
    "golden-vector-manifest.schema.json",
)

VECTORS_DIR = SCHEMA_DIR / "vectors"
VECTOR_MANIFEST_SCHEMA = "golden-vector-manifest.schema.json"
ENVELOPE_SCHEMA = "mqtt-telemetry-envelope.schema.json"

# Both authority manifests must ship (same presence floor as
# REQUIRED_EXAMPLE_PATTERNS — a vanished vectors/ dir must not false-pass).
REQUIRED_VECTOR_MANIFESTS = (
    "mqtt-golden-vectors.field.v1.json",
    "mqtt-golden-vectors.ems.v1.json",
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

# Patterns that MUST match at least one shipped example. Without this floor the gate
# false-passes ("0 examples conform", exit 0) if config/examples/ (or its adapters/
# mappings) vanishes in a bad merge — the same presence regression REQUIRED_SCHEMAS
# guards for schemas.
REQUIRED_EXAMPLE_PATTERNS = (
    "adapters/modbus.*.json",
    "adapters/mqtt.*.json",
    "adapters/opcua.*.json",
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


def match_rule(rel):
    for pattern, target in EXAMPLE_RULES:
        if rel.match(pattern):
            return pattern, target
    return None, None


def vector_payload_errors(path, instance, envelope_defs):
    """ADR 0013 §5.2: every telemetry/command/command_ack payload must validate
    against its envelope definition, carry no key beyond its properties, and
    carry every required key. properties == required for telemetry, so the
    generic rule IS the exact-key-set check there; command/command_ack keep
    their optional members (reactive_power_kvar, reason). The subset rule is
    what the schema alone (no additionalProperties:false) cannot enforce.
    status/fault payloads have no envelope definition by design (§5.1
    non-goal) and are pinned by the Go drift gate instead."""
    if not isinstance(envelope_defs, dict):
        return [f"{path}: envelope $defs unavailable — cannot check vector payloads"]
    errs = []
    cases = instance.get("cases") if isinstance(instance, dict) else None
    for case in cases if isinstance(cases, list) else []:
        if not isinstance(case, dict):
            continue
        definition = envelope_defs.get(case.get("topic_name"))
        if not isinstance(definition, dict):
            continue  # status/fault: no envelope definition by design
        name = case.get("name")
        payload = case.get("payload")
        if not isinstance(payload, dict):
            if not case.get("suppressed"):
                errs.append(f"{path}: case {name!r} has no payload object")
            continue
        for err in Draft202012Validator(definition).iter_errors(payload):
            loc = "/".join(str(p) for p in err.path) or "(root)"
            errs.append(f"{path}: case {name!r} fails the envelope {case.get('topic_name')} definition at {loc}: {err.message}")
        properties = set(definition.get("properties", {}))
        extra = sorted(set(payload) - properties)
        if extra:
            errs.append(
                f"{path}: case {name!r} carries keys {extra} beyond the envelope "
                f"{case.get('topic_name')} properties (added producer fields cannot pass silently)"
            )
        missing = sorted(set(definition.get("required", [])) - set(payload))
        if missing:
            errs.append(f"{path}: case {name!r} misses envelope-required keys {missing}")
    return errs


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

    # Every shipped example validates against its schema, and the required example
    # patterns must each be present (no silent 0-examples pass).
    example_count = 0
    if not EXAMPLES_DIR.is_dir():
        errors.append(f"{EXAMPLES_DIR}: examples directory is missing")
    else:
        matched = {pattern: 0 for pattern, _ in EXAMPLE_RULES}
        for example in sorted(EXAMPLES_DIR.rglob("*.json")):
            rel = example.relative_to(EXAMPLES_DIR)
            pattern, schema_name = match_rule(rel)
            if schema_name is None:
                errors.append(f"{example}: shipped example has no validation rule (add it to EXAMPLE_RULES)")
                continue
            matched[pattern] += 1
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
        for pattern in REQUIRED_EXAMPLE_PATTERNS:
            if matched.get(pattern, 0) == 0:
                errors.append(f"{EXAMPLES_DIR}: no example matches required pattern '{pattern}' — a protocol mapping example must ship")

    # ADR 0013 §5.2: golden-vector manifests (presence + manifest-schema
    # validation + telemetry payloads against the envelope, exact key set).
    vector_count = 0
    for name in REQUIRED_VECTOR_MANIFESTS:
        if not (VECTORS_DIR / name).is_file():
            errors.append(f"{VECTORS_DIR / name}: required golden-vector manifest is missing")
    manifest_schema = schemas.get(VECTOR_MANIFEST_SCHEMA)
    envelope = schemas.get(ENVELOPE_SCHEMA)
    envelope_defs = envelope.get("$defs") if isinstance(envelope, dict) else None
    if not isinstance(manifest_schema, dict) or VECTOR_MANIFEST_SCHEMA in invalid_schemas:
        errors.append(f"{SCHEMA_DIR / VECTOR_MANIFEST_SCHEMA}: unavailable — cannot validate golden-vector manifests")
    elif VECTORS_DIR.is_dir():
        for path in sorted(VECTORS_DIR.glob("*.json")):
            instance = load_json(path, errors)
            if instance is None:
                continue
            for err in Draft202012Validator(manifest_schema, registry=registry).iter_errors(instance):
                loc = "/".join(str(p) for p in err.path) or "(root)"
                errors.append(f"{path}: fails {VECTOR_MANIFEST_SCHEMA} at {loc}: {err.message}")
            errors.extend(vector_payload_errors(path, instance, envelope_defs))
            vector_count += 1

    if errors:
        print("field-contract-check FAILED:", file=sys.stderr)
        for err in errors:
            print(f"  - {err}", file=sys.stderr)
        return 1

    print(
        f"field-contract-check OK — {len(schemas)} schemas valid (Draft 2020-12), "
        f"{len(REQUIRED_SCHEMAS)} required present, {example_count} examples conform, "
        f"{vector_count} golden-vector manifests conform (payloads envelope-checked incl. key sets), "
        f"schema_version pinned to {CONTRACT_MAJOR[0]}, CHANGELOG present"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
