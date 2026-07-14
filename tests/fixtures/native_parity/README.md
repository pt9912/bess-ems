# Native ↔ .NET Control Kernel Parity Replay Dataset

[RM-M3-10](../../../docs/plan/planning/done/plan-RM-M3.md) versioned golden dataset. Every case is a `(snapshot, limits,
request, expected)` tuple that must produce **identical** commands when
fed through:

1. The native control core (`libbattery_control_core.so`,
   `battery_control_core_compute`).
2. The managed reference (`BatteryEms.Application.Control.ManagedControlKernel`).

`BatteryEms.NativeInterop.IntegrationTests.NativeParityReplayTests`
loads `cases.v*.json` at test time, drives both kernels with the
fixture, and asserts:

- `native.active_power_kw == managed.active_power_kw` within
  `tolerance_active_power_kw` (default 1e-12 — in practice bit-exact
  on x86_64 because both kernels execute the same FP sequence on the
  same `double` values).
- `native.reason == managed.reason` (string equality after
  `NativeFallbackControlKernel.MapReason` translates the BCC reason
  code).
- `native.was_limited == managed.was_limited`.
- Both equal the fixture's `expected.*` (catches the case where both
  kernels drift in the same direction silently).

## Versioning

The file name carries the schema version (`cases.v1.json`). A future
incompatible schema change ships a new file (e.g. `cases.v2.json`)
alongside the old one and a parallel test class — the old file must
keep validating against the historical algorithm even after the new
one lands. Bumping the schema in-place is a parity-history erase and
is forbidden.

`schema_version` inside the JSON pins the format the loader expects
to see; mismatch is a hard fail at test time, not a soft warning.

## Scope and exclusions

This dataset is deliberately limited to cases the production
managed-precheck pipeline accepts. Per the [RM-M3-10](../../../docs/plan/planning/done/plan-RM-M3.md) plan entry,
the following are **not** parity cases:

- Negative `dt` — managed control raises before reaching the kernel.
- Non-finite snapshot / limit / request fields — managed precheck
  rejects, the kernel is never called.
- Stale snapshot, `Available == false`, fault-status, `ValidUntil`
  expiration — purely managed-control concerns.

Those paths are pinned in `BatteryEms.Application.Tests` (managed
side) and `BatteryEms.NativeInterop.IntegrationTests`
(`NativeAbiNegativeTests`, native-only contract). Mixing them into
parity would model parity on inputs that production never reaches —
a contract distortion the plan explicitly forbids.

## Schema

```jsonc
{
  "schema_version": "v1",
  "tolerance_active_power_kw": 1e-12,
  "asset_baseline": "free-form description of the asset envelope",
  "cases": [
    {
      "name": "stable-id-used-in-test-output",
      "description": "human-readable purpose / what branch this hits",
      "snapshot": {
        "soc_percent": 50,
        "active_power_kw": 0,
        "temperature_celsius": 22
      },
      "limits": {
        "max_charge_power_kw": 50,
        "max_discharge_power_kw": 50,
        "min_soc_percent": 10,
        "max_soc_percent": 90,
        "max_ramp_kw_per_second": 25,
        "min_temperature_celsius": -20,
        "max_temperature_celsius": 55
      },
      "request": {
        "target_active_power_kw": 10,
        "previous_active_power_kw": null,    // null => has_previous = 0
        "dt_seconds": 1.0
      },
      "expected": {
        "active_power_kw": 10,
        "reason": "within-limits",            // managed reason string
        "was_limited": false,
        "mode": "discharge"                   // stop | idle | charge | discharge
      }
    }
  ]
}
```

Reason strings mirror `BatteryEms.Domain.LimitResult` and the BCC
reason mapping in `NativeFallbackControlKernel.MapReason` — every
value used in `expected.reason` must be a key in that table.

## Adding a case

1. Identify the branch / boundary the case is meant to pin (the
   `description` is the contract — write it like a sentence the
   reviewer can verify by reading `compute.c` + `ConstraintLimiter.cs`
   + `RampLimiter.cs`).
2. Compute `expected.*` by hand from the algorithm — do **not** copy
   what the current code produces, because that erases drift detection.
3. Run `make test-native-interop`; if both kernels match `expected`
   the case lands; if either disagrees, the case caught a real bug or
   a deliberate algorithm change that needs a separate PR.

## Excluded path: PID

PID is [RM-M3-13](../../../docs/plan/planning/done/plan-RM-M3.md) scope. When that slice lands it ships `cases.v2.json`
(or extends `cases.v1.json` if the schema is forward-compatible — to
be decided in the [RM-M3-13](../../../docs/plan/planning/done/plan-RM-M3.md) plan).
