using System.IO;

namespace BatteryEms.NativeInterop.IntegrationTests;

// RM-M3-07 helper that locates libbattery_control_core.so for the
// integration tests. Two discovery modes:
//
//   1. BESS_NATIVE_LIB_PATH (env var) — explicit override, used by
//      the test-native-interop Docker stage and by CI configurations
//      that build the .so to a non-standard path.
//   2. Conventional CMake build directories under
//      native/battery_control_core/ — covers a developer running
//      `cmake -S native/battery_control_core -B build/native &&
//      cmake --build build/native` from the repo root.
//
// The locator walks parent directories from the test assembly
// location so an `out/<framework>/` execution context still resolves
// the repo-root-relative paths.
//
// On miss, every test fails with a clear actionable message instead
// of being silently skipped — RM-M3-07 demands the parity gate be
// reproducible, so a missing .so is a setup error, not a soft pass.
internal static class NativeLibraryLocator
{
    private const string LibFileName = "libbattery_control_core.so";

    public static string Locate()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BESS_NATIVE_LIB_PATH");
        if (!string.IsNullOrEmpty(fromEnv))
        {
            return File.Exists(fromEnv)
                ? fromEnv
                : throw new FileNotFoundException(
                    $"BESS_NATIVE_LIB_PATH is set to '{fromEnv}' but no file exists there. "
                    + $"Build the native library first: "
                    + $"cmake -S native/battery_control_core -B build/native && cmake --build build/native.",
                    fromEnv);
        }

        var repoRoot = FindRepoRoot();
        string[] candidates =
        [
            Path.Combine(repoRoot, "build", "native", LibFileName),
            Path.Combine(repoRoot, "native", "battery_control_core", "build", LibFileName),
            Path.Combine(repoRoot, "native", "battery_control_core", "out", LibFileName),
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate {LibFileName}. Set BESS_NATIVE_LIB_PATH or build it via "
            + $"`cmake -S native/battery_control_core -B build/native && cmake --build build/native`. "
            + $"Searched: {string.Join(", ", candidates)}");
    }

    public static string RepoPath(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var allSegments = new string[segments.Length + 1];
        allSegments[0] = FindRepoRoot();
        Array.Copy(segments, 0, allSegments, 1, segments.Length);
        return Path.Combine(allSegments);
    }

    private static string FindRepoRoot()
    {
        // The marker is the .git directory at the repo root; walking
        // up from the test assembly is more robust than a fixed
        // ../../../../ path that breaks under different framework
        // / configuration directory shapes.
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))
                || File.Exists(Path.Combine(dir, "BatteryEms.sln")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) { break; }
            dir = parent.FullName;
        }
        throw new InvalidOperationException(
            "Could not find repo root from " + AppContext.BaseDirectory
            + "; expected to find a .git directory or BatteryEms.sln.");
    }
}
