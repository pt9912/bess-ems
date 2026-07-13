# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

### Changed

### Fixed

## [2.0.0] - 2026-07-13

Device mappings become a published, versioned field contract
(ADR 0013 §5.1). Native control kernel ABI stays at 0.3.0 (no native
changes in this release).

### Changed

- **BREAKING — `schema_version` is now required on Modbus and MQTT
  mapping files.** The configuration loader hard-rejects any
  `modbus-mapping`/`mqtt-mapping` file without `"schema_version": "v1"`
  (pre-check before schema validation, same pattern OPC-UA mappings
  already enforced). **Migration:** add `"schema_version": "v1"` as a
  top-level field to every Modbus/MQTT mapping file; all bundled
  example mappings are already lifted.
- Markdown docs gate migrated to `d-check` plus a host-path sensor
  (absolute local paths in committed docs fail the gate).
- Release workflow actions bumped to Node 24 runtimes.
- Helm chart `bess-ems` version/appVersion 1.0.0 → 2.0.0.

### Added

- **MQTT telemetry envelope schema**
  (`config/schema/mqtt-telemetry-envelope.schema.json`): the wide
  telemetry snapshot, command and command-ack payloads are now
  machine-readable, generated from the C# wire types
  (`MqttPayloads.cs`) with a two-sided drift check (generated-schema
  diff plus serializer round-trip validation) in the test gates.
- **Versioned schema bundle as release asset**: `config/schema/` ships
  as a reproducible `bess-ems-schemas-<version>.tar.gz` (including a
  schema CHANGELOG and `min_supported` floor) next to chart/tarball/
  SBOM, listed in `SHA256SUMS` — external simulators can generate
  against the contract instead of mirroring it (ADR 0013).
- **`make field-contract-check` gate**: Draft 2020-12 meta-validation
  of every schema in `config/schema/` plus validation of all bundled
  example mappings against their schemas; wired into `make gates` and
  CI.
- **`Bess:SnapshotMaxAge` config key**: the telemetry snapshot
  freshness window (previously hardcoded 10 s) is configurable in both
  the Host and Api processes; default remains `00:00:10`.
- ADR 0013 (device mappings as published versioned field contract);
  ADRs 0010–0012 close AR-OPEN-009/-008/-011.
- English `README.md` (German original moved to `README.de.md`).

### Security

- Pin `Microsoft.OpenApi` to patched 2.7.5 (CVE-2026-49451).

## [1.0.0] - 2026-05-14

First production release. Milestones M1–M6 are closed; native control
kernel ABI is 0.3.0 (independent SemVer line — see
`native/battery_control_core/include/battery_control_core.h`).

### Added

- **M1 — MVP regulation pipeline**: domain model, hexagonal application
  layer with driving/driven ports, Modbus telemetry source and command
  sink, Postgres persistence via Dapper, `/health` and `/metrics`,
  hardened Docker runtime image, full mandatory gate set.
- **M2 — market & optimization**: OR-Tools GLOP day-ahead optimizer,
  market commitment priorities, configurable objective (degradation
  cost, SoC penalty), OTel tracing for control/dispatch/optimization,
  PID controller primitive, replay harness, HIL simulator integration,
  versioned schema migrations via `d-migrate`.
- **M3 — native control core**: C-language native kernel, P/Invoke
  fallback adapter, four native quality gates (lint, sanitizer,
  coverage, exclusions), replay-based parity, PID native slice with
  ABI minor bump 0.1 → 0.2, M3-D2 production routing activation.
- **M4 — ancillary services & OPC-UA**: intraday re-optimization,
  reserve product modelling, regelleistung activation pipeline,
  OPC-UA telemetry/command adapter with production-fail-closed
  security (SignAndEncrypt + cert trust), MQTT QoS with TLS/auth
  hardening and ExactlyOnce gate, versioned OPC-UA mappings.
- **M5 — MPC, solver sidecar, replay**: gRPC sidecar contract with
  managed fallback, MPC kernel with local OSQP and Kalman, native
  high-frequency telemetry filter (ABI 0.3.0), replay manifest and
  golden traces, sidecar metrics, worker+sidecar compose gate,
  source-neutral price-series port.
- **M6 — scaling, UI, edge / multi-asset**: operator UI, multi-asset
  hosting (shared worker default per ADR 0007), Kubernetes/Helm chart,
  TimescaleDB option, edge boundary (ADR 0008), certification gate.
- **Release pipeline**: GHCR image push, helm chart package, source
  tarball, native `.so` + header, SBOM, SHA256SUMS, keyless cosign
  signature, GitHub Release with assets and CHANGELOG-extracted notes.
- **Process documentation**: [`docs/user/releasing.md`](docs/user/releasing.md).

### Changed

- Helm chart `bess-ems` version/appVersion 0.1.0 → 1.0.0 (synchronised
  with first major release; from 1.1.0 onwards chart and app versions
  MAY diverge per chart-versioning convention).
