# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

### Changed

### Fixed

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
