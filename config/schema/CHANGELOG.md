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
