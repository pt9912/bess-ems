using System.Globalization;
using BatteryEms.Adapters.NativeInterop;
using BatteryEms.Application.Control;

namespace BatteryEms.NativeInterop.IntegrationTests;

internal static class NativeParityEngineComparisonRunner
{
    public static NativeParityEngineComparisonReport Compare(
        ReplayManifestV1 manifest,
        ParityFixtureV1 fixture,
        NativeControlKernel native)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(native);

        var differences = new List<NativeParityEngineDifference>();
        var casesByName = fixture.Cases.ToDictionary(theCase => theCase.Name, StringComparer.Ordinal);
        foreach (var caseName in manifest.CoversLegacyCases)
        {
            if (!casesByName.TryGetValue(caseName, out var theCase))
            {
                differences.Add(new NativeParityEngineDifference(
                    caseName,
                    "manifest",
                    "business_drift",
                    "case",
                    "present",
                    "missing",
                    null));
                continue;
            }

            CompareCase(theCase, manifest.Tolerances.ActivePowerKwAbs, native, differences);
        }

        return new NativeParityEngineComparisonReport(differences);
    }

    private static void CompareCase(
        ParityCase theCase,
        double tolerance,
        NativeControlKernel native,
        List<NativeParityEngineDifference> differences)
    {
        var managed = NativeParityReplayEngine.RunManaged(theCase);
        var nativeResult = NativeParityReplayEngine.RunNative(native, theCase, out var nativeMode);
        CompareExpected(theCase.Name, "managed", managed, theCase.Expected, tolerance, differences);
        CompareExpected(theCase.Name, "native", nativeResult, theCase.Expected, tolerance, differences);
        CompareMode(theCase.Name, nativeMode, theCase.Expected.Mode, differences);
        CompareEngines(theCase.Name, managed, nativeResult, tolerance, differences);
    }

    private static void CompareExpected(
        string caseName,
        string engine,
        KernelResult actual,
        ExpectedCommand expected,
        double tolerance,
        List<NativeParityEngineDifference> differences)
    {
        CompareNumeric(caseName, engine, "active_power_kw", expected.ActivePowerKw, actual.ActivePowerKw, tolerance, differences);
        CompareScalar(caseName, engine, "reason", expected.Reason, actual.Reason, differences);
        CompareScalar(caseName, engine, "was_limited", expected.WasLimited, actual.WasLimited, differences);
    }

    private static void CompareMode(
        string caseName,
        int actualMode,
        string expectedMode,
        List<NativeParityEngineDifference> differences)
    {
        var expected = NativeParityReplayEngine.NormaliseMode(expectedMode);
        if (expected != actualMode)
        {
            differences.Add(new NativeParityEngineDifference(
                caseName,
                "native",
                "business_drift",
                "mode",
                expected.ToString(CultureInfo.InvariantCulture),
                actualMode.ToString(CultureInfo.InvariantCulture),
                null));
        }
    }

    private static void CompareEngines(
        string caseName,
        KernelResult managed,
        KernelResult native,
        double tolerance,
        List<NativeParityEngineDifference> differences)
    {
        CompareNumeric(caseName, "managed/native", "active_power_kw", managed.ActivePowerKw, native.ActivePowerKw, tolerance, differences);
        CompareScalar(caseName, "managed/native", "reason", managed.Reason, native.Reason, differences);
        CompareScalar(caseName, "managed/native", "was_limited", managed.WasLimited, native.WasLimited, differences);
    }

    private static void CompareNumeric(
        string caseName,
        string engine,
        string field,
        double expected,
        double actual,
        double tolerance,
        List<NativeParityEngineDifference> differences)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            differences.Add(new NativeParityEngineDifference(
                caseName,
                engine,
                "numeric_tolerance",
                field,
                expected.ToString("R", CultureInfo.InvariantCulture),
                actual.ToString("R", CultureInfo.InvariantCulture),
                tolerance));
        }
    }

    private static void CompareScalar<T>(
        string caseName,
        string engine,
        string field,
        T expected,
        T actual,
        List<NativeParityEngineDifference> differences)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            differences.Add(new NativeParityEngineDifference(
                caseName,
                engine,
                "business_drift",
                field,
                Convert.ToString(expected, CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(actual, CultureInfo.InvariantCulture) ?? string.Empty,
                null));
        }
    }
}

internal sealed record NativeParityEngineComparisonReport(
    IReadOnlyList<NativeParityEngineDifference> Differences)
{
    public bool IsMatch => Differences.Count == 0;
}

internal sealed record NativeParityEngineDifference(
    string CaseName,
    string Engine,
    string Kind,
    string Field,
    string Expected,
    string Actual,
    double? Tolerance);
