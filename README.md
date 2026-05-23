# bess-ems

*English | [Deutsch](README.de.md)*

Battery Energy Management System (EMS) for Battery Energy Storage Systems (BESS).

`bess-ems` plans, monitors and controls battery storage systems, taking into
account market mechanisms, real-time measurement data, technical limits and
safety requirements. The system is modular, containerised, and designed so that
market optimisation and technical control are strictly separated.

> **Status:** M1–M6 are complete; `v1.0.0` is released
> ([Release](https://github.com/pt9912/bess-ems/releases/tag/v1.0.0),
> [Roadmap](docs/plan/planning/in-progress/roadmap.md),
> [Release process](docs/user/releasing.md))

---

## Core idea

```text
Measurements
→ Validation
→ Snapshot
→ State Machine
→ Market / schedule resolution
→ Ancillary-services prioritisation (if enabled)
→ Constraint Limiter
→ Ramp Limiter
→ Command
→ Protocol Adapter
```

The central architectural rule:

> Optimisation produces desired values.
> The technical control loop decides what can be operated safely.

Optimisation results are never sent directly to battery systems; they are
always routed through the State Machine, Constraint Limiter and Ramp Limiter.

---

## Functional scope

The system addresses, among other things:

- Day-ahead market, intraday market, ancillary services
- Charge and discharge control with power ramps
- State machines for operational and safety states
- PID control, MPC / state-space models (planned)
- LP / MILP / heuristic optimisation via a pluggable solver interface
- Field communication via Modbus TCP, MQTT and (post-MVP) OPC-UA
- Near-real-time measurement processing with data-quality assessment
- Persistence, auditability and monitoring

---

## Technology stack

| Area                      | Technology                                       |
| ------------------------- | ------------------------------------------------ |
| Main platform             | C# / .NET 10                                     |
| API                       | ASP.NET Core Minimal API                         |
| Performance-critical core | C / C++ (optional native core via C ABI)         |
| Persistence (MVP)         | PostgreSQL via Dapper + Npgsql                   |
| Messaging / fieldbus      | MQTTnet, FluentModbus; OPC-UA post-MVP           |
| Simulator                 | Go field simulator for Modbus/MQTT               |
| Operations / gates        | Linux, Docker, Dockerfile stages, Makefile       |
| Observability             | Structured logs; Prometheus/OTel incrementally   |

---

## Architecture layers

Detailed definitions, responsibilities and boundaries are documented in
[`spec/architecture.md`](spec/architecture.md).

- Domain Layer
- Market Layer
- Optimization Layer
- Realtime Layer
- Control Layer
- Protocol Adapter Layer
- Infrastructure Layer
- API Layer
- Native Core Layer (optional)

Protocol adapters contain only transformations between external signals and
internal models — no market, optimisation or control decisions.

---

## Sign convention

Active power at the battery storage system:

- `> 0 kW` → discharging / feeding in
- `< 0 kW` → charging / drawing
- `0 kW` → no active charge or discharge command

This convention is used consistently internally. Deviating device conventions
are mapped exclusively inside protocol adapters.

---

## Safety principles

Safe state in the MVP means:

- no active charge or discharge command
- no forwarding of stale or invalid commands
- emission of a `0 kW` command, an explicit stop command, or disabling of the
  output
- a persisted and logged reason

Priority order in the control loop:

1. Emergency Stop
2. Battery, inverter and grid limits
3. Ancillary-services activation
4. Binding market obligations
5. Intraday schedule
6. Day-ahead schedule
7. Local optimisation

Software-side stop and safety functions do not replace a hardware emergency
stop, BMS protection mechanisms or vendor-specific inverter protections. Hard
real-time and certification-relevant functions must be implemented outside the
Docker-based EMS.

The Go field simulator under `simulators/bess-field-sim` is a test tool: MQTT
runs there deliberately anonymously and in plaintext over `tcp://` without TLS
or credentials. This is intended only for local Mosquitto / CI simulations and
not for production brokers.

---

## MVP scope

### In scope for the MVP

- C# / .NET Worker Service
- Domain model, Realtime Snapshot Store, State Machine
- Constraint Limiter, Ramp Limiter
- Optimisation interface (without a production solver)
- MQTT and Modbus-TCP adapters
- Static schedule import, simple day-ahead schedule tracking
- PostgreSQL persistence
- Health, status, command, schedule and operator-stop API
  (write access with AuthN/AuthZ and audit log)
- Structured logs
- Docker Compose
- Unit tests for core logic


---

## Repository structure

The authoritative module and layer structure is described in
[`spec/architecture.md`](spec/architecture.md). The most important entry points
in the repository are:

- `BatteryEms.sln` — .NET solution
- `src/hexagon/` — domain and application core
- `src/adapters/` — driving adapters (API, worker) and driven adapters
  (Modbus, MQTT, persistence, optimisation, telemetry)
- `src/infrastructure/` — infrastructure implementations
- `tests/` — architecture, unit and integration tests
- `config/` — JSON schemas and example configurations
- `simulators/bess-field-sim/` — Go field simulator
- `docs/plan/` — roadmap, active and pending detailed plans


---

## Installation

Container image (signed via Cosign keyless, SBOM-attested):

```bash
docker pull ghcr.io/pt9912/bess-ems:v1.0.0
# or :1.0.0 / :latest
```

Helm chart, source tarball, native `libbattery_control_core.so` plus headers,
SBOM and `SHA256SUMS` are published as release assets:
[Release v1.0.0](https://github.com/pt9912/bess-ems/releases/tag/v1.0.0).

Verifying the image signature:

```bash
cosign verify ghcr.io/pt9912/bess-ems:v1.0.0 \
  --certificate-identity-regexp '^https://github.com/pt9912/bess-ems/.*' \
  --certificate-oidc-issuer 'https://token.actions.githubusercontent.com'
```

Full release-process documentation in
[`docs/user/releasing.md`](docs/user/releasing.md).

---

## Local gates

The active gate targets are visible in the `Makefile`:

```bash
make help
make lint
make arch-check
make test
make test-safety
make coverage-gate
make simulator-test
make test-integration
```

---

## Development approach

V-model–style requirements structure with identifiers and traceability from
requirement → design → implementation → test. Requirements are uniquely
referenceable in the specification with prefixes (e.g. `LH-CTRL-002`,
`LH-SAFE-001`).

---

## Further reading

- [`spec/lastenheft.md`](spec/lastenheft.md) — complete requirements,
  acceptance criteria, traceability tables, risks and open items
- [`spec/architecture.md`](spec/architecture.md) — architecture design:
  layers, modules, data flow, native-core strategy, traceability
- [`docs/plan/planning/in-progress/roadmap.md`](docs/plan/planning/in-progress/roadmap.md) —
  milestones M1–M6 with deliverables and LH traceability
- [`docs/user/releasing.md`](docs/user/releasing.md) — release process,
  tag contract, workflow steps, rollback
- [`docs/user/quality.md`](docs/user/quality.md) — binding quality and
  measurement paths (static analysis C# / .NET + C / C++, tests, coverage,
  contract gates, native parity, CI/release)
- [`docs/archive/idea.md`](docs/archive/idea.md) — historical native-core
  idea sketch, non-normative

---

## Related projects

- **[grid-gym](https://github.com/pt9912/grid-gym)** — deterministic
  simulation, replay and fault-injection platform for smart-grid and EMS
  systems; a natural test and HIL companion for `bess-ems`.
- **[grid-guide](https://github.com/pt9912/grid-guide)** — local desktop
  assistant for preparing grid-connection and authority applications for PV,
  storage and generation plants; covers the installation and permitting stage
  that precedes EMS operation.

---

## License

Released under the [MIT License](LICENSE).
