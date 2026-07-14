# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

### Changed

- **Docs reference gate consolidated onto d-check v0.42.0**
  (digest-pinned, up from v0.2.0). `make docs-check` now runs the
  `links`, `anchors`, `hostpaths`, and `spans` modules in a single
  hermetic container (`--network none`; no network module is active).
  The `hostpaths` module (DC-FA-HOST-001) subsumes the former host-path
  rest sensor — its `prefixes` list keeps `tmp` for parity with the
  retired checker (the d-check default omits it). `spans` newly flags
  unclosed code spans and nested links. Config lives in `.d-check.yml`.

- **Docs gate extended with the `codepaths` + `tracked` modules, wired
  via the `d-check --print-mk` fragment.** The Makefile now `include`s
  `d-check.mk` (targets `doc-check`, `doc-tracked`, `doc-doctor`, …;
  digest-pinned through `DCHECK_DIGEST`), and `make docs-check` runs
  `doc-check` (now also `codepaths` — inline-code path existence) plus
  `doc-tracked` (reference targets must be git-tracked). Historical and
  immutable docs (`done/` plans, Accepted ADRs, `archive/`) are
  `exempt-paths`; a documented runtime path and two genuine dangling
  references in `quality.md` are tracked via `codepaths.ignore-refs`
  rather than silently accepted (see Fixed).

- **Reference matrix (`matrix`) enforced: the specification no longer
  references ADRs downward.** `spec/lastenheft.md` and
  `spec/architecture.md` are the authoritative stratum; an architecture
  decision is settled in its ADR (which references the requirement
  upward), so the spec now states outcomes without pointing back at ADRs.
  All ADR references were removed from both spec files, and section 18
  "Offene architektonische Punkte" of the architecture document was
  trimmed to the genuinely open points — the closed ones were redundant
  with their deciding ADRs, which remain the authoritative record.
  Within the strata the same direction holds: `lastenheft.md` is more
  authoritative than `architecture.md`, so it must not reference it
  downward (`order` + `direction: no-downward`; the reverse, architecture
  → lastenheft, stays allowed). ADRs, in turn, must not reference planning
  documents downward (`adr → plan` forbidden) — a plan references its ADR
  upward, not the reverse. The nine Accepted ADRs that already cite their
  implementing plans as provenance (0001–0009) are immutable and
  grandfathered; the rule is live for 0010 onward and every new ADR (which
  can still declare a provenance link with `<!-- d-check:status-provenance
  -->`). `matrix` additionally forbids references to `superseded`/
  `deprecated` ADRs.

- **ID cross-references linked, and the link policy scoped to the
  planning layer.** The `ids` module enforces a link policy: a bare
  "ADR NNNN" in prose must link to its ADR, with the §-section anchor
  when a section is named (43 such references linked across the live
  docs, each anchor verified). Because RM-* and ADR identifiers are
  planning-internal (they live under `docs/plan/…`, non-normative — the
  norm is `LH-*`), the policy is scoped to `docs/plan/planning` rather
  than forced onto user/spec/code docs. The matrix gains two guards that
  stop the planning layer leaking into the authoritative one:
  `spec → plan` forbidden, and RM-* token detection on the `plan` class
  so a bare "RM-M…" in a new ADR (0014 onward) flags `matrix-forbidden`
  — the existing ADRs 0001–0013 are immutable and grandfathered.

### Removed

- **`tools/check_markdown_links.py` and its `docs-check` Dockerfile
  stage**: the host-local-absolute-path check is now the d-check
  `hostpaths` module (see above), so the standalone Python rest sensor
  and its dedicated build stage are gone — one check line and one
  container instead of two.

- **Planning identifiers and implementation/scope sections purged from
  the normative docs.** All RM-* references were removed from
  `spec/lastenheft.md`, `spec/architecture.md`, and `docs/user/quality.md`
  (the quality manual also lost its RM-slice "Quelle" provenance columns).
  Three lastenheft sections that tracked implementation/scope rather than
  requirements were dropped entirely: §27.2 "Anforderung zu
  Implementierung", §28 "Anhang A — MVP-Scope (historisch)", and §30
  "Klärungen" (which also carried a spec → planning-note link). No
  internal or external references pointed at the removed anchors.

### Fixed

- **Stale file paths in the operator quality manual**
  (`docs/user/quality.md`), surfaced by the new `codepaths` check: the
  JSON-Schema table pointed at `config/schema/*.json` (the files are
  `*.schema.json`) and the native-tooling table at `native/*` (the files
  live under `native/battery_control_core/`). Corrected. Two references
  remain flagged as real gaps rather than removed —
  `native/battery_control_core/.clang-format` (no clang-format config
  exists; only `.clang-tidy`) and `config/schema/limits.schema.json`
  (there is no `limits` schema) — tracked via `codepaths.ignore-refs`
  for follow-up.

## [2.2.1] - 2026-07-13

Patch release: the operator web shell is now actually served by the
production host, and the new operator user manual documents the operator
surface end to end. Native control kernel ABI stays at 0.3.0 (no native
changes in this release).

### Added

- **Operator user manual** (`docs/user/anwenderhandbuch.md`): task-based
  manual for operators (status, operator stop, price import, day-ahead +
  intraday optimization, troubleshooting) with UI screenshots, bound to
  the software version; authored per
  `docs/user/benutzerhandbuch-standard.md` (also added).

### Fixed

- **Operator web shell now served by the production host**: the shell
  assets (`wwwroot/operator/`) were published into the runtime image but
  `BessHostBuilder` never wired `UseOperatorUiStaticShell`, so
  `/operator/` returned 404 in the compose stack (RM-M6-01-B defines the
  shell as part of the operator surface). Pinned twice: an in-process
  host-composition test asserts the `/operator` → `/operator/` redirect,
  and `make runtime` now probes `/operator/` in the running container.

## [2.2.0] - 2026-07-13

ADR 0013 §5 is complete: the field contract gains Modbus golden vectors
and an operator-facing SUT mode. Native control kernel ABI stays at
0.3.0 (no native changes in this release).

### Added

- **Modbus golden vectors** (`config/schema/vectors/`): per covered
  mapping profile (`modbus.simulator`, `modbus.hil-simulator`) the
  register wire images — reads lifted through the C# codec
  (`RegisterDecoder`), EMS write images captured from the real
  `ModbusCommandSink` dispatch; engineering values are raw-value exact
  so `Decode(words) == value` holds without tolerances. The manifest
  schema gains the `modbus` contract (per-profile manifests, authority
  pinned to `ems`). The shipped SunSpec profile is a documented
  exclusion (no in-repo producer path).
- **SUT mode** (`docs/user/sut-field-endpoint.md`): point bess-ems at
  an external MQTT field endpoint config-only — full `Bess__Mqtt*` key
  table (incl. first-time documentation of the QoS defaults),
  `asset_id`↔`{assetId}` correspondence, cadence rule, security
  posture, and a verification recipe with distinguishable safe-stop
  causes. `deploy/compose.sut.yml` + `deploy/compose.field.yml` run
  bess-ems against a stand-in field stack over a shared external
  network; `make sut-smoke` proves the path mechanically
  (JSON-anchored good-case signal `"EventId":1701`) and runs in
  `make fullbuild`; `make sut-config-check` guards the compose pair as
  a mandatory gate.
- **`bess-field-sim` serves the full Modbus contract**: the proven
  `register_table`/`word_order` hand-mirror drift (ADR 0013 §1) is
  closed — the simulator honors both fields (separate holding/input
  register spaces, FC04 handler, word order) with loader-identical
  defaults, and the golden-vector conformance check in
  `make field-vectors-check` gates it permanently.

### Changed

- `make field-contract-check` additionally pins Modbus vector cases
  against their mapping profiles (address/type/scale/table/order with
  loader defaults resolved, value range, word count, exact
  `Decode(words) == value`) and rejects vector manifests without a
  codec gate.
- `make fullbuild` builds the simulator image once instead of three
  times (shared `simulator-build` prerequisite) and includes the SUT
  smoke.
- MQTT broker security anchoring corrected across docs and ADR 0013:
  it lives in `quality.md` §2.2.1 (RM-M4-06-FUP), not `LH-PROT-002`
  (protocol errors → quality flag).
- Helm chart `bess-ems` version/appVersion 2.1.0 → 2.2.0.

### Fixed

- `GET /health` semantics documented correctly (503 when a critical
  component is unhealthy — not blanket 200 while the host runs).

## [2.1.0] - 2026-07-13

The MQTT field contract gains its acceptance harness: structurally
compared golden vectors, published inside the schema bundle
(ADR 0013 §5.2). Native control kernel ABI stays at 0.3.0 (no native
changes in this release).

### Added

- **MQTT golden-vector suite** (`config/schema/vectors/`): two
  authority manifests — field-produced cases (telemetry
  nominal/charging, status, fault active/suppressed, command_ack
  accepted-echo) lifted from the Go field producer's real serializer
  paths, and EMS-produced cases (command with and without
  `reactive_power_kvar`) lifted from the C# wire serializer — plus the
  `golden-vector-manifest.v1` schema. Payloads are embedded as JSON
  objects and compared **structurally** (field-normative: names,
  presence, types, null-omission; not byte order). External field
  implementations (e.g. a grid-gym publisher) validate against these
  vectors instead of hand-mirroring the wire format.
- **`make field-vectors-check`** mandatory gate (repo-root Docker
  stage, wired into `make gates`, `make ci`, and hosted CI): the field
  manifest is regenerated through the producer code and compared
  structurally; the ems manifest is decoded through the simulator's
  `model.Command` with key-set, envelope-required, and value pins; the
  exact emitted message set per input is enforced (a new producer
  topic fails with "add a golden case"). `make field-vectors-refresh`
  regenerates after deliberate producer changes.

### Changed

- The schema bundle (`bess-ems-schemas-<version>.tar.gz`) now ships
  `schema/vectors/` and is packed by a single shared script
  (`scripts/pack-schema-bundle.sh`) for both `make release-assets` and
  the release workflow — the workflow's former inline packing had
  already drifted and would have shipped the bundle without vectors.
- `make field-contract-check` additionally validates the vector
  manifests and pins payload key sets against the envelope definitions
  (telemetry/command/command_ack).
- Helm chart `bess-ems` version/appVersion 2.0.0 → 2.1.0.

### Fixed

- `bess-field-sim`'s test-fixture mapping copy lifted to the shipped
  example state (it was missing `schema_version` and thus
  schema-invalid since v2.0.0; the golden-vector generator reads the
  shipped example directly).

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
