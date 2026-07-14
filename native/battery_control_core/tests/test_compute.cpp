// RM-M3-08 C++ unit tests for the Constraint + Ramp facade.
//
// doctest replaces the hand-rolled assertion harness from RM-M3-02:
// each TEST_CASE is independently discovered, failures point at the
// exact line, and the binary is the seam RM-M3-09 hooks sanitizers
// and coverage onto. The framework header is FetchContent-pinned
// in CMakeLists.txt (URL_HASH SHA256), so a tampered or moved
// upstream artefact fails the build instead of silently swapping
// the test infrastructure.
//
// Coverage target: every BCC_STATUS_* and every BCC_REASON_* the
// kernel can emit from a validated input is exercised at least
// once. UNSUPPORTED_STATE is reachable only via the catch-all in
// `battery_control_core_compute` and would require a thrown C++
// exception inside the body — none of today's helpers throw, so
// that path stays covered by code review (and by the
// static_assert that pins enum widths in compute.cpp). PID lands
// with RM-M3-13.

#define DOCTEST_CONFIG_IMPLEMENT_WITH_MAIN
#include "doctest.h"

#include "battery_control_core.h"

#include <cmath>
#include <limits>

namespace
{

// Reference single-bess asset matching the .NET integration tests
// and the parity fixtures in BatteryEms.NativeInterop.IntegrationTests:
// ±50 kW, SOC band 10..90 %, ramp 25 kW/s, temperature −20..55 °C.
bcc_limits_t make_limits()
{
    bcc_limits_t l{};
    l.max_charge_power_kw = 50.0;
    l.max_discharge_power_kw = 50.0;
    l.min_soc_percent = 10.0;
    l.max_soc_percent = 90.0;
    l.max_ramp_kw_per_second = 25.0;
    l.min_temperature_celsius = -20.0;
    l.max_temperature_celsius = 55.0;
    return l;
}

bcc_snapshot_t make_snapshot(double soc = 50.0, double power = 0.0,
                             double temp = 22.0)
{
    bcc_snapshot_t s{};
    s.soc_percent = soc;
    s.active_power_kw = power;
    s.temperature_celsius = temp;
    return s;
}

bcc_request_t make_request(double target, double previous = 0.0,
                           double dt = 1.0, int32_t has_previous = 1)
{
    bcc_request_t r{};
    r.target_active_power_kw = target;
    r.previous_active_power_kw = previous;
    r.dt_seconds = dt;
    r.has_previous = has_previous;
    return r;
}

} // namespace

TEST_CASE("within-limits OK with discharge mode and within-limits reason")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(/*target=*/10.0, 0.0, 1.0,
                                  /*has_previous=*/0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_OK);

    CHECK(cmd.status == static_cast<int32_t>(BCC_STATUS_OK));
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_WITHIN_LIMITS));
    CHECK(cmd.active_power_kw == doctest::Approx(10.0).epsilon(1e-12));
    CHECK(cmd.mode == static_cast<int32_t>(BCC_MODE_DISCHARGE));
}

TEST_CASE("max-charge-power limited at -max_charge with charge mode")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(-100.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_MAX_CHARGE_POWER));
    CHECK(cmd.active_power_kw == doctest::Approx(-50.0).epsilon(1e-12));
    CHECK(cmd.mode == static_cast<int32_t>(BCC_MODE_CHARGE));
}

TEST_CASE("max-discharge-power limited at +max_discharge with discharge mode")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(100.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_MAX_DISCHARGE_POWER));
    CHECK(cmd.active_power_kw == doctest::Approx(50.0).epsilon(1e-12));
    CHECK(cmd.mode == static_cast<int32_t>(BCC_MODE_DISCHARGE));
}

TEST_CASE("soc at max blocks charge, idle mode")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot(/*soc=*/95.0);
    const auto req = make_request(-30.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_SOC_AT_MAX_CHARGE_BLOCKED));
    CHECK(cmd.active_power_kw == doctest::Approx(0.0).epsilon(1e-12));
    CHECK(cmd.mode == static_cast<int32_t>(BCC_MODE_IDLE));
}

TEST_CASE("soc at min blocks discharge, idle mode")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot(/*soc=*/5.0);
    const auto req = make_request(30.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_SOC_AT_MIN_DISCHARGE_BLOCKED));
    CHECK(cmd.active_power_kw == doctest::Approx(0.0).epsilon(1e-12));
    CHECK(cmd.mode == static_cast<int32_t>(BCC_MODE_IDLE));
}

TEST_CASE("temperature above max forces 0 kW with temperature reason")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot(50.0, 0.0, /*temp=*/70.0);
    const auto req = make_request(20.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_TEMPERATURE_OUT_OF_RANGE));
    CHECK(cmd.active_power_kw == doctest::Approx(0.0).epsilon(1e-12));
}

TEST_CASE("temperature below min forces 0 kW with temperature reason")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot(50.0, 0.0, /*temp=*/-30.0);
    const auto req = make_request(20.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_TEMPERATURE_OUT_OF_RANGE));
    CHECK(cmd.active_power_kw == doctest::Approx(0.0).epsilon(1e-12));
}

TEST_CASE("ramp-up clamped to previous + max_ramp*dt")
{
    // prev = 10, dt = 1, max_ramp = 25 → window [-15, 35];
    // requested 50 must clip to 35.
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(50.0, 10.0, 1.0, 1);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_RAMP_UP_CLAMPED));
    CHECK(cmd.active_power_kw == doctest::Approx(35.0).epsilon(1e-12));
}

TEST_CASE("ramp-down clamped to previous - max_ramp*dt")
{
    // prev = 10, dt = 1, max_ramp = 25 → window [-15, 35];
    // requested -20 clamps to -15. Same fixture as the
    // "ramp-down-clamp" parity case in NativeKernelParityTests.cs.
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(-20.0, 10.0, 1.0, 1);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_RAMP_DOWN_CLAMPED));
    CHECK(cmd.active_power_kw == doctest::Approx(-15.0).epsilon(1e-12));
}

TEST_CASE("dt == 0 with previous and changing target → ramp-not-permitted")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(/*target=*/25.0, /*previous=*/10.0,
                                  /*dt=*/0.0, /*has_previous=*/1);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_RAMP_NOT_PERMITTED));
    // ramp-not-permitted holds output at the previous power.
    CHECK(cmd.active_power_kw == doctest::Approx(10.0).epsilon(1e-12));
}

TEST_CASE("ramp limiter inside window leaves request unchanged (OK within-limits)")
{
    // prev = 10, dt = 1, max_ramp = 25 → window [-15, 35].
    // requested 20 lies strictly inside the window, so apply_ramp
    // exits through the unclamped tail returning OK with
    // within-limits. Pinning this branch keeps coverage at 100%
    // alongside the up/down-clamp tests above.
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(/*target=*/20.0, /*previous=*/10.0,
                                  /*dt=*/1.0, /*has_previous=*/1);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_OK);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_WITHIN_LIMITS));
    CHECK(cmd.active_power_kw == doctest::Approx(20.0).epsilon(1e-12));
}

TEST_CASE("dt == 0 with previous and equal target → OK within-limits (no clamp)")
{
    // The ramp limiter has a single OK exit when requested == previous
    // even with dt == 0 / max_ramp == 0; the held-equal branch is
    // distinct from ramp-not-permitted and must surface as OK.
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(/*target=*/10.0, /*previous=*/10.0,
                                  /*dt=*/0.0, /*has_previous=*/1);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_OK);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_WITHIN_LIMITS));
    CHECK(cmd.active_power_kw == doctest::Approx(10.0).epsilon(1e-12));
}

TEST_CASE("max_ramp == 0 with previous and changing target → ramp-not-permitted")
{
    // The dt == 0 and max_ramp == 0 paths share the same managed
    // contract. Pinning both keeps a future refactor that splits
    // them honest.
    auto lim = make_limits();
    lim.max_ramp_kw_per_second = 0.0;
    const auto snap = make_snapshot();
    const auto req = make_request(/*target=*/25.0, /*previous=*/10.0,
                                  /*dt=*/1.0, /*has_previous=*/1);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_RAMP_NOT_PERMITTED));
    CHECK(cmd.active_power_kw == doctest::Approx(10.0).epsilon(1e-12));
}

TEST_CASE("constraint + ramp combined: constraint reason wins, ramp value wins")
{
    // requested 100 kW from prev 10 with max_discharge 50 kW and
    // ramp window 10±25 → constraint clamps to 50, ramp clamps to 35.
    // Plan rule: final power = ramp result (35), reason = constraint
    // (max-discharge-power).
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(100.0, 10.0, 1.0, 1);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_MAX_DISCHARGE_POWER));
    CHECK(cmd.active_power_kw == doctest::Approx(35.0).epsilon(1e-12));
}

TEST_CASE("first tick (has_previous == 0) skips ramp limiter")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(/*target=*/40.0, 0.0, 1.0,
                                  /*has_previous=*/0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_OK);

    CHECK(cmd.active_power_kw == doctest::Approx(40.0).epsilon(1e-12));
    CHECK(cmd.mode == static_cast<int32_t>(BCC_MODE_DISCHARGE));
}

TEST_CASE("zero-power output maps to BCC_MODE_IDLE")
{
    // Mode mapping pin: power == 0 → IDLE (not STOP — STOP is reserved
    // for a managed-side state-machine decision per architecture §4.1).
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(/*target=*/0.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_OK);

    CHECK(cmd.active_power_kw == doctest::Approx(0.0).epsilon(1e-12));
    CHECK(cmd.mode == static_cast<int32_t>(BCC_MODE_IDLE));
}

TEST_CASE("non-finite SOC in snapshot returns NON_FINITE input")
{
    const auto lim = make_limits();
    auto snap = make_snapshot();
    snap.soc_percent = std::numeric_limits<double>::quiet_NaN();
    const auto req = make_request(10.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_NON_FINITE);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));
    CHECK(cmd.active_power_kw == doctest::Approx(0.0).epsilon(1e-12));
}

TEST_CASE("non-finite limit (max_charge = +Inf) returns NON_FINITE input")
{
    auto lim = make_limits();
    lim.max_charge_power_kw = std::numeric_limits<double>::infinity();
    const auto snap = make_snapshot();
    const auto req = make_request(10.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_NON_FINITE);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));
}

TEST_CASE("non-finite request target returns NON_FINITE input")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    auto req = make_request(0.0, 0.0, 1.0, 0);
    req.target_active_power_kw = std::numeric_limits<double>::quiet_NaN();
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_NON_FINITE);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));
}

TEST_CASE("NaN previous power tolerated when has_previous == 0")
{
    // First-tick contract: managed code may legitimately hand NaN
    // for previous_active_power_kw to signal "no previous". The
    // ramp limiter must never inspect it in this branch, and the
    // non-finite guard must NOT fire. Mirror of the integration
    // test in NativeAbiNegativeTests.
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    auto req = make_request(10.0, 0.0, 1.0, /*has_previous=*/0);
    req.previous_active_power_kw = std::numeric_limits<double>::quiet_NaN();
    req.dt_seconds = std::numeric_limits<double>::quiet_NaN();
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_OK);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_WITHIN_LIMITS));
    CHECK(cmd.active_power_kw == doctest::Approx(10.0).epsilon(1e-12));
}

TEST_CASE("NaN previous power rejected when has_previous == 1")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    auto req = make_request(10.0, 0.0, 1.0, /*has_previous=*/1);
    req.previous_active_power_kw = std::numeric_limits<double>::quiet_NaN();
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_NON_FINITE);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));
}

TEST_CASE("negative dt with has_previous == 1 returns NEGATIVE_DT")
{
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(/*target=*/10.0, /*previous=*/5.0,
                                  /*dt=*/-1.0, /*has_previous=*/1);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_NEGATIVE_DT);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NEGATIVE_DT));
    CHECK(cmd.active_power_kw == doctest::Approx(0.0).epsilon(1e-12));
}

TEST_CASE("negative dt without previous is ignored (ramp never runs)")
{
    // has_previous == 0 → ramp limiter is skipped entirely, so a
    // negative dt is irrelevant. Documents the contract from
    // compute.cpp explicitly.
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(/*target=*/10.0, /*previous=*/0.0,
                                  /*dt=*/-5.0, /*has_previous=*/0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(&snap, &lim, &req, &cmd) == BCC_STATUS_OK);

    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_WITHIN_LIMITS));
}

TEST_CASE("null pointers yield INVALID_INPUT")
{
    bcc_command_t cmd{};
    CHECK(battery_control_core_compute(nullptr, nullptr, nullptr, &cmd) == BCC_STATUS_INVALID_INPUT);
    // Without out_command we cannot signal a reason, but the bare
    // status return must still be INVALID_INPUT — this is the only
    // error case where the kernel cannot write into the command
    // struct, so the contract from battery_control_core.h says the
    // function falls back to the bare status.
    CHECK(battery_control_core_compute(nullptr, nullptr, nullptr, nullptr) == BCC_STATUS_INVALID_INPUT);
}

TEST_CASE("partial null inputs (out_command non-null) yield INVALID_INPUT")
{
    // Pin the early-return path where snapshot is null but limits
    // and request are not — the C-ABI contract demands the same
    // status as the all-null case.
    const auto lim = make_limits();
    const auto req = make_request(10.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    REQUIRE(battery_control_core_compute(nullptr, &lim, &req, &cmd) == BCC_STATUS_INVALID_INPUT);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));
    CHECK(cmd.active_power_kw == doctest::Approx(0.0).epsilon(1e-12));
}

TEST_CASE("ABI version function exposes packed major/minor/patch")
{
    const uint32_t v = battery_control_core_abi_version();
    CHECK(v == BCC_ABI_VERSION);
    CHECK(((v >> 16) & 0xFFu) == BCC_ABI_VERSION_MAJOR);
    CHECK(((v >> 8) & 0xFFu) == BCC_ABI_VERSION_MINOR);
    CHECK((v & 0xFFu) == BCC_ABI_VERSION_PATCH);
}

// ---------------------------------------------------------------------
// RM-M3-13 PID kernel tests.
// ---------------------------------------------------------------------

namespace
{

bcc_pid_options_t make_pid_options(double kp = 1.0, double ki = 0.5, double kd = 0.1,
                                   double output_min = -100.0, double output_max = 100.0,
                                   double deadband = 0.0)
{
    bcc_pid_options_t o{};
    o.kp = kp;
    o.ki = ki;
    o.kd = kd;
    o.output_min = output_min;
    o.output_max = output_max;
    o.deadband_absolute = deadband;
    o.anti_windup_mode = static_cast<int32_t>(BCC_PID_ANTI_WINDUP_CONDITIONAL_INTEGRATION);
    return o;
}

bcc_pid_state_t make_pid_state(double integral = 0.0, double previous_error = 0.0)
{
    bcc_pid_state_t s{};
    s.integral = integral;
    s.previous_error = previous_error;
    return s;
}

bcc_pid_input_t make_pid_input(double setpoint, double measurement, double dt_seconds = 1.0)
{
    bcc_pid_input_t i{};
    i.setpoint = setpoint;
    i.measurement = measurement;
    i.dt_seconds = dt_seconds;
    return i;
}

} // namespace

TEST_CASE("PID happy path produces P+I+D output and updates state")
{
    // Step 1 from zero state: error = 10, dt = 1.
    //   P = 1.0 * 10 = 10
    //   I_step = 0.5 * 10 = 5; I_next = 0 + 5*1 = 5
    //   D = 0.1 * (10 - 0) / 1 = 1
    //   pre-clamp = 10 + 5 + 1 = 16; within [-100, 100].
    const auto opts = make_pid_options();
    const auto state = make_pid_state();
    const auto in = make_pid_input(/*setpoint=*/10.0, /*measurement=*/0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_OK);

    CHECK(cmd.output == doctest::Approx(16.0).epsilon(1e-12));
    CHECK(cmd.next_integral == doctest::Approx(5.0).epsilon(1e-12));
    CHECK(cmd.next_previous_error == doctest::Approx(10.0).epsilon(1e-12));
    CHECK(cmd.was_clamped == 0);
    CHECK(cmd.was_integral_frozen == 0);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_WITHIN_LIMITS));
}

TEST_CASE("PID output clamped at OutputMax → LIMITED with PID_OUTPUT_CLAMPED_HIGH")
{
    // Drive the controller into saturation with small bounds.
    const auto opts = make_pid_options(/*kp=*/10.0, /*ki=*/0.0, /*kd=*/0.0,
                                       /*output_min=*/-1.0, /*output_max=*/1.0);
    const auto state = make_pid_state();
    const auto in = make_pid_input(/*setpoint=*/100.0, /*measurement=*/0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.output == doctest::Approx(1.0).epsilon(1e-12));
    CHECK(cmd.was_clamped == 1);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_PID_OUTPUT_CLAMPED_HIGH));
}

TEST_CASE("PID output clamped at OutputMin → LIMITED with PID_OUTPUT_CLAMPED_LOW")
{
    const auto opts = make_pid_options(10.0, 0.0, 0.0, -1.0, 1.0);
    const auto state = make_pid_state();
    const auto in = make_pid_input(-100.0, 0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.output == doctest::Approx(-1.0).epsilon(1e-12));
    CHECK(cmd.was_clamped == 1);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_PID_OUTPUT_CLAMPED_LOW));
}

TEST_CASE("PID anti-windup freezes integrator at high saturation with positive Ki·error")
{
    // pre-clamp > output_max AND integrator_step > 0 → freeze.
    const auto opts = make_pid_options(/*kp=*/10.0, /*ki=*/1.0, /*kd=*/0.0,
                                       /*output_min=*/-1.0, /*output_max=*/1.0);
    const auto state = make_pid_state(/*integral=*/0.5);
    const auto in = make_pid_input(/*setpoint=*/100.0, /*measurement=*/0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.was_integral_frozen == 1);
    CHECK(cmd.next_integral == doctest::Approx(0.5).epsilon(1e-12)); // unchanged
    CHECK(cmd.was_clamped == 1);
}

TEST_CASE("PID anti-windup does NOT freeze with negative Ki when integrator decrements toward relief")
{
    // Plan rule: direction is integrator_step sign (Ki·error), not error.
    // With Ki = -1 and error > 0, integrator_step < 0; if pre-clamp also
    // exceeds OutputMax, the freeze guard expects integrator_step > 0,
    // so it does NOT freeze — the integrator continues unwinding toward
    // the bound.
    const auto opts = make_pid_options(/*kp=*/10.0, /*ki=*/-1.0, /*kd=*/0.0,
                                       /*output_min=*/-1.0, /*output_max=*/1.0);
    const auto state = make_pid_state(/*integral=*/2.0);
    const auto in = make_pid_input(/*setpoint=*/100.0, /*measurement=*/0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_LIMITED);

    CHECK(cmd.was_integral_frozen == 0);
    // integrator_step = -1 * 100 = -100; new I = 2 + (-100*1) = -98
    CHECK(cmd.next_integral == doctest::Approx(-98.0).epsilon(1e-12));
}

TEST_CASE("PID deadband suppresses P and D, holds integrator and previous_error")
{
    // |error| = 0.5 < deadband 1.0 → effective_error = 0; P = D = 0.
    // Integrator step is also 0 (Ki * 0); chosen_integral stays at the
    // input integral. previous_error preserved across the band.
    const auto opts = make_pid_options(/*kp=*/10.0, /*ki=*/1.0, /*kd=*/5.0,
                                       /*output_min=*/-100.0, /*output_max=*/100.0,
                                       /*deadband=*/1.0);
    const auto state = make_pid_state(/*integral=*/3.0, /*previous_error=*/-2.0);
    const auto in = make_pid_input(/*setpoint=*/0.5, /*measurement=*/0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_OK);

    // pre-clamp = 0 + 3 + 0 = 3
    CHECK(cmd.output == doctest::Approx(3.0).epsilon(1e-12));
    CHECK(cmd.next_integral == doctest::Approx(3.0).epsilon(1e-12)); // held
    // previous_error preserved across the deadband (NOT zeroed):
    CHECK(cmd.next_previous_error == doctest::Approx(-2.0).epsilon(1e-12));
}

TEST_CASE("PID deadband boundary: |error| == deadband is NOT in band")
{
    // Strict less-than: error == deadband must produce a normal step.
    const auto opts = make_pid_options(/*kp=*/1.0, /*ki=*/0.0, /*kd=*/0.0,
                                       /*output_min=*/-100, /*output_max=*/100,
                                       /*deadband=*/1.0);
    const auto state = make_pid_state();
    const auto in = make_pid_input(/*setpoint=*/1.0, /*measurement=*/0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_OK);

    CHECK(cmd.output == doctest::Approx(1.0).epsilon(1e-12));
    CHECK(cmd.next_previous_error == doctest::Approx(1.0).epsilon(1e-12));
}

TEST_CASE("PID dt <= 0 returns NEGATIVE_DT")
{
    const auto opts = make_pid_options();
    const auto state = make_pid_state();
    const auto in = make_pid_input(10.0, 0.0, /*dt_seconds=*/0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_NEGATIVE_DT);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NEGATIVE_DT));

    auto in_neg = in;
    in_neg.dt_seconds = -1.0;
    bcc_pid_command_t cmd2{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in_neg, &cmd2) == BCC_STATUS_NEGATIVE_DT);
}

TEST_CASE("PID non-finite setpoint or measurement → NON_FINITE")
{
    const auto opts = make_pid_options();
    const auto state = make_pid_state();
    auto in = make_pid_input(std::numeric_limits<double>::quiet_NaN(), 0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_NON_FINITE);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));

    in = make_pid_input(0.0, std::numeric_limits<double>::infinity());
    bcc_pid_command_t cmd2{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd2) == BCC_STATUS_NON_FINITE);
}

TEST_CASE("PID non-finite state.integral or state.previous_error → NON_FINITE")
{
    const auto opts = make_pid_options();
    const auto in = make_pid_input(10.0, 0.0);
    auto bad_state = make_pid_state(std::numeric_limits<double>::quiet_NaN());
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&bad_state, &opts, &in, &cmd) == BCC_STATUS_NON_FINITE);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));

    bad_state = make_pid_state(0.0, std::numeric_limits<double>::infinity());
    bcc_pid_command_t cmd2{};
    REQUIRE(battery_control_core_pid_step(&bad_state, &opts, &in, &cmd2) == BCC_STATUS_NON_FINITE);
}

TEST_CASE("PID non-finite gain → NON_FINITE")
{
    auto opts = make_pid_options();
    opts.ki = std::numeric_limits<double>::quiet_NaN();
    const auto state = make_pid_state();
    const auto in = make_pid_input(10.0, 0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_NON_FINITE);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));
}

TEST_CASE("PID OutputMin > OutputMax → INVALID_INPUT with PID_INVALID_OPTIONS")
{
    auto opts = make_pid_options();
    opts.output_min = 100.0;
    opts.output_max = 1.0;
    const auto state = make_pid_state();
    const auto in = make_pid_input(10.0, 0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_INVALID_INPUT);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_PID_INVALID_OPTIONS));
}

TEST_CASE("PID negative deadband → INVALID_INPUT with PID_INVALID_OPTIONS")
{
    auto opts = make_pid_options();
    opts.deadband_absolute = -0.001;
    const auto state = make_pid_state();
    const auto in = make_pid_input(10.0, 0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_INVALID_INPUT);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_PID_INVALID_OPTIONS));
}

TEST_CASE("PID unknown anti-windup mode → UNSUPPORTED_STATE")
{
    auto opts = make_pid_options();
    opts.anti_windup_mode = 99;
    const auto state = make_pid_state();
    const auto in = make_pid_input(10.0, 0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_UNSUPPORTED_STATE);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_UNSUPPORTED_STATE));
}

TEST_CASE("PID P-term overflow with Ki=0 → NON_FINITE with NON_FINITE_OUTPUT")
{
    // kp * effective_error overflows to ±Inf for finite inputs when
    // either factor is large enough. Ki=0 keeps the integrator path
    // out of it, so the integrator-overflow guard does NOT catch
    // this — the pre-clamp NON_FINITE_OUTPUT branch is the right
    // one (matches the managed reference's second OverflowException
    // site for "P+D non-finite from finite inputs").
    auto opts = make_pid_options();
    opts.kp = std::numeric_limits<double>::max();
    opts.ki = 0.0;
    opts.kd = 0.0;
    const auto state = make_pid_state();
    const auto in = make_pid_input(/*setpoint=*/2.0, /*measurement=*/0.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_NON_FINITE);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_OUTPUT));
}

TEST_CASE("PID integrator overflow → NON_FINITE with PID_INTEGRATOR_OVERFLOW")
{
    // ki * effective_error * dt overflows to ±Inf when scaled to
    // DBL_MAX with dt > 1 (the multiplication tips it past the
    // representable range). The native side maps this to
    // BCC_REASON_PID_INTEGRATOR_OVERFLOW rather than letting the
    // anti-windup freeze silently mask it.
    auto opts = make_pid_options();
    opts.ki = std::numeric_limits<double>::max();
    const auto state = make_pid_state();
    const auto in = make_pid_input(/*setpoint=*/std::numeric_limits<double>::max(),
                                   /*measurement=*/0.0,
                                   /*dt_seconds=*/2.0);
    bcc_pid_command_t cmd{};
    REQUIRE(battery_control_core_pid_step(&state, &opts, &in, &cmd) == BCC_STATUS_NON_FINITE);
    CHECK(cmd.reason_code == static_cast<int32_t>(BCC_REASON_PID_INTEGRATOR_OVERFLOW));
}

TEST_CASE("PID null pointer → INVALID_INPUT")
{
    bcc_pid_command_t cmd{};
    CHECK(battery_control_core_pid_step(nullptr, nullptr, nullptr, &cmd) == BCC_STATUS_INVALID_INPUT);
    CHECK(battery_control_core_pid_step(nullptr, nullptr, nullptr, nullptr) == BCC_STATUS_INVALID_INPUT);
}

TEST_CASE("PID state propagation across two consecutive steps")
{
    // Step 1: error 10, dt 1, kp=1, ki=0.5, kd=0.1.
    //   I_next = 5; prev_error_next = 10; output = 16.
    // Step 2 (feed step-1 state back): same setpoint/measurement.
    //   error = 10 (unchanged); D = 0.1*(10-10)/1 = 0
    //   I_next = 5 + 5 = 10; output = 10 + 10 + 0 = 20.
    const auto opts = make_pid_options(/*kp=*/1.0, /*ki=*/0.5, /*kd=*/0.1);
    const auto in = make_pid_input(10.0, 0.0);

    bcc_pid_command_t step1{};
    auto s0 = make_pid_state();
    REQUIRE(battery_control_core_pid_step(&s0, &opts, &in, &step1) == BCC_STATUS_OK);
    CHECK(step1.next_integral == doctest::Approx(5.0).epsilon(1e-12));
    CHECK(step1.next_previous_error == doctest::Approx(10.0).epsilon(1e-12));

    bcc_pid_command_t step2{};
    bcc_pid_state_t s1{};
    s1.integral = step1.next_integral;
    s1.previous_error = step1.next_previous_error;
    REQUIRE(battery_control_core_pid_step(&s1, &opts, &in, &step2) == BCC_STATUS_OK);
    CHECK(step2.output == doctest::Approx(20.0).epsilon(1e-12));
    CHECK(step2.next_integral == doctest::Approx(10.0).epsilon(1e-12));
}

// ---------------------------------------------------------------------
// RM-M5-03 telemetry-filter tests.
// ---------------------------------------------------------------------

namespace
{

bcc_telemetry_filter_state_t make_filter_state(
    double soc = 50.0,
    double power = 10.0,
    double temperature = 22.0,
    int32_t initialized = 1)
{
    bcc_telemetry_filter_state_t s{};
    s.filtered_soc_percent = soc;
    s.filtered_active_power_kw = power;
    s.filtered_temperature_celsius = temperature;
    s.initialized = initialized;
    return s;
}

bcc_telemetry_filter_options_t make_filter_options(double alpha = 0.25)
{
    bcc_telemetry_filter_options_t o{};
    o.alpha = alpha;
    o.max_soc_delta_percent = 20.0;
    o.max_power_delta_kw = 50.0;
    o.max_temperature_delta_celsius = 10.0;
    o.min_sample_period_seconds = 0.001;
    o.max_sample_period_seconds = 1.0;
    return o;
}

bcc_telemetry_filter_input_t make_filter_input(
    double soc = 54.0,
    double power = 30.0,
    double temperature = 24.0,
    double dt = 0.01)
{
    bcc_telemetry_filter_input_t i{};
    i.soc_percent = soc;
    i.active_power_kw = power;
    i.temperature_celsius = temperature;
    i.dt_seconds = dt;
    return i;
}

} // namespace

TEST_CASE("Telemetry filter cold boot seeds from first measurement")
{
    const auto state = make_filter_state(
        std::numeric_limits<double>::quiet_NaN(),
        std::numeric_limits<double>::quiet_NaN(),
        std::numeric_limits<double>::quiet_NaN(),
        /*initialized=*/0);
    const auto opts = make_filter_options(/*alpha=*/0.25);
    const auto in = make_filter_input(/*soc=*/55.0, /*power=*/12.0,
                                      /*temperature=*/23.0);
    bcc_telemetry_filter_output_t out{};
    REQUIRE(battery_control_core_filter_telemetry(&state, &opts, &in, &out) == BCC_STATUS_OK);

    CHECK(out.filtered_soc_percent == doctest::Approx(55.0).epsilon(1e-12));
    CHECK(out.filtered_active_power_kw == doctest::Approx(12.0).epsilon(1e-12));
    CHECK(out.filtered_temperature_celsius == doctest::Approx(23.0).epsilon(1e-12));
    CHECK(out.initialized == 1);
    CHECK(out.drift_detected == 0);
    CHECK(out.reason_code == static_cast<int32_t>(BCC_REASON_WITHIN_LIMITS));
}

TEST_CASE("Telemetry filter applies first-order IIR update after initialization")
{
    const auto state = make_filter_state(/*soc=*/50.0, /*power=*/10.0,
                                         /*temperature=*/20.0);
    const auto opts = make_filter_options(/*alpha=*/0.25);
    const auto in = make_filter_input(/*soc=*/54.0, /*power=*/30.0,
                                      /*temperature=*/24.0);
    bcc_telemetry_filter_output_t out{};
    REQUIRE(battery_control_core_filter_telemetry(&state, &opts, &in, &out) == BCC_STATUS_OK);

    CHECK(out.filtered_soc_percent == doctest::Approx(51.0).epsilon(1e-12));
    CHECK(out.filtered_active_power_kw == doctest::Approx(15.0).epsilon(1e-12));
    CHECK(out.filtered_temperature_celsius == doctest::Approx(21.0).epsilon(1e-12));
}

TEST_CASE("Telemetry filter alpha one is pass-through and alpha zero holds state")
{
    const auto state = make_filter_state(/*soc=*/50.0, /*power=*/10.0,
                                         /*temperature=*/20.0);
    const auto in = make_filter_input(/*soc=*/54.0, /*power=*/30.0,
                                      /*temperature=*/24.0);

    auto opts = make_filter_options(/*alpha=*/1.0);
    bcc_telemetry_filter_output_t pass{};
    REQUIRE(battery_control_core_filter_telemetry(&state, &opts, &in, &pass) == BCC_STATUS_OK);
    CHECK(pass.filtered_soc_percent == doctest::Approx(54.0).epsilon(1e-12));
    CHECK(pass.filtered_active_power_kw == doctest::Approx(30.0).epsilon(1e-12));

    opts.alpha = 0.0;
    bcc_telemetry_filter_output_t held{};
    REQUIRE(battery_control_core_filter_telemetry(&state, &opts, &in, &held) == BCC_STATUS_OK);
    CHECK(held.filtered_soc_percent == doctest::Approx(50.0).epsilon(1e-12));
    CHECK(held.filtered_active_power_kw == doctest::Approx(10.0).epsilon(1e-12));
}

TEST_CASE("Telemetry filter rejects non-finite input")
{
    const auto state = make_filter_state();
    const auto opts = make_filter_options();
    auto in = make_filter_input();
    in.active_power_kw = std::numeric_limits<double>::quiet_NaN();
    bcc_telemetry_filter_output_t out{};
    REQUIRE(battery_control_core_filter_telemetry(&state, &opts, &in, &out) == BCC_STATUS_NON_FINITE);

    CHECK(out.reason_code == static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));
    CHECK(out.initialized == 0);
}

TEST_CASE("Telemetry filter rejects invalid options")
{
    const auto state = make_filter_state();
    auto opts = make_filter_options();
    opts.alpha = 1.01;
    const auto in = make_filter_input();
    bcc_telemetry_filter_output_t out{};
    REQUIRE(battery_control_core_filter_telemetry(&state, &opts, &in, &out) == BCC_STATUS_INVALID_INPUT);

    CHECK(out.reason_code == static_cast<int32_t>(BCC_REASON_FILTER_INVALID_OPTIONS));
}

TEST_CASE("Telemetry filter rejects sample period outside configured window")
{
    const auto state = make_filter_state();
    const auto opts = make_filter_options();
    auto in = make_filter_input();
    in.dt_seconds = 2.0;
    bcc_telemetry_filter_output_t out{};
    REQUIRE(battery_control_core_filter_telemetry(&state, &opts, &in, &out) == BCC_STATUS_INVALID_INPUT);

    CHECK(out.reason_code == static_cast<int32_t>(BCC_REASON_FILTER_SAMPLE_PERIOD));
}

TEST_CASE("Telemetry filter rejects drift and preserves prior filtered state")
{
    const auto state = make_filter_state(/*soc=*/50.0, /*power=*/10.0,
                                         /*temperature=*/20.0);
    auto opts = make_filter_options();
    opts.max_power_delta_kw = 5.0;
    const auto in = make_filter_input(/*soc=*/51.0, /*power=*/30.0,
                                      /*temperature=*/20.5);
    bcc_telemetry_filter_output_t out{};
    REQUIRE(battery_control_core_filter_telemetry(&state, &opts, &in, &out) == BCC_STATUS_INVALID_INPUT);

    CHECK(out.reason_code == static_cast<int32_t>(BCC_REASON_FILTER_TELEMETRY_DRIFT));
    CHECK(out.drift_detected == 1);
    CHECK(out.filtered_active_power_kw == doctest::Approx(10.0).epsilon(1e-12));
    CHECK(out.initialized == 1);
}

TEST_CASE("Telemetry filter null pointer yields INVALID_INPUT")
{
    bcc_telemetry_filter_output_t out{};
    CHECK(battery_control_core_filter_telemetry(nullptr, nullptr, nullptr, &out) == BCC_STATUS_INVALID_INPUT);
    CHECK(battery_control_core_filter_telemetry(nullptr, nullptr, nullptr, nullptr) == BCC_STATUS_INVALID_INPUT);
}
