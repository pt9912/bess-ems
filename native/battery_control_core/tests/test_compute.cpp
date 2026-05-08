// RM-M3-02 first-cut C++ tests for the Constraint + Ramp facade.
//
// Hand-rolled assertion harness for now: enough to verify every
// status code is reachable and every reason-code branch fires for
// the same fixtures the managed reference uses. RM-M3-08 will
// broaden this with a real framework + sanitizers + the PID
// follow-up; coverage gating lands in RM-M3-09.

#include "battery_control_core.h"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <limits>

namespace {

int g_failures = 0;
int g_passes   = 0;

void report(const char* test_name, const char* msg)
{
    std::fprintf(stderr, "  FAIL %s: %s\n", test_name, msg);
}

#define EXPECT(test_name, cond)                                            \
    do {                                                                   \
        if (!(cond)) {                                                     \
            ++g_failures;                                                  \
            report((test_name), "expected " #cond);                        \
        } else {                                                           \
            ++g_passes;                                                    \
        }                                                                  \
    } while (0)

#define EXPECT_EQ(test_name, lhs, rhs)                                     \
    do {                                                                   \
        const auto _l = (lhs);                                             \
        const auto _r = (rhs);                                             \
        if (_l != _r) {                                                    \
            ++g_failures;                                                  \
            char _buf[256];                                                \
            std::snprintf(_buf, sizeof(_buf),                              \
                          "%s != %s (got %lld vs %lld)",                   \
                          #lhs, #rhs,                                      \
                          static_cast<long long>(_l),                      \
                          static_cast<long long>(_r));                     \
            report((test_name), _buf);                                     \
        } else {                                                           \
            ++g_passes;                                                    \
        }                                                                  \
    } while (0)

#define EXPECT_NEAR(test_name, actual, expected, tol)                      \
    do {                                                                   \
        const double _a = (actual);                                        \
        const double _e = (expected);                                      \
        if (!std::isfinite(_a) || std::fabs(_a - _e) > (tol)) {             \
            ++g_failures;                                                  \
            char _buf[256];                                                \
            std::snprintf(_buf, sizeof(_buf),                              \
                          "expected %.6f (~%.6f), got %.6f",               \
                          _e, (tol), _a);                                  \
            report((test_name), _buf);                                     \
        } else {                                                           \
            ++g_passes;                                                    \
        }                                                                  \
    } while (0)

// Reference fixture matching the M1 single-bess asset in the .NET
// integration tests: ±50 kW, SOC band 10..90 %, ramp 25 kW/s,
// temperature ±55 °C / -20 °C.
bcc_limits_t make_limits()
{
    bcc_limits_t l{};
    l.max_charge_power_kw       = 50.0;
    l.max_discharge_power_kw    = 50.0;
    l.min_soc_percent           = 10.0;
    l.max_soc_percent           = 90.0;
    l.max_ramp_kw_per_second    = 25.0;
    l.min_temperature_celsius   = -20.0;
    l.max_temperature_celsius   = 55.0;
    return l;
}

bcc_snapshot_t make_snapshot(double soc = 50.0, double power = 0.0,
                             double temp = 22.0)
{
    bcc_snapshot_t s{};
    s.soc_percent          = soc;
    s.active_power_kw      = power;
    s.temperature_celsius  = temp;
    return s;
}

bcc_request_t make_request(double target, double previous = 0.0,
                           double dt = 1.0, int32_t has_previous = 1)
{
    bcc_request_t r{};
    r.target_active_power_kw   = target;
    r.previous_active_power_kw = previous;
    r.dt_seconds               = dt;
    r.has_previous             = has_previous;
    return r;
}

// Each test is a free function; they share the global counters via
// the EXPECT macros.

void test_within_limits()
{
    const char* T = "within_limits";
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    // 10 kW request, no previous so ramp is skipped.
    const auto req = make_request(/*target=*/10.0, 0.0, 1.0,
                                  /*has_previous=*/0);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_OK);
    EXPECT_EQ(T, cmd.status, static_cast<int32_t>(BCC_STATUS_OK));
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_WITHIN_LIMITS));
    EXPECT_NEAR(T, cmd.active_power_kw, 10.0, 1e-9);
    EXPECT_EQ(T, cmd.mode, static_cast<int32_t>(BCC_MODE_DISCHARGE));
}

void test_max_charge_clamp()
{
    const char* T = "max_charge_clamp";
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(-100.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_LIMITED);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_MAX_CHARGE_POWER));
    EXPECT_NEAR(T, cmd.active_power_kw, -50.0, 1e-9);
    EXPECT_EQ(T, cmd.mode, static_cast<int32_t>(BCC_MODE_CHARGE));
}

void test_max_discharge_clamp()
{
    const char* T = "max_discharge_clamp";
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(100.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_LIMITED);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_MAX_DISCHARGE_POWER));
    EXPECT_NEAR(T, cmd.active_power_kw, 50.0, 1e-9);
}

void test_soc_at_max_blocks_charge()
{
    const char* T = "soc_at_max_blocks_charge";
    const auto lim = make_limits();
    const auto snap = make_snapshot(/*soc=*/95.0);
    const auto req = make_request(-30.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_LIMITED);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_SOC_AT_MAX_CHARGE_BLOCKED));
    EXPECT_NEAR(T, cmd.active_power_kw, 0.0, 1e-9);
}

void test_soc_at_min_blocks_discharge()
{
    const char* T = "soc_at_min_blocks_discharge";
    const auto lim = make_limits();
    const auto snap = make_snapshot(/*soc=*/5.0);
    const auto req = make_request(30.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_LIMITED);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_SOC_AT_MIN_DISCHARGE_BLOCKED));
    EXPECT_NEAR(T, cmd.active_power_kw, 0.0, 1e-9);
}

void test_temperature_out_of_range()
{
    const char* T = "temperature_out_of_range";
    const auto lim = make_limits();
    const auto snap = make_snapshot(50.0, 0.0, /*temp=*/70.0);
    const auto req = make_request(20.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_LIMITED);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_TEMPERATURE_OUT_OF_RANGE));
    EXPECT_NEAR(T, cmd.active_power_kw, 0.0, 1e-9);
}

void test_ramp_up_clamp()
{
    const char* T = "ramp_up_clamp";
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    // previous=10 kW, target=50 kW, dt=1 s, max_ramp=25 kW/s
    // → upper bound 35 kW; 50 clamps to 35.
    const auto req = make_request(50.0, 10.0, 1.0, /*has_previous=*/1);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_LIMITED);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_RAMP_UP_CLAMPED));
    EXPECT_NEAR(T, cmd.active_power_kw, 35.0, 1e-9);
}

void test_ramp_down_clamp()
{
    const char* T = "ramp_down_clamp";
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    // previous=20 kW, target=-20 kW, dt=1 s, max_ramp=25 kW/s
    // → lower bound -5 kW; -20 clamps to -5.
    const auto req = make_request(-20.0, 20.0, 1.0, 1);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_LIMITED);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_RAMP_DOWN_CLAMPED));
    EXPECT_NEAR(T, cmd.active_power_kw, -5.0, 1e-9);
}

void test_ramp_not_permitted_dt_zero()
{
    const char* T = "ramp_not_permitted_dt_zero";
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    // dt=0 with target != previous → ramp-not-permitted, hold previous.
    const auto req = make_request(20.0, 10.0, /*dt=*/0.0, 1);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_LIMITED);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_RAMP_NOT_PERMITTED));
    EXPECT_NEAR(T, cmd.active_power_kw, 10.0, 1e-9);
}

void test_constraint_and_ramp_combined_constraint_wins_reason()
{
    const char* T = "constraint_plus_ramp_constraint_wins";
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    // target=100 (constraint clamps to 50) + ramp from previous=10 with
    // dt=1, max_ramp=25 → ramp upper=35, so final value=35 (ramp clamps
    // the constrained 50 down to 35). Reason = max_discharge_power
    // (constraint wins).
    const auto req = make_request(100.0, 10.0, 1.0, 1);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_LIMITED);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_MAX_DISCHARGE_POWER));
    EXPECT_NEAR(T, cmd.active_power_kw, 35.0, 1e-9);
}

void test_first_tick_skips_ramp()
{
    const char* T = "first_tick_skips_ramp";
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    // has_previous=0 → ramp is skipped; constraint-only result.
    const auto req = make_request(40.0, 0.0, 1.0, /*has_previous=*/0);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_OK);
    EXPECT_NEAR(T, cmd.active_power_kw, 40.0, 1e-9);
}

void test_non_finite_input_rejected()
{
    const char* T = "non_finite_input";
    const auto lim = make_limits();
    auto snap = make_snapshot();
    snap.soc_percent = std::numeric_limits<double>::quiet_NaN();
    const auto req = make_request(10.0, 0.0, 1.0, 0);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_NON_FINITE);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_NON_FINITE_INPUT));
}

void test_negative_dt_with_previous_rejected()
{
    const char* T = "negative_dt";
    const auto lim = make_limits();
    const auto snap = make_snapshot();
    const auto req = make_request(10.0, 5.0, /*dt=*/-1.0, 1);
    bcc_command_t cmd{};
    const auto status = battery_control_core_compute(&snap, &lim, &req, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_NEGATIVE_DT);
    EXPECT_EQ(T, cmd.reason_code,
              static_cast<int32_t>(BCC_REASON_NEGATIVE_DT));
}

void test_null_pointers_yield_invalid_input()
{
    const char* T = "null_pointers";
    bcc_command_t cmd{};
    auto status = battery_control_core_compute(nullptr, nullptr, nullptr, &cmd);
    EXPECT_EQ(T, status, BCC_STATUS_INVALID_INPUT);
    // Without out_command we cannot signal a reason, only the status.
    status = battery_control_core_compute(nullptr, nullptr, nullptr, nullptr);
    EXPECT_EQ(T, status, BCC_STATUS_INVALID_INPUT);
}

void test_abi_version_is_exposed()
{
    const char* T = "abi_version";
    const uint32_t v = battery_control_core_abi_version();
    EXPECT_EQ(T, v, BCC_ABI_VERSION);
    // 0.1.0 baseline; later slices may bump.
    EXPECT_EQ(T, (v >> 16) & 0xFF, BCC_ABI_VERSION_MAJOR);
}

}  // namespace

int main()
{
    test_within_limits();
    test_max_charge_clamp();
    test_max_discharge_clamp();
    test_soc_at_max_blocks_charge();
    test_soc_at_min_blocks_discharge();
    test_temperature_out_of_range();
    test_ramp_up_clamp();
    test_ramp_down_clamp();
    test_ramp_not_permitted_dt_zero();
    test_constraint_and_ramp_combined_constraint_wins_reason();
    test_first_tick_skips_ramp();
    test_non_finite_input_rejected();
    test_negative_dt_with_previous_rejected();
    test_null_pointers_yield_invalid_input();
    test_abi_version_is_exposed();

    std::fprintf(stderr, "passed=%d failed=%d\n", g_passes, g_failures);
    return g_failures == 0 ? 0 : 1;
}
