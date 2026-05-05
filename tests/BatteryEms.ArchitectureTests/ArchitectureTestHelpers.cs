using NetArchTest.Rules;

namespace BatteryEms.ArchitectureTests;

internal static class ArchitectureTestHelpers
{
    public static string FormatFailures(TestResult result)
    {
        if (result.IsSuccessful)
        {
            return string.Empty;
        }

        var failures = result.FailingTypeNames is null || result.FailingTypeNames.Count == 0
            ? "(no specific types reported)"
            : string.Join(", ", result.FailingTypeNames);

        return $"Architecture rule violated. Offending types: {failures}";
    }
}
