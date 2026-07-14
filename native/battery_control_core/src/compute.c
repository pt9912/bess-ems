// RM-M3-02 (rewritten in C for RM-M3-09): Constraint + Ramp facade
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
//
// Sprachwahl C statt C++ (RM-M3-09 follow-up): die Surface ist
// rein wertbasiert (keine Allocation, keine STL, keine Exceptions),
// damit eliminiert C die Exception-Maschinerie vollständig — kein
// catch (...) mehr nötig, keine libstdc++-Abhängigkeit im Runtime
// Image, kleinere und ABI-stabilere `.so`. Die `extern "C"`-
// Deklarationen im Header bleiben unverändert; .NET-Seite
// (BatteryEms.Adapters.NativeInterop) merkt nichts vom Sprach-
// Wechsel. RM-M3-13 (PID) entscheidet später separat ob Rust für
// die wachsende State-Surface den Borrow-Checker-Win wert ist.

#include "battery_control_core.h"

#include <assert.h> // C11 static_assert macro
#include <math.h>   // isfinite, fabs (C99)
#include <stddef.h> // NULL

// RM-M3-05 review M-3: the C-ABI puts bcc_status_t / bcc_reason_t
// values into int32_t fields on the ABI output structs. Both enums today
// fit in int32_t, so any C ABI where int >= 32 bits stores them
// safely in 4 bytes. Pin the assumption with static_assert so a
// future enum that grows beyond int32_t would refuse to compile
// rather than silently truncate when round-tripped through the
// marshalling layer.
static_assert(sizeof(int32_t) >= sizeof(bcc_status_t),
              "bcc_status_t must fit into the int32_t status field of bcc_command_t");
static_assert(sizeof(int32_t) >= sizeof(bcc_reason_t),
              "bcc_reason_t must fit into the int32_t reason_code field of bcc_command_t");
static_assert(sizeof(int32_t) >= sizeof(bcc_mode_t),
              "bcc_mode_t must fit into the int32_t mode field of bcc_command_t");

static int is_finite_snapshot(const bcc_snapshot_t *s)
{
    return isfinite(s->soc_percent) && isfinite(s->active_power_kw) && isfinite(s->temperature_celsius);
}

static int is_finite_limits(const bcc_limits_t *l)
{
    return isfinite(l->max_charge_power_kw) && isfinite(l->max_discharge_power_kw) && isfinite(l->min_soc_percent) && isfinite(l->max_soc_percent) && isfinite(l->max_ramp_kw_per_second) && isfinite(l->min_temperature_celsius) && isfinite(l->max_temperature_celsius);
}

static int is_finite_request(const bcc_request_t *r)
{
    if (!isfinite(r->target_active_power_kw)) {
        return 0;
    }
    // previous_active_power_kw and dt_seconds only matter when the
    // ramp limiter actually runs. has_previous == 0 means the M1
    // first-tick contract: managed code may legitimately pass NaN
    // there to signal "no previous", and we mirror that tolerance.
    if (r->has_previous != 0) {
        if (!isfinite(r->previous_active_power_kw) || !isfinite(r->dt_seconds)) {
            return 0;
        }
    }
    return 1;
}

// Constraint logic — direct port of ConstraintLimiter.Apply minus
// the asset-unavailable branch (Available stays on the .NET side).
// Order matches the managed implementation exactly so parity tests
// land on the same reason for the same input.
static bcc_status_t apply_constraint(
    const bcc_snapshot_t *snap,
    const bcc_limits_t *lim,
    double requested,
    double *out_power,
    int32_t *out_reason)
{
    if (snap->temperature_celsius < lim->min_temperature_celsius || snap->temperature_celsius > lim->max_temperature_celsius) {
        *out_power = 0.0;
        *out_reason = (int32_t)BCC_REASON_TEMPERATURE_OUT_OF_RANGE;
        return BCC_STATUS_LIMITED;
    }

    if (requested < 0.0 && snap->soc_percent >= lim->max_soc_percent) {
        *out_power = 0.0;
        *out_reason = (int32_t)BCC_REASON_SOC_AT_MAX_CHARGE_BLOCKED;
        return BCC_STATUS_LIMITED;
    }

    if (requested > 0.0 && snap->soc_percent <= lim->min_soc_percent) {
        *out_power = 0.0;
        *out_reason = (int32_t)BCC_REASON_SOC_AT_MIN_DISCHARGE_BLOCKED;
        return BCC_STATUS_LIMITED;
    }

    if (requested < -lim->max_charge_power_kw) {
        *out_power = -lim->max_charge_power_kw;
        *out_reason = (int32_t)BCC_REASON_MAX_CHARGE_POWER;
        return BCC_STATUS_LIMITED;
    }

    if (requested > lim->max_discharge_power_kw) {
        *out_power = lim->max_discharge_power_kw;
        *out_reason = (int32_t)BCC_REASON_MAX_DISCHARGE_POWER;
        return BCC_STATUS_LIMITED;
    }

    *out_power = requested;
    *out_reason = (int32_t)BCC_REASON_WITHIN_LIMITS;
    return BCC_STATUS_OK;
}

// Ramp logic — direct port of RampLimiter.Apply. Caller has
// already verified dt_seconds >= 0 and has_previous == 1.
static bcc_status_t apply_ramp(
    double previous,
    double requested,
    double dt_seconds,
    double max_ramp_kw_per_second,
    double *out_power,
    int32_t *out_reason)
{
    if (max_ramp_kw_per_second == 0.0 || dt_seconds == 0.0) {
        if (requested == previous) {
            *out_power = requested;
            *out_reason = (int32_t)BCC_REASON_WITHIN_LIMITS;
            return BCC_STATUS_OK;
        }
        *out_power = previous;
        *out_reason = (int32_t)BCC_REASON_RAMP_NOT_PERMITTED;
        return BCC_STATUS_LIMITED;
    }

    const double max_delta = max_ramp_kw_per_second * dt_seconds;
    const double lower = previous - max_delta;
    const double upper = previous + max_delta;

    if (requested < lower) {
        *out_power = lower;
        *out_reason = (int32_t)BCC_REASON_RAMP_DOWN_CLAMPED;
        return BCC_STATUS_LIMITED;
    }

    if (requested > upper) {
        *out_power = upper;
        *out_reason = (int32_t)BCC_REASON_RAMP_UP_CLAMPED;
        return BCC_STATUS_LIMITED;
    }

    *out_power = requested;
    *out_reason = (int32_t)BCC_REASON_WITHIN_LIMITS;
    return BCC_STATUS_OK;
}

// Mode follows the architecture §4.1 sign convention: discharge
// positive, charge negative, zero = idle. Stop is reserved for
// emergency conditions decided in .NET (state machine), not by
// the kernel.
static bcc_mode_t mode_from_power(double power_kw)
{
    if (power_kw > 0.0) {
        return BCC_MODE_DISCHARGE;
    }
    if (power_kw < 0.0) {
        return BCC_MODE_CHARGE;
    }
    return BCC_MODE_IDLE;
}

static void fill_command(bcc_command_t *out, double power_kw,
                         bcc_status_t status, int32_t reason)
{
    out->active_power_kw = power_kw;
    out->mode = (int32_t)mode_from_power(power_kw);
    out->status = (int32_t)status;
    out->reason_code = reason;
}

uint32_t battery_control_core_abi_version(void)
{
    return BCC_ABI_VERSION;
}

bcc_status_t battery_control_core_compute(
    const bcc_snapshot_t *snapshot,
    const bcc_limits_t *limits,
    const bcc_request_t *request,
    bcc_command_t *out_command)
{
    // Without an output struct the function has nowhere to write the
    // result; this is the only error case we cannot communicate
    // through reason_code, so fall back to the bare status return.
    if (out_command == NULL) {
        return BCC_STATUS_INVALID_INPUT;
    }

    // Default-initialise the output so any early-return path leaves
    // it in a documented state instead of whatever the caller's
    // stack happened to hold.
    fill_command(out_command, 0.0,
                 BCC_STATUS_INVALID_INPUT,
                 (int32_t)BCC_REASON_NON_FINITE_INPUT);

    if (snapshot == NULL || limits == NULL || request == NULL) {
        out_command->status = (int32_t)BCC_STATUS_INVALID_INPUT;
        out_command->reason_code = (int32_t)BCC_REASON_NON_FINITE_INPUT;
        return BCC_STATUS_INVALID_INPUT;
    }

    // C has no exceptions, so the C++-era catch(...) wrapper around
    // the body is gone — none of the helpers below can raise a
    // language-level error, only return status codes. The non-finite
    // output guard from the C++ version is also gone: with all
    // inputs pre-checked finite and no transcendentals on the
    // critical path, the constraint+ramp arithmetic cannot produce
    // ±Inf or NaN from finite inputs. RM-M3-13 (PID with sin/exp-
    // style state) will reintroduce both an output-finite check and
    // (if needed) a panic translation, with their own coverage at
    // that time.

    if (!is_finite_snapshot(snapshot) || !is_finite_limits(limits) || !is_finite_request(request)) {
        fill_command(out_command, 0.0,
                     BCC_STATUS_NON_FINITE,
                     (int32_t)BCC_REASON_NON_FINITE_INPUT);
        return BCC_STATUS_NON_FINITE;
    }

    // negative_dt is purely a ramp concern (PID/dt-zero is
    // RM-M3-13). When has_previous == 0 the ramp limiter never
    // runs, so a negative dt is irrelevant and is not treated
    // as an error here.
    if (request->has_previous != 0 && request->dt_seconds < 0.0) {
        fill_command(out_command, 0.0,
                     BCC_STATUS_NEGATIVE_DT,
                     (int32_t)BCC_REASON_NEGATIVE_DT);
        return BCC_STATUS_NEGATIVE_DT;
    }

    double constrained_power = 0.0;
    int32_t constraint_reason = (int32_t)BCC_REASON_WITHIN_LIMITS;
    const bcc_status_t constraint_status = apply_constraint(
        snapshot, limits, request->target_active_power_kw,
        &constrained_power, &constraint_reason);

    double final_power = constrained_power;
    int32_t final_reason = constraint_reason;
    bcc_status_t final_status = constraint_status;

    if (request->has_previous != 0) {
        double ramped_power = 0.0;
        int32_t ramp_reason = (int32_t)BCC_REASON_WITHIN_LIMITS;
        const bcc_status_t ramp_status = apply_ramp(
            request->previous_active_power_kw,
            constrained_power,
            request->dt_seconds,
            limits->max_ramp_kw_per_second,
            &ramped_power, &ramp_reason);

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

    fill_command(out_command, final_power, final_status, final_reason);
    return final_status;
}

/* ------------------------------------------------------------------
 * RM-M3-13 PID kernel (battery_control_core_pid_step). Mirrors
 * BatteryEms.Domain.PidController.Step one-to-one — same operation
 * order, same anti-windup direction logic, same deadband semantics
 * (P/D suppressed, integrator held, previous_error preserved across
 * the band so the derivative on exit measures the actual change).
 * The managed reference throws on dt <= 0, non-finite inputs, the
 * integrator overflowing or the pre-clamp output going non-finite;
 * the native side maps those onto deterministic status/reason
 * codes so the routing layer can branch without exception
 * propagation across the C-ABI.
 * ------------------------------------------------------------------ */

static void fill_pid_command(bcc_pid_command_t *out,
                             double output,
                             const bcc_pid_state_t *next_state,
                             bcc_status_t status,
                             bcc_reason_t reason,
                             int was_clamped,
                             int was_integral_frozen)
{
    out->output = output;
    out->next_integral = next_state->integral;
    out->next_previous_error = next_state->previous_error;
    out->status = (int32_t)status;
    out->reason_code = (int32_t)reason;
    out->was_clamped = was_clamped ? 1 : 0;
    out->was_integral_frozen = was_integral_frozen ? 1 : 0;
}

static int is_finite_pid_state(const bcc_pid_state_t *s)
{
    return isfinite(s->integral) && isfinite(s->previous_error);
}

static int is_finite_pid_options(const bcc_pid_options_t *o)
{
    return isfinite(o->kp) && isfinite(o->ki) && isfinite(o->kd) && isfinite(o->output_min) && isfinite(o->output_max) && isfinite(o->deadband_absolute);
}

static int is_finite_pid_input(const bcc_pid_input_t *i)
{
    return isfinite(i->setpoint) && isfinite(i->measurement) && isfinite(i->dt_seconds);
}

/* Validate state/options/input together. Returns BCC_STATUS_OK when
 * pid_step may proceed; on any rejection returns the matching status
 * and writes the reason into *out_reason so the caller's error fill
 * is a single line. Splitting this out keeps pid_step's cognitive
 * complexity inside the readability-function-cognitive-complexity
 * threshold (20) — the function would otherwise stack ~6 guard
 * branches before any real work happens. */
static bcc_status_t validate_pid_inputs(
    const bcc_pid_state_t *state,
    const bcc_pid_options_t *options,
    const bcc_pid_input_t *input,
    bcc_reason_t *out_reason)
{
    if (!is_finite_pid_state(state) || !is_finite_pid_options(options) || !is_finite_pid_input(input)) {
        *out_reason = BCC_REASON_NON_FINITE_INPUT;
        return BCC_STATUS_NON_FINITE;
    }
    if (options->output_min > options->output_max || options->deadband_absolute < 0.0) {
        *out_reason = BCC_REASON_PID_INVALID_OPTIONS;
        return BCC_STATUS_INVALID_INPUT;
    }
    if (options->anti_windup_mode != (int32_t)BCC_PID_ANTI_WINDUP_CONDITIONAL_INTEGRATION) {
        *out_reason = BCC_REASON_UNSUPPORTED_STATE;
        return BCC_STATUS_UNSUPPORTED_STATE;
    }
    if (input->dt_seconds <= 0.0) {
        *out_reason = BCC_REASON_NEGATIVE_DT;
        return BCC_STATUS_NEGATIVE_DT;
    }
    *out_reason = BCC_REASON_WITHIN_LIMITS;
    return BCC_STATUS_OK;
}

bcc_status_t battery_control_core_pid_step(
    const bcc_pid_state_t *state,
    const bcc_pid_options_t *options,
    const bcc_pid_input_t *input,
    bcc_pid_command_t *out_command)
{
    if (out_command == NULL) {
        return BCC_STATUS_INVALID_INPUT;
    }

    /* Default the output struct so any early return leaves the
     * caller looking at a documented state instead of stack
     * residue. The "next" state mirrors the input state on every
     * error path — the managed reference threads the unchanged
     * input state when it throws, so callers staying on the
     * Managed-Fallback after a native error will not see a state
     * regression either way. */
    const bcc_pid_state_t zero_state = {0.0, 0.0};
    fill_pid_command(out_command, 0.0, &zero_state,
                     BCC_STATUS_INVALID_INPUT,
                     BCC_REASON_NON_FINITE_INPUT,
                     /*was_clamped=*/0,
                     /*was_integral_frozen=*/0);

    if (state == NULL || options == NULL || input == NULL) {
        return BCC_STATUS_INVALID_INPUT;
    }

    bcc_reason_t guard_reason = BCC_REASON_WITHIN_LIMITS;
    const bcc_status_t guard_status =
        validate_pid_inputs(state, options, input, &guard_reason);
    if (guard_status != BCC_STATUS_OK) {
        fill_pid_command(out_command, 0.0, state,
                         guard_status, guard_reason, 0, 0);
        return guard_status;
    }

    const double dt = input->dt_seconds;
    const double raw_error = input->setpoint - input->measurement;
    const int in_deadband = options->deadband_absolute > 0.0 && fabs(raw_error) < options->deadband_absolute;
    const double effective_error = in_deadband ? 0.0 : raw_error;

    const double p = options->kp * effective_error;
    const double d = in_deadband
                         ? 0.0
                         : options->kd * (effective_error - state->previous_error) / dt;

    const double integrator_step = options->ki * effective_error;
    const double candidate_integral = state->integral + (integrator_step * dt);
    if (!isfinite(candidate_integral)) {
        /* Match the managed reference's OverflowException semantics:
         * the operating point is outside any reasonable regime, do
         * not let anti-windup mask the overflow by silently freezing
         * to the (still-finite) prior integrator. */
        fill_pid_command(out_command, 0.0, state,
                         BCC_STATUS_NON_FINITE,
                         BCC_REASON_PID_INTEGRATOR_OVERFLOW,
                         0, 0);
        return BCC_STATUS_NON_FINITE;
    }

    const double candidate_pre_clamp = p + candidate_integral + d;

    /* Anti-windup: freeze the integrator when the candidate output
     * is past the saturation bound and continuing to integrate
     * would not relieve it. Direction is the sign of integrator_step
     * (= ki·error), not the error sign — so a negative-Ki
     * configuration with positive error correctly does NOT freeze
     * when the integrator is decrementing toward relief.
     *
     * Both saturation arms share the same freeze body (hold integral,
     * set the flag), so they collapse into a single combined
     * condition; the check `bugprone-branch-clone` flags the
     * if/else-if duplicate otherwise. */
    const int freeze_high =
        candidate_pre_clamp > options->output_max && integrator_step > 0.0;
    const int freeze_low =
        candidate_pre_clamp < options->output_min && integrator_step < 0.0;
    int was_integral_frozen = 0;
    double chosen_integral = candidate_integral;
    if (freeze_high || freeze_low) {
        chosen_integral = state->integral;
        was_integral_frozen = 1;
    }

    const double pre_clamp = p + chosen_integral + d;
    if (!isfinite(pre_clamp)) {
        /* Catches a non-finite from the P or D side (the integrator
         * is checked above). The pre-clamp NON_FINITE_OUTPUT path
         * matches the managed reference's second OverflowException
         * site. */
        fill_pid_command(out_command, 0.0, state,
                         BCC_STATUS_NON_FINITE,
                         BCC_REASON_NON_FINITE_OUTPUT,
                         0, 0);
        return BCC_STATUS_NON_FINITE;
    }

    /* Clamp to [output_min, output_max]. */
    double output = pre_clamp;
    bcc_reason_t reason = BCC_REASON_WITHIN_LIMITS;
    int was_clamped = 0;
    if (pre_clamp > options->output_max) {
        output = options->output_max;
        reason = BCC_REASON_PID_OUTPUT_CLAMPED_HIGH;
        was_clamped = 1;
    } else if (pre_clamp < options->output_min) {
        output = options->output_min;
        reason = BCC_REASON_PID_OUTPUT_CLAMPED_LOW;
        was_clamped = 1;
    }

    /* Preserve previous_error across a deadband transition so the
     * derivative on the first out-of-band tick measures the actual
     * change, not a spike against zero. */
    const bcc_pid_state_t next_state = {
        .integral = chosen_integral,
        .previous_error = in_deadband ? state->previous_error : effective_error,
    };

    const bcc_status_t status = was_clamped ? BCC_STATUS_LIMITED : BCC_STATUS_OK;
    fill_pid_command(out_command, output, &next_state,
                     status, reason,
                     was_clamped, was_integral_frozen);
    return status;
}

/* ------------------------------------------------------------------
 * RM-M5-03 high-frequency telemetry filter.
 * ------------------------------------------------------------------ */

static void fill_filter_output(
    bcc_telemetry_filter_output_t *out,
    double filtered_soc_percent,
    double filtered_active_power_kw,
    double filtered_temperature_celsius,
    bcc_status_t status,
    bcc_reason_t reason,
    int drift_detected,
    int initialized)
{
    out->filtered_soc_percent = filtered_soc_percent;
    out->filtered_active_power_kw = filtered_active_power_kw;
    out->filtered_temperature_celsius = filtered_temperature_celsius;
    out->status = (int32_t)status;
    out->reason_code = (int32_t)reason;
    out->drift_detected = drift_detected ? 1 : 0;
    out->initialized = initialized ? 1 : 0;
}

static int is_finite_filter_state(const bcc_telemetry_filter_state_t *s)
{
    if (s->initialized == 0) {
        return 1;
    }
    return isfinite(s->filtered_soc_percent) && isfinite(s->filtered_active_power_kw) && isfinite(s->filtered_temperature_celsius);
}

static int is_finite_filter_options(const bcc_telemetry_filter_options_t *o)
{
    return isfinite(o->alpha) && isfinite(o->max_soc_delta_percent) && isfinite(o->max_power_delta_kw) && isfinite(o->max_temperature_delta_celsius) && isfinite(o->min_sample_period_seconds) && isfinite(o->max_sample_period_seconds);
}

static int is_finite_filter_input(const bcc_telemetry_filter_input_t *i)
{
    return isfinite(i->soc_percent) && isfinite(i->active_power_kw) && isfinite(i->temperature_celsius) && isfinite(i->dt_seconds);
}

static bcc_status_t validate_filter_inputs(
    const bcc_telemetry_filter_state_t *state,
    const bcc_telemetry_filter_options_t *options,
    const bcc_telemetry_filter_input_t *input,
    bcc_reason_t *out_reason)
{
    if (!is_finite_filter_state(state) || !is_finite_filter_options(options) || !is_finite_filter_input(input)) {
        *out_reason = BCC_REASON_NON_FINITE_INPUT;
        return BCC_STATUS_NON_FINITE;
    }
    if (options->alpha < 0.0 || options->alpha > 1.0 || options->max_soc_delta_percent < 0.0 || options->max_power_delta_kw < 0.0 || options->max_temperature_delta_celsius < 0.0 || options->min_sample_period_seconds < 0.0 || options->max_sample_period_seconds < options->min_sample_period_seconds) {
        *out_reason = BCC_REASON_FILTER_INVALID_OPTIONS;
        return BCC_STATUS_INVALID_INPUT;
    }
    if (input->dt_seconds < options->min_sample_period_seconds || input->dt_seconds > options->max_sample_period_seconds) {
        *out_reason = BCC_REASON_FILTER_SAMPLE_PERIOD;
        return BCC_STATUS_INVALID_INPUT;
    }
    *out_reason = BCC_REASON_WITHIN_LIMITS;
    return BCC_STATUS_OK;
}

static int filter_drift_detected(
    const bcc_telemetry_filter_state_t *state,
    const bcc_telemetry_filter_options_t *options,
    const bcc_telemetry_filter_input_t *input)
{
    if (state->initialized == 0) {
        return 0;
    }
    return fabs(input->soc_percent - state->filtered_soc_percent) > options->max_soc_delta_percent || fabs(input->active_power_kw - state->filtered_active_power_kw) > options->max_power_delta_kw || fabs(input->temperature_celsius - state->filtered_temperature_celsius) > options->max_temperature_delta_celsius;
}

static double filter_step(double previous, double measurement, double alpha)
{
    return (alpha * measurement) + ((1.0 - alpha) * previous);
}

bcc_status_t battery_control_core_filter_telemetry(
    const bcc_telemetry_filter_state_t *state,
    const bcc_telemetry_filter_options_t *options,
    const bcc_telemetry_filter_input_t *input,
    bcc_telemetry_filter_output_t *out_output)
{
    if (out_output == NULL) {
        return BCC_STATUS_INVALID_INPUT;
    }

    fill_filter_output(out_output, 0.0, 0.0, 0.0,
                       BCC_STATUS_INVALID_INPUT,
                       BCC_REASON_NON_FINITE_INPUT,
                       0, 0);

    if (state == NULL || options == NULL || input == NULL) {
        return BCC_STATUS_INVALID_INPUT;
    }

    bcc_reason_t guard_reason = BCC_REASON_WITHIN_LIMITS;
    const bcc_status_t guard_status =
        validate_filter_inputs(state, options, input, &guard_reason);
    if (guard_status != BCC_STATUS_OK) {
        const int preserve_state = guard_status != BCC_STATUS_NON_FINITE && state->initialized != 0;
        fill_filter_output(out_output,
                           preserve_state ? state->filtered_soc_percent : 0.0,
                           preserve_state ? state->filtered_active_power_kw : 0.0,
                           preserve_state ? state->filtered_temperature_celsius : 0.0,
                           guard_status,
                           guard_reason,
                           guard_reason == BCC_REASON_FILTER_TELEMETRY_DRIFT,
                           preserve_state);
        return guard_status;
    }

    if (filter_drift_detected(state, options, input)) {
        fill_filter_output(out_output,
                           state->filtered_soc_percent,
                           state->filtered_active_power_kw,
                           state->filtered_temperature_celsius,
                           BCC_STATUS_INVALID_INPUT,
                           BCC_REASON_FILTER_TELEMETRY_DRIFT,
                           1,
                           state->initialized);
        return BCC_STATUS_INVALID_INPUT;
    }

    const int was_initialized = state->initialized != 0;
    const double previous_soc = was_initialized
                                    ? state->filtered_soc_percent
                                    : input->soc_percent;
    const double previous_power = was_initialized
                                      ? state->filtered_active_power_kw
                                      : input->active_power_kw;
    const double previous_temperature = was_initialized
                                            ? state->filtered_temperature_celsius
                                            : input->temperature_celsius;

    fill_filter_output(out_output,
                       filter_step(previous_soc, input->soc_percent, options->alpha),
                       filter_step(previous_power, input->active_power_kw, options->alpha),
                       filter_step(previous_temperature, input->temperature_celsius, options->alpha),
                       BCC_STATUS_OK,
                       BCC_REASON_WITHIN_LIMITS,
                       0,
                       1);
    return BCC_STATUS_OK;
}
