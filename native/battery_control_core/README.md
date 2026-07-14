# battery_control_core (RM-M3 Native Control Core)

Native C implementation of the safety-critical primitives that today
live in `BatteryEms.Domain` (`ConstraintLimiter`, `RampLimiter`,
`PidController`) plus the [RM-M5-03](../../docs/plan/planning/done/plan-RM-M5-03.md) high-frequency telemetry filter.
The .NET path stays the production reference and the parity oracle;
the native library is loaded only when explicitly configured
(`NativeControl:Enabled=true`) and falls back to managed on ABI
mismatch, missing `.so`, or any native error from a valid .NET context
(per [architecture §13.4](../../spec/architecture.md#134-fallback) + [LH-NATIVE-004](../../spec/lastenheft.md#lh-native-004--native-fehlercodes)).

## Layout

```
native/battery_control_core/
├── include/
│   └── battery_control_core.h   (RM-M3-01: stable C-ABI)
├── src/                          (C implementation: Constraint/Ramp, PID, filter)
├── tests/                        (RM-M3-08: C++ unit tests)
└── CMakeLists.txt                (RM-M3-06 part 1: build wiring)
```

`include/`, `src/`, `tests/` and `CMakeLists.txt` are the canonical
paths referenced by [`docs/user/quality.md` §5.2](../../docs/user/quality.md#52-native-abi) and the M3 plan
(coverage scope `native/battery_control_core/src/`, header path
`native/battery_control_core/include/battery_control_core.h`).

## Slice status

| Slice                          | Status |
| ------------------------------ | ------ |
| [RM-M3-01](../../docs/plan/planning/done/plan-RM-M3.md) C-ABI header          | ✅      |
| [RM-M3-02](../../docs/plan/planning/done/plan-RM-M3.md) C impl (Constraint+Ramp) | ✅      |
| [RM-M3-06](../../docs/plan/planning/done/plan-RM-M3.md) part 1 build skeleton | ✅ (CMakeLists + Dockerfile native-build stage; `make native-build` runs cmake + ctest) |
| [RM-M3-08](../../docs/plan/planning/done/plan-RM-M3.md) C++ unit tests        | ✅ (doctest suite in tests/test_compute.cpp; sanitizer/coverage/lint gates active) |
| [RM-M3-13](../../docs/plan/planning/done/plan-RM-M3.md) PID                   | ✅      |
| [RM-M5-03](../../docs/plan/planning/done/plan-RM-M5-03.md) Telemetry filter      | ✅      |

The M3 plan is closed; [RM-M5-03](../../docs/plan/planning/done/plan-RM-M5-03.md) extends the same ABI additively with
`battery_control_core_filter_telemetry`. See
`docs/plan/planning/done/plan-RM-M5-03.md` for the filter slice.

## ABI conventions (header summary)

- **Sign convention** ([LH-DOM-007](../../spec/lastenheft.md#lh-dom-007--vorzeichenkonvention)): discharge is positive,
  charge is negative. Power values are kW.
- **Status codes** are ABI-stable numeric values; renumbering
  requires an ABI-major bump.
- **Booleans** are `int32_t` 0/1 to avoid C99 `_Bool` / C++ `bool`
  layout variability.
- **No allocation crosses the boundary.** Every struct is
  pointer-passed by value, the caller owns the memory, the native
  side neither retains pointers nor allocates.
- **Header includes only `<stdint.h>`.** No platform headers, no
  time-of-day, no string ownership.

The current ABI version is `0.3.0`: `0.2.0` added PID structs and
`battery_control_core_pid_step`; `0.3.0` added the telemetry-filter
structs, filter reason codes and `battery_control_core_filter_telemetry`.
All changes are additive under ABI major `0`.

## How to compile-check the header

The header is C11 + C++17 clean under `-Wall -Wextra -pedantic`:

```bash
gcc -xc   -fsyntax-only -Wall -Wextra -pedantic -std=c11 \
    native/battery_control_core/include/battery_control_core.h
g++ -xc++ -fsyntax-only -Wall -Wextra -pedantic -std=c++17 \
    native/battery_control_core/include/battery_control_core.h
```

These checks need no toolchain beyond gcc/g++. The full library build
and native unit tests run in the Docker build stage:

```bash
make native-build
make native-lint
make native-sanitizer
make native-coverage-gate
```
