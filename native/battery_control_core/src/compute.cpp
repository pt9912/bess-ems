// RM-M3-02 C++ implementation of the Constraint + Ramp facade
// declared in battery_control_core.h.
//
// The native kernel mirrors the managed reference path
// (BatteryEms.Domain.ConstraintLimiter.Apply +
// BatteryEms.Domain.RampLimiter.Apply) one-to-one. PID is the
// RM-M3-13 follow-up slice and is intentionally absent here.
//
// Combination rule for Constraint + Ramp (per plan, "Constraint und
// Ramp begrenzen beide"): the final power is always the ramp
// result — even when constraint clamped to 0 the ramp limiter
// must still bound the slew rate, otherwise a temperature trip
// would slam-stop the inverter from full discharge to 0 kW in one
// tick. The final reason follows managed priority: when constraint
// limited, constraint's reason wins; when only ramp limited, ramp's
// reason wins; when neither limits, within-limits.
//
// `Available == false`, stale snapshots, fault status, asset-id
// strings and free DataQuality reason texts stay on the .NET side
// (Native-Datenvertrag baseline). The kernel sees only the four
// Snapshot/Limits/Request/Command structs.

#include "battery_control_core.h"

#include <cmath>

namespace {

constexpr int32_t to_i32(bcc_status_t s) { return static_cast<int32_t>(s); }
constexpr int32_t to_i32(bcc_reason_t r) { return static_cast<int32_t>(r); }
constexpr int32_t to_i32(bcc_mode_t   m) { return static_cast<int32_t>(m); }

bool is_finite_snapshot(const bcc_snapshot_t& s)
{
    return std::isfinite(s.soc_percent)
        && std::isfinite(s.active_power_kw)
        && std::isfinite(s.temperature_celsius);
}

bool is_finite_limits(const bcc_limits_t& l)
{
    return std::isfinite(l.max_charge_power_kw)
        && std::isfinite(l.max_discharge_power_kw)
        && std::isfinite(l.min_soc_percent)
        && std::isfinite(l.max_soc_percent)
        && std::isfinite(l.max_ramp_kw_per_second)
        && std::isfinite(l.min_temperature_celsius)
        && std::isfinite(l.max_temperature_celsius);
}

bool is_finite_request(const bcc_request_t& r)
{
    if (!std::isfinite(r.target_active_power_kw)) {
        return false;
    }
    // previous_active_power_kw and dt_seconds only matter when the
    // ramp limiter actually runs. has_previous == 0 means the M1
    // first-tick contract: managed code may legitimately pass NaN
    // there to signal "no previous", and we mirror that tolerance.
    if (r.has_previous != 0) {
        if (!std::isfinite(r.previous_active_power_kw)
            || !std::isfinite(r.dt_seconds))
        {
            return false;
        }
    }
    return true;
}

// Constraint logic — direct port of ConstraintLimiter.Apply minus
// the asset-unavailable branch (Available stays on the .NET side).
// Order matches the managed implementation exactly so parity tests
// land on the same reason for the same input.
bcc_status_t apply_constraint(
    const bcc_snapshot_t& snap,
    const bcc_limits_t&   lim,
    double                requested,
    double&               out_power,
    int32_t&              out_reason)
{
    if (snap.temperature_celsius < lim.min_temperature_celsius
        || snap.temperature_celsius > lim.max_temperature_celsius)
    {
        out_power = 0.0;
        out_reason = to_i32(BCC_REASON_TEMPERATURE_OUT_OF_RANGE);
        return BCC_STATUS_LIMITED;
    }

    if (requested < 0.0 && snap.soc_percent >= lim.max_soc_percent) {
        out_power = 0.0;
        out_reason = to_i32(BCC_REASON_SOC_AT_MAX_CHARGE_BLOCKED);
        return BCC_STATUS_LIMITED;
    }

    if (requested > 0.0 && snap.soc_percent <= lim.min_soc_percent) {
        out_power = 0.0;
        out_reason = to_i32(BCC_REASON_SOC_AT_MIN_DISCHARGE_BLOCKED);
        return BCC_STATUS_LIMITED;
    }

    if (requested < -lim.max_charge_power_kw) {
        out_power = -lim.max_charge_power_kw;
        out_reason = to_i32(BCC_REASON_MAX_CHARGE_POWER);
        return BCC_STATUS_LIMITED;
    }

    if (requested > lim.max_discharge_power_kw) {
        out_power = lim.max_discharge_power_kw;
        out_reason = to_i32(BCC_REASON_MAX_DISCHARGE_POWER);
        return BCC_STATUS_LIMITED;
    }

    out_power = requested;
    out_reason = to_i32(BCC_REASON_WITHIN_LIMITS);
    return BCC_STATUS_OK;
}

// Ramp logic — direct port of RampLimiter.Apply. Caller has
// already verified dt_seconds >= 0 and has_previous == 1.
bcc_status_t apply_ramp(
    double   previous,
    double   requested,
    double   dt_seconds,
    double   max_ramp_kw_per_second,
    double&  out_power,
    int32_t& out_reason)
{
    if (max_ramp_kw_per_second == 0.0 || dt_seconds == 0.0) {
        if (requested == previous) {
            out_power = requested;
            out_reason = to_i32(BCC_REASON_WITHIN_LIMITS);
            return BCC_STATUS_OK;
        }
        out_power = previous;
        out_reason = to_i32(BCC_REASON_RAMP_NOT_PERMITTED);
        return BCC_STATUS_LIMITED;
    }

    const double max_delta = max_ramp_kw_per_second * dt_seconds;
    const double lower = previous - max_delta;
    const double upper = previous + max_delta;

    if (requested < lower) {
        out_power = lower;
        out_reason = to_i32(BCC_REASON_RAMP_DOWN_CLAMPED);
        return BCC_STATUS_LIMITED;
    }

    if (requested > upper) {
        out_power = upper;
        out_reason = to_i32(BCC_REASON_RAMP_UP_CLAMPED);
        return BCC_STATUS_LIMITED;
    }

    out_power = requested;
    out_reason = to_i32(BCC_REASON_WITHIN_LIMITS);
    return BCC_STATUS_OK;
}

// Mode follows the architecture §4.1 sign convention: discharge
// positive, charge negative, zero = idle. Stop is reserved for
// emergency conditions decided in .NET (state machine), not by
// the kernel.
bcc_mode_t mode_from_power(double power_kw)
{
    if (power_kw > 0.0) { return BCC_MODE_DISCHARGE; }
    if (power_kw < 0.0) { return BCC_MODE_CHARGE; }
    return BCC_MODE_IDLE;
}

void fill_command(bcc_command_t& out, double power_kw,
                  bcc_status_t status, int32_t reason)
{
    out.active_power_kw = power_kw;
    out.mode            = to_i32(mode_from_power(power_kw));
    out.status          = to_i32(status);
    out.reason_code     = reason;
}

}  // namespace

extern "C" uint32_t battery_control_core_abi_version(void)
{
    return BCC_ABI_VERSION;
}

extern "C" bcc_status_t battery_control_core_compute(
    const bcc_snapshot_t* snapshot,
    const bcc_limits_t*   limits,
    const bcc_request_t*  request,
    bcc_command_t*        out_command)
{
    // Without an output struct the function has nowhere to write the
    // result; this is the only error case we cannot communicate
    // through reason_code, so fall back to the bare status return.
    if (out_command == nullptr) {
        return BCC_STATUS_INVALID_INPUT;
    }

    // Default-initialise the output so any early-return path leaves
    // it in a documented state instead of whatever the caller's
    // stack happened to hold.
    fill_command(*out_command, 0.0,
                 BCC_STATUS_INVALID_INPUT,
                 to_i32(BCC_REASON_NON_FINITE_INPUT));

    if (snapshot == nullptr || limits == nullptr || request == nullptr) {
        out_command->status      = to_i32(BCC_STATUS_INVALID_INPUT);
        out_command->reason_code = to_i32(BCC_REASON_NON_FINITE_INPUT);
        return BCC_STATUS_INVALID_INPUT;
    }

    // Catch-all so a future refactor that pulls in std::vector or
    // any throwing construct cannot let an exception cross the C
    // ABI. Today none of the helpers throw, but the wrapper makes
    // the contract from the plan ("keine Exception verlaesst den
    // Export-Pfad") enforceable in interop tests.
    try {
        if (!is_finite_snapshot(*snapshot)
            || !is_finite_limits(*limits)
            || !is_finite_request(*request))
        {
            fill_command(*out_command, 0.0,
                         BCC_STATUS_NON_FINITE,
                         to_i32(BCC_REASON_NON_FINITE_INPUT));
            return BCC_STATUS_NON_FINITE;
        }

        // negative_dt is purely a ramp concern (PID/dt-zero is
        // RM-M3-13). When has_previous == 0 the ramp limiter never
        // runs, so a negative dt is irrelevant and is not treated
        // as an error here.
        if (request->has_previous != 0 && request->dt_seconds < 0.0) {
            fill_command(*out_command, 0.0,
                         BCC_STATUS_NEGATIVE_DT,
                         to_i32(BCC_REASON_NEGATIVE_DT));
            return BCC_STATUS_NEGATIVE_DT;
        }

        double constrained_power = 0.0;
        int32_t constraint_reason = to_i32(BCC_REASON_WITHIN_LIMITS);
        const bcc_status_t constraint_status = apply_constraint(
            *snapshot, *limits, request->target_active_power_kw,
            constrained_power, constraint_reason);

        double final_power = constrained_power;
        int32_t final_reason = constraint_reason;
        bcc_status_t final_status = constraint_status;

        if (request->has_previous != 0) {
            double ramped_power = 0.0;
            int32_t ramp_reason = to_i32(BCC_REASON_WITHIN_LIMITS);
            const bcc_status_t ramp_status = apply_ramp(
                request->previous_active_power_kw,
                constrained_power,
                request->dt_seconds,
                limits->max_ramp_kw_per_second,
                ramped_power, ramp_reason);

            final_power = ramped_power;
            // Combination per plan: constraint reason wins when
            // constraint limited; otherwise ramp's outcome (which
            // may itself be ok or limited) carries through.
            if (constraint_status == BCC_STATUS_LIMITED) {
                final_reason = constraint_reason;
                final_status = BCC_STATUS_LIMITED;
            } else {
                final_reason = ramp_reason;
                final_status = ramp_status;
            }
        }

        if (!std::isfinite(final_power)) {
            fill_command(*out_command, 0.0,
                         BCC_STATUS_NON_FINITE,
                         to_i32(BCC_REASON_NON_FINITE_OUTPUT));
            return BCC_STATUS_NON_FINITE;
        }

        fill_command(*out_command, final_power, final_status, final_reason);
        return final_status;

    } catch (...) {
        // Last-resort guard. The body is exception-free today, but
        // wrapping it keeps the contract enforceable when the
        // implementation grows.
        fill_command(*out_command, 0.0,
                     BCC_STATUS_UNSUPPORTED_STATE,
                     to_i32(BCC_REASON_UNSUPPORTED_STATE));
        return BCC_STATUS_UNSUPPORTED_STATE;
    }
}
