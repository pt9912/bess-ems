using System.Linq;
using System.Runtime.InteropServices;
using BatteryEms.Adapters.NativeInterop;
using BatteryEms.Application.Control;
using Xunit;

namespace BatteryEms.NativeInterop.IntegrationTests;

// RM-M3-10 replay-based parity gate. Loads the versioned dataset
// at tests/fixtures/native_parity/cases.v1.json and drives every
// (snapshot, limits, request) tuple through both kernels:
//
//   1. native: real libbattery_control_core.so via NativeControlKernel
//   2. managed: ManagedControlKernel (the M1/M2 reference)
//
// For every case the test asserts:
//   - native.ActivePowerKw == expected.active_power_kw    (within tolerance)
//   - native.ReasonCode    -> expected.reason             (via MapReason)
//   - native.Status        -> expected.was_limited
//   - native.Mode          == expected.mode
//   - managed.ActivePowerKw == expected.active_power_kw   (within tolerance)
//   - managed.Reason        == expected.reason
//   - managed.WasLimited    == expected.was_limited
//   - native and managed agree on power / reason / was_limited
//
// The tolerance is read from the fixture (default 1e-12); on x86_64
// both kernels execute the same FP sequence on the same `double`
// values so the actual delta is bit-zero, but the documented
// tolerance leaves headroom for a future architecture that fuses
// arithmetic differently. Negative-dt, non-finite inputs and
// stale-snapshot cases are intentionally NOT in the dataset — see
// tests/fixtures/native_parity/README.md for the rationale.
[Collection("native-library")]
[Trait("Category", "Parity")]
public sealed class NativeParityReplayTests
{
    private static readonly ParityFixtureV1 Fixture = ParityFixtureLoader.Load();

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var c in Fixture.Cases)
        {
            data.Add(c.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Native_and_managed_match_replay_expected(string caseName)
    {
        var theCase = Fixture.Cases.Single(c => c.Name == caseName);
        var libraryPath = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(libraryPath);
        using var native = new NativeControlKernel(handle);

        var managed       = NativeParityReplayEngine.RunManaged(theCase);
        var nativeResult  = NativeParityReplayEngine.RunNative(native, theCase, out var nativeMode);
        var expected      = theCase.Expected;
        var tolerance     = Fixture.ToleranceActivePowerKw;

        // Every kernel matches the documented expectation. Asserting
        // both sides against `expected` independently catches the
        // case where both drift the same way silently.
        AssertMatchesExpected("managed", managed, expected, tolerance);
        AssertMatchesExpected("native",  nativeResult, expected, tolerance);

        // Native carries an explicit Mode field on the BCC command;
        // managed deduces mode from the active-power sign in the
        // worker layer downstream. Pin the native side here so a
        // future BCC mode mapping change surfaces; managed mode is
        // implicitly checked via the active_power_kw equality.
        Assert.Equal(NativeParityReplayEngine.NormaliseMode(expected.Mode), nativeMode);

        // Native ↔ managed parity: redundant given the two checks
        // above, but explicit so the gate's contract reads cleanly.
        Assert.True(
            Math.Abs(managed.ActivePowerKw - nativeResult.ActivePowerKw) <= tolerance,
            $"native and managed disagree on ActivePowerKw: managed={managed.ActivePowerKw} native={nativeResult.ActivePowerKw} tol={tolerance}");
        Assert.Equal(managed.Reason,     nativeResult.Reason);
        Assert.Equal(managed.WasLimited, nativeResult.WasLimited);
    }

    private static void AssertMatchesExpected(
        string side, KernelResult result, ExpectedCommand expected, double tolerance)
    {
        Assert.True(
            Math.Abs(result.ActivePowerKw - expected.ActivePowerKw) <= tolerance,
            $"{side} ActivePowerKw {result.ActivePowerKw} != expected {expected.ActivePowerKw} (tol {tolerance})");
        Assert.Equal(expected.Reason,     result.Reason);
        Assert.Equal(expected.WasLimited, result.WasLimited);
    }
}
