# Device-mapping schema bundle — CHANGELOG

The `config/schema/*.json` set is published as a versioned release asset
(`bess-ems-schemas-<version>.tar.gz`) so an external field simulator generates its
protocol adapters against one source of truth instead of hand-mirroring the wire
format (ADR 0013 §5.1).

Compatibility is file-based with no runtime handshake: consumers pin `schema_version`
and fail closed outside the supported range. A breaking bump ships the new major
alongside the previous one for a deprecation window (`min_supported`), per ADR 0013 §2
(Breaking-Bump-Rollout).

## v1

Initial published contract:

- `device-point`, `modbus-mapping`, `mqtt-mapping`, `opcua-mapping` — protocol mappings
  (all three now carry a required `schema_version: v1`).
- `mqtt-telemetry-envelope` — field-normative MQTT payload schema (telemetry / command /
  command_ack), generated from `MqttPayloads.cs`.
- `asset`, `assets`, `schedule`, `retention` — supporting config schemas.

`min_supported: v1`.

### Additive: golden vectors (ADR 0013 §5.2)

- `golden-vector-manifest` — manifest schema for structurally compared golden
  vectors (`golden-vector-manifest.v1`); payloads embedded as JSON objects
  (field-normative, no byte canon).
- `vectors/mqtt-golden-vectors.field.v1.json` — lifted from the Go field
  producer (`serializer.go` via `ResolveTelemetry`, `model.CommandAck`):
  telemetry / status / fault (incl. suppression case) / command_ack.
- `vectors/mqtt-golden-vectors.ems.v1.json` — lifted from the C# EMS producer
  (`MqttJson.Options`): command with and without `reactive_power_kvar`.

Contract major stays `v1` (additive change, no mapping-file impact).
