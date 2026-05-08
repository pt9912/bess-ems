/*
 * battery_control_core.h — RM-M3-01 C-ABI for the native control core.
 *
 * Stable C-ABI surface for the Constraint/Ramp (and later PID, RM-M3-13)
 * primitives that today live in the BatteryEms.Domain managed kernel.
 * This header is the single source of truth for layout, status codes
 * and reason codes that cross the .NET ↔ native boundary; the
 * P/Invoke bindings in BatteryEms.Adapters.NativeInterop (RM-M3-04)
 * mirror it 1:1.
 *
 * ABI guarantees committed at merge time:
 *   1. The numeric values of every BCC_STATUS_* and BCC_REASON_* are
 *      ABI: they MUST NOT be renumbered. New codes append. Removal
 *      requires an ABI-major bump.
 *   2. The struct field order, type, and natural-alignment layout
 *      under -m64 / x86_64 SysV is ABI. Changing field order or
 *      adding fields in the middle of a struct requires an ABI-major
 *      bump. Appending fields requires an ABI-minor bump and a
 *      separate sized-struct accessor (introduced in a later slice
 *      if needed).
 *   3. Sign convention follows architecture §4.1: discharge is
 *      positive, charge is negative. Power values are kW. SOC and
 *      temperature percentages stay in their natural units.
 *   4. Boolean-ish fields are int32_t (0/1) to avoid the platform
 *      variability of C99 _Bool / C++ bool layout.
 *   5. The header includes only standard C integer/float types
 *      (<stdint.h>). No platform headers, no struct timeval, no
 *      DateTimeOffset analogues. Time is double seconds (dt_seconds)
 *      for the ramp/PID delta; absolute clocks stay on the .NET side.
 *   6. No allocation or ownership crosses the boundary. Every struct
 *      is value-passed by pointer, the caller owns the memory, and
 *      the native side neither retains pointers nor allocates.
 *
 * Slice scope (RM-M3-01): this slice ships ONLY the header. The C++
 * implementation lands with RM-M3-02, the build wiring with
 * RM-M3-06 part 1, and the actual P/Invoke side with RM-M3-04.
 * The header therefore declares functions but does not require any
 * .so to exist yet — `gcc -c -xc -fsyntax-only` and `g++ -c -xc++
 * -fsyntax-only` are the only validations RM-M3-01 needs to pass.
 */

#ifndef BATTERY_CONTROL_CORE_H
#define BATTERY_CONTROL_CORE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------
 * ABI version
 * ------------------------------------------------------------------
 * Major: incompatible struct layout / status-code renumbering.
 * Minor: backward-compatible additions (new optional codes, appended
 *        struct fields with a sized accessor).
 * Patch: implementation-only changes that do not touch the ABI.
 *
 * The runtime version is exposed via battery_control_core_abi_version()
 * as a packed uint32_t = (major << 16) | (minor << 8) | patch.
 * 0.x means the ABI is still pre-1.0 — RM-M3-13 (PID slice) is
 * expected to bump major when the Command struct gains the PID-state
 * extension.
 */
#define BCC_ABI_VERSION_MAJOR 0
#define BCC_ABI_VERSION_MINOR 1
#define BCC_ABI_VERSION_PATCH 0

#define BCC_ABI_VERSION_PACK(major, minor, patch) \
    (((uint32_t)(major) << 16) | ((uint32_t)(minor) << 8) | (uint32_t)(patch))

#define BCC_ABI_VERSION \
    BCC_ABI_VERSION_PACK(BCC_ABI_VERSION_MAJOR, \
                         BCC_ABI_VERSION_MINOR, \
                         BCC_ABI_VERSION_PATCH)

/* ------------------------------------------------------------------
 * Status codes — ABI-stable numeric values per the M3 plan
 * (Statuscode-Baseline). Renumbering requires an ABI-major bump.
 * ------------------------------------------------------------------ */
typedef enum bcc_status {
    BCC_STATUS_OK                = 0, /* Command computed without limit. */
    BCC_STATUS_LIMITED           = 1, /* Constraint or ramp limited the output. */
    BCC_STATUS_INVALID_INPUT     = 2, /* Required field missing / out-of-range. */
    BCC_STATUS_NON_FINITE        = 3, /* NaN/Inf in input or output. */
    BCC_STATUS_NEGATIVE_DT       = 4, /* dt < 0 (ramp) or dt <= 0 (PID). */
    BCC_STATUS_UNSUPPORTED_STATE = 5  /* Valid ABI, slice cannot compute. */
} bcc_status_t;

/* ------------------------------------------------------------------
 * Reason codes — paired with the status codes above. Numeric values
 * are ABI; the names match the existing managed-side reason strings
 * 1:1 so the .NET mapping in RM-M3-04 stays trivial.
 *
 * Reason → Status mapping per the plan:
 *   WITHIN_LIMITS                                          → OK
 *   TEMPERATURE_OUT_OF_RANGE, SOC_AT_*, MAX_*_POWER,
 *   RAMP_NOT_PERMITTED, RAMP_DOWN_CLAMPED, RAMP_UP_CLAMPED → LIMITED
 *   NON_FINITE_INPUT, NON_FINITE_OUTPUT                    → NON_FINITE
 *   NEGATIVE_DT_REASON                                     → NEGATIVE_DT
 *   UNSUPPORTED_STATE_REASON                               → UNSUPPORTED_STATE
 *
 * INVALID_INPUT carries no reason in this slice — the managed
 * precheck (RM-M3-05) is responsible for surfacing that path before
 * the kernel runs.
 * ------------------------------------------------------------------ */
typedef enum bcc_reason {
    BCC_REASON_WITHIN_LIMITS               = 0,
    BCC_REASON_TEMPERATURE_OUT_OF_RANGE    = 1,
    BCC_REASON_SOC_AT_MAX_CHARGE_BLOCKED   = 2,
    BCC_REASON_SOC_AT_MIN_DISCHARGE_BLOCKED = 3,
    BCC_REASON_MAX_CHARGE_POWER            = 4,
    BCC_REASON_MAX_DISCHARGE_POWER         = 5,
    BCC_REASON_RAMP_NOT_PERMITTED          = 6,
    BCC_REASON_RAMP_DOWN_CLAMPED           = 7,
    BCC_REASON_RAMP_UP_CLAMPED             = 8,
    BCC_REASON_NON_FINITE_INPUT            = 9,
    BCC_REASON_NON_FINITE_OUTPUT           = 10,
    BCC_REASON_NEGATIVE_DT                 = 11,
    BCC_REASON_UNSUPPORTED_STATE           = 12
} bcc_reason_t;

/* ------------------------------------------------------------------
 * Mode — mirrors BatteryEms.Domain.CommandMode 1:1. The numeric
 * values match the managed enum for round-trip identity.
 * ------------------------------------------------------------------ */
typedef enum bcc_mode {
    BCC_MODE_STOP      = 0,
    BCC_MODE_IDLE      = 1,
    BCC_MODE_CHARGE    = 2,
    BCC_MODE_DISCHARGE = 3
} bcc_mode_t;

/* ------------------------------------------------------------------
 * Snapshot — input from the managed control cycle after its own
 * pre-checks. Stale-snapshot, Available==false, fault status, free
 * DataQuality reason texts and the asset-id string stay on the .NET
 * side and never enter this struct (per Native-Datenvertrag).
 * ------------------------------------------------------------------ */
typedef struct bcc_snapshot {
    double soc_percent;          /* 0..100, after managed precheck. */
    double active_power_kw;      /* discharge positive (architecture §4.1). */
    double temperature_celsius;  /* cell temperature, finite. */
} bcc_snapshot_t;

/* ------------------------------------------------------------------
 * Limits — asset and safety bounds. Power values are kW; ramp limit
 * is kW/s. min_temperature/max_temperature are the asset's safe
 * operating window — outside this range the Constraint slice forces
 * 0 kW.
 * ------------------------------------------------------------------ */
typedef struct bcc_limits {
    double max_charge_power_kw;       /* magnitude; charge is negative. */
    double max_discharge_power_kw;    /* magnitude; discharge is positive. */
    double min_soc_percent;           /* 0..100. */
    double max_soc_percent;           /* 0..100. */
    double max_ramp_kw_per_second;    /* magnitude; 0 means ramp is held. */
    double min_temperature_celsius;
    double max_temperature_celsius;
} bcc_limits_t;

/* ------------------------------------------------------------------
 * Request — optimizer / dispatch setpoint plus the context the ramp
 * limiter needs. has_previous == 0 means "first tick, skip ramp"
 * matching the managed RampLimiter contract.
 * ------------------------------------------------------------------ */
typedef struct bcc_request {
    double  target_active_power_kw;   /* signed; discharge positive. */
    double  previous_active_power_kw; /* ignored when has_previous == 0. */
    double  dt_seconds;               /* seconds since previous tick; >= 0 for ramp. */
    int32_t has_previous;             /* 0 = no previous power, 1 = previous valid. */
} bcc_request_t;

/* ------------------------------------------------------------------
 * Command — the kernel's output. status / reason_code carry
 * bcc_status / bcc_reason values respectively (declared as int32_t
 * for ABI stability — enums sometimes pick smaller storage on
 * stricter targets).
 * ------------------------------------------------------------------ */
typedef struct bcc_command {
    double  active_power_kw;     /* signed; discharge positive. */
    int32_t mode;                /* bcc_mode_t value. */
    int32_t status;              /* bcc_status_t value. */
    int32_t reason_code;         /* bcc_reason_t value. */
} bcc_command_t;

/* ------------------------------------------------------------------
 * Exported functions
 * ------------------------------------------------------------------
 * battery_control_core_abi_version()
 *   Returns the packed major/minor/patch as documented above. The
 *   .NET startup check (RM-M3-03) validates major equality and
 *   minor compatibility before any compute call is allowed.
 *
 * battery_control_core_compute()
 *   The orchestrated kernel facade. Applies the managed-equivalent
 *   Constraint then Ramp logic to the (snapshot, limits, request)
 *   triple and writes the result into out_command.
 *
 *   Return value mirrors out_command->status — duplicated so callers
 *   can branch on the status without dereferencing the output.
 *
 *   Pointer contract: snapshot, limits, request and out_command are
 *   non-null and point to fully-initialised structs. Passing NULL
 *   yields BCC_STATUS_INVALID_INPUT. The native side does not retain
 *   any of the pointers after the call returns.
 *
 *   No C++ exception leaves this function. C++-side exceptions are
 *   caught by the export wrapper and translated to the matching
 *   BCC_STATUS_* / BCC_REASON_* pair.
 * ------------------------------------------------------------------ */

uint32_t battery_control_core_abi_version(void);

bcc_status_t battery_control_core_compute(
    const bcc_snapshot_t *snapshot,
    const bcc_limits_t   *limits,
    const bcc_request_t  *request,
    bcc_command_t        *out_command);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* BATTERY_CONTROL_CORE_H */
