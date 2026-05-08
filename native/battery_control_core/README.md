# battery_control_core (RM-M3 Native Control Core)

Native C++ implementation of the safety-critical primitives that today
live in `BatteryEms.Domain` (`ConstraintLimiter`, `RampLimiter`,
`PidController`). The .NET path stays the production reference and the
parity oracle; the native library is loaded only when explicitly
configured (`NativeControl:Enabled=true`) and falls back to managed
on ABI mismatch, missing `.so`, or any native error from a valid
.NET context (per architecture §13.4 + LH-NATIVE-004).

## Layout

```
native/battery_control_core/
├── include/
│   └── battery_control_core.h   (RM-M3-01: stable C-ABI)
├── src/                          (RM-M3-02: C++ Constraint/Ramp impl)
├── tests/                        (RM-M3-08: C++ unit tests)
└── CMakeLists.txt                (RM-M3-06 part 1: build wiring)
```

`include/`, `src/`, `tests/` and `CMakeLists.txt` are the canonical
paths referenced by `docs/user/quality.md` §5.2 and the M3 plan
(coverage scope `native/battery_control_core/src/`, header path
`native/battery_control_core/include/battery_control_core.h`).

## Slice status

| Slice                          | Status |
| ------------------------------ | ------ |
| RM-M3-01 C-ABI header          | ✅      |
| RM-M3-02 C++ impl (Constraint+Ramp) | ⬜      |
| RM-M3-06 part 1 build skeleton | ⬜      |
| RM-M3-08 C++ unit tests        | ⬜      |
| RM-M3-13 PID                   | ⬜      |

The header is the only artefact merged in RM-M3-01. The build,
implementation, tests and Docker wiring follow as separate slices —
see `docs/plan/planning/in-progress/plan-RM-M3.md` for the full
slice/PR shape.

## ABI conventions (header summary)

- **Sign convention** (architecture §4.1): discharge is positive,
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

The current ABI version is `0.1.0`. RM-M3-13 (PID slice) is the
expected first major bump when the Command struct gains the PID
state extension.

## How to compile-check the header

The header is C11 + C++17 clean under `-Wall -Wextra -pedantic`:

```bash
gcc -xc   -fsyntax-only -Wall -Wextra -pedantic -std=c11 \
    native/battery_control_core/include/battery_control_core.h
g++ -xc++ -fsyntax-only -Wall -Wextra -pedantic -std=c++17 \
    native/battery_control_core/include/battery_control_core.h
```

These checks need no toolchain beyond gcc/g++. The full library
build lands with RM-M3-06 part 1 inside the Docker build stage.
