using Dapper;

namespace BatteryEms.Adapters.Persistence;

// One-shot Dapper configuration shared across all repositories in this
// assembly. MatchNamesWithUnderscores lets row classes use PascalCase
// property names while the SQL columns stay snake_case — readable on
// both sides without per-query AS aliases.
internal static class DapperConfig
{
    private static int _initialised;

    public static void EnsureConfigured()
    {
        if (Interlocked.Exchange(ref _initialised, 1) == 1)
        {
            return;
        }
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}
