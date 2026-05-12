using System.Globalization;
using BatteryEms.Domain;

namespace BatteryEms.Application.Tests.Replay;

internal static class ReplayGoldenComparer
{
    public static ReplayDiffReport Compare(
        IReadOnlyList<BatteryCommand> actual,
        IReadOnlyList<ReplayGoldenCommand> expected,
        ReplayTolerances tolerances)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(tolerances);

        var differences = new List<ReplayDiff>();
        if (actual.Count != expected.Count)
        {
            differences.Add(BusinessDrift(
                "$.commands.count",
                expected.Count.ToString(CultureInfo.InvariantCulture),
                actual.Count.ToString(CultureInfo.InvariantCulture)));
        }

        var count = Math.Min(actual.Count, expected.Count);
        for (var index = 0; index < count; index++)
        {
            CompareCommand(actual[index], expected[index], tolerances, index, differences);
        }

        return new ReplayDiffReport(differences);
    }

    private static void CompareCommand(
        BatteryCommand actual,
        ReplayGoldenCommand expected,
        ReplayTolerances tolerances,
        int index,
        List<ReplayDiff> differences)
    {
        var prefix = $"$.commands[{index}]";
        CompareScalar(expected.CommandId, actual.CommandId, $"{prefix}.command_id", differences);
        CompareScalar(expected.Timestamp, actual.Timestamp, $"{prefix}.timestamp_utc", differences);
        CompareScalar(expected.AssetId, actual.AssetId, $"{prefix}.asset_id", differences);
        CompareScalar(expected.Mode, actual.Mode, $"{prefix}.mode", differences);
        CompareScalar(expected.Reason, actual.Reason, $"{prefix}.reason", differences);
        CompareScalar(expected.Source, actual.Source, $"{prefix}.source", differences);
        CompareNumeric(
            expected.ActivePowerKw,
            actual.ActivePowerKw,
            tolerances.ActivePowerKwAbs,
            $"{prefix}.active_power_kw",
            differences);
        CompareNumeric(
            expected.ReactivePowerKvar ?? 0,
            actual.ReactivePowerKvar ?? 0,
            tolerances.ReactivePowerKvarAbs,
            $"{prefix}.reactive_power_kvar",
            differences);
    }

    private static void CompareScalar<T>(
        T expected,
        T actual,
        string path,
        List<ReplayDiff> differences)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            differences.Add(BusinessDrift(
                path,
                Convert.ToString(expected, CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(actual, CultureInfo.InvariantCulture) ?? string.Empty));
        }
    }

    private static void CompareNumeric(
        double expected,
        double actual,
        double tolerance,
        string path,
        List<ReplayDiff> differences)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            differences.Add(new ReplayDiff(
                "numeric_tolerance",
                path,
                expected.ToString("R", CultureInfo.InvariantCulture),
                actual.ToString("R", CultureInfo.InvariantCulture),
                tolerance));
        }
    }

    private static ReplayDiff BusinessDrift(string path, string expected, string actual) =>
        new("business_drift", path, expected, actual, null);
}
