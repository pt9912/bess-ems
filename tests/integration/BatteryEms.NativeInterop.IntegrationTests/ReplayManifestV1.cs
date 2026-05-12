using System.Text.Json;

namespace BatteryEms.NativeInterop.IntegrationTests;

internal sealed record ReplayManifestV1(
    string DatasetId,
    string Kind,
    ReplayManifestFileReference Fixture,
    ReplayManifestFileReference Golden,
    ReplayManifestTolerances Tolerances,
    IReadOnlyList<string> CoversLegacyCases)
{
    public static ReplayManifestV1 LoadFromRepo(string relativePath)
    {
        var path = RepositoryPath(relativePath);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var schemaVersion = RequiredString(root, "schema_version");
        if (!StringComparer.Ordinal.Equals(schemaVersion, "replay-manifest.v1"))
        {
            throw new InvalidDataException($"Unsupported replay manifest schema '{schemaVersion}'.");
        }

        return new ReplayManifestV1(
            DatasetId: RequiredString(root, "dataset_id"),
            Kind: RequiredString(root, "kind"),
            Fixture: ReadFileReference(root.GetProperty("fixture")),
            Golden: ReadFileReference(root.GetProperty("golden")),
            Tolerances: ReadTolerances(root.GetProperty("tolerances")),
            CoversLegacyCases: ReadCompatibility(root.GetProperty("compatibility")));
    }

    public static string ResolveRepoReference(string path)
    {
        const string Prefix = "repo://";
        if (!path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected repo:// replay reference, got '{path}'.");
        }

        return RepositoryPath(path[Prefix.Length..]);
    }

    private static ReplayManifestFileReference ReadFileReference(JsonElement element) =>
        new(
            Path: RequiredString(element, "path"),
            SchemaVersion: RequiredString(element, "schema_version"));

    private static ReplayManifestTolerances ReadTolerances(JsonElement element) =>
        new(RequiredDouble(element, "active_power_kw_abs"));

    private static IReadOnlyList<string> ReadCompatibility(JsonElement element)
    {
        var cases = element.GetProperty("covers_legacy_cases");
        var names = new List<string>();
        foreach (var item in cases.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String)
            {
                throw new InvalidDataException(
                    "Manifest field 'compatibility.covers_legacy_cases' must contain only strings.");
            }

            var name = item.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException(
                    "Manifest field 'compatibility.covers_legacy_cases' must not contain empty names.");
            }

            names.Add(name);
        }

        return names;
    }

    private static string RequiredString(JsonElement element, string name)
    {
        var property = element.GetProperty(name);
        if (property.ValueKind is not JsonValueKind.String)
        {
            throw new InvalidDataException($"Manifest field '{name}' must be a string.");
        }

        return property.GetString() ?? string.Empty;
    }

    private static double RequiredDouble(JsonElement element, string name)
    {
        var property = element.GetProperty(name);
        if (!property.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            throw new InvalidDataException($"Manifest field '{name}' must be a finite number.");
        }

        return value;
    }

    private static string RepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BatteryEms.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        return Path.Combine(directory.FullName, relativePath);
    }
}

internal sealed record ReplayManifestFileReference(string Path, string SchemaVersion);

internal sealed record ReplayManifestTolerances(double ActivePowerKwAbs);
