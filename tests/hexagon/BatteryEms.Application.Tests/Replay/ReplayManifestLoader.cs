using System.Text.Json;

namespace BatteryEms.Application.Tests.Replay;

internal static class ReplayManifestLoader
{
    private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
    {
        "schema_version",
        "dataset_id",
        "kind",
        "schema",
        "fixture",
        "golden",
        "determinism",
        "request_id_rule",
        "solver_options",
        "tolerances",
        "compatibility",
    };

    private static readonly HashSet<string> SchemaFields = new(StringComparer.Ordinal)
    {
        "required_fields",
        "optional_fields",
        "deprecated_fields",
        "tolerated_legacy_fields",
    };

    private static readonly HashSet<string> FileReferenceFields = new(StringComparer.Ordinal)
    {
        "path",
        "schema_version",
    };

    private static readonly HashSet<string> DeterminismFields = new(StringComparer.Ordinal)
    {
        "mode",
        "seed",
        "runtime",
        "numeric",
    };

    private static readonly HashSet<string> SolverFields = new(StringComparer.Ordinal)
    {
        "optimizer",
    };

    private static readonly HashSet<string> ToleranceFields = new(StringComparer.Ordinal)
    {
        "active_power_kw_abs",
        "reactive_power_kvar_abs",
    };

    private static readonly HashSet<string> CompatibilityFields = new(StringComparer.Ordinal)
    {
        "covers_legacy_cases",
    };

    private static readonly HashSet<string> SupportedKinds = new(StringComparer.Ordinal)
    {
        "telemetry-control-cycle",
        "native-control-parity",
        "local-mpc-engine-comparison",
        "optimization-core-sidecar-comparison",
    };

    public static ReplayManifestLoadResult Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = new ReplayJsonReader(document.RootElement, "$");
            var manifest = ReadManifest(root);
            return ReplayManifestLoadResult.Success(manifest);
        }
        catch (JsonException exception)
        {
            return ReplayManifestLoadResult.Failure(
                "invalid_json",
                manifestPath,
                exception.Message);
        }
        catch (ReplayJsonException exception)
        {
            return ReplayManifestLoadResult.Failure(
                exception.Code,
                exception.Path,
                exception.Detail);
        }
    }

    private static ReplayManifest ReadManifest(ReplayJsonReader root)
    {
        root.RejectUnknownProperties(RootFields);
        var schemaVersion = root.RequiredString("schema_version");
        if (!StringComparer.Ordinal.Equals(schemaVersion, ReplaySchemaVersions.Manifest))
        {
            throw new ReplayJsonException(
                "unsupported_schema_version",
                "$.schema_version",
                $"Unsupported manifest schema '{schemaVersion}'.");
        }

        var kind = root.RequiredString("kind");
        if (!SupportedKinds.Contains(kind))
        {
            throw new ReplayJsonException(
                "unsupported_replay_kind",
                "$.kind",
                $"Unsupported replay kind '{kind}'.");
        }

        return new ReplayManifest(
            DatasetId: root.RequiredString("dataset_id"),
            Kind: kind,
            Schema: ReadSchema(root.RequiredObject("schema")),
            Fixture: ReadFileReference(root.RequiredObject("fixture"), ExpectedFixtureSchema(kind)),
            Golden: ReadFileReference(root.RequiredObject("golden"), ExpectedGoldenSchema(kind)),
            Determinism: ReadDeterminism(root.RequiredObject("determinism")),
            RequestIdRule: root.RequiredString("request_id_rule"),
            SolverOptions: ReadSolverOptions(root.RequiredObject("solver_options")),
            Tolerances: ReadTolerances(root.RequiredObject("tolerances")),
            Compatibility: ReadCompatibility(root.RequiredObject("compatibility")));
    }

    private static ReplayFieldSchema ReadSchema(ReplayJsonReader reader)
    {
        reader.RejectUnknownProperties(SchemaFields);
        return new ReplayFieldSchema(
            RequiredFields: reader.RequiredArray("required_fields", ReadStringArrayItem),
            OptionalFields: reader.RequiredArray("optional_fields", ReadStringArrayItem),
            DeprecatedFields: reader.RequiredArray("deprecated_fields", ReadStringArrayItem),
            ToleratedLegacyFields: reader.RequiredArray("tolerated_legacy_fields", ReadStringArrayItem));
    }

    private static ReplayFileReference ReadFileReference(
        ReplayJsonReader reader,
        string expectedSchemaVersion)
    {
        reader.RejectUnknownProperties(FileReferenceFields);
        var reference = new ReplayFileReference(
            Path: reader.RequiredString("path"),
            SchemaVersion: reader.RequiredString("schema_version"));

        if (!StringComparer.Ordinal.Equals(reference.SchemaVersion, expectedSchemaVersion))
        {
            throw new ReplayJsonException(
                "unsupported_schema_version",
                $"{reader.Path}.schema_version",
                $"Unsupported file schema '{reference.SchemaVersion}'.");
        }

        if (!IsValidReplayPath(reference.Path))
        {
            throw new ReplayJsonException(
                "invalid_relative_path",
                $"{reader.Path}.path",
                "Replay file paths must be relative to the manifest directory or repo:// references.");
        }

        return reference;
    }

    private static ReplayDeterminism ReadDeterminism(ReplayJsonReader reader)
    {
        reader.RejectUnknownProperties(DeterminismFields);
        return new ReplayDeterminism(
            Mode: reader.RequiredString("mode"),
            Seed: reader.OptionalInt32("seed"),
            Runtime: reader.RequiredString("runtime"),
            Numeric: reader.RequiredString("numeric"));
    }

    private static ReplaySolverOptions ReadSolverOptions(ReplayJsonReader reader)
    {
        reader.RejectUnknownProperties(SolverFields);
        return new ReplaySolverOptions(reader.RequiredString("optimizer"));
    }

    private static ReplayTolerances ReadTolerances(ReplayJsonReader reader)
    {
        reader.RejectUnknownProperties(ToleranceFields);
        var activePowerKwAbs = reader.RequiredFiniteDouble("active_power_kw_abs");
        var reactivePowerKvarAbs = reader.RequiredFiniteDouble("reactive_power_kvar_abs");
        if (activePowerKwAbs < 0 || reactivePowerKvarAbs < 0)
        {
            throw new ReplayJsonException(
                "invalid_tolerance",
                "$.tolerances",
                "Replay tolerances must be non-negative.");
        }

        return new ReplayTolerances(activePowerKwAbs, reactivePowerKvarAbs);
    }

    private static ReplayCompatibility ReadCompatibility(ReplayJsonReader reader)
    {
        reader.RejectUnknownProperties(CompatibilityFields);
        return new ReplayCompatibility(
            reader.RequiredArray("covers_legacy_cases", ReadStringArrayItem));
    }

    private static string ReadStringArrayItem(JsonElement item, string path)
    {
        if (item.ValueKind is not JsonValueKind.String)
        {
            throw new ReplayJsonException("invalid_type", path, "Expected string.");
        }

        return item.GetString() ?? string.Empty;
    }

    private static string ExpectedFixtureSchema(string kind) => kind switch
    {
        "telemetry-control-cycle" => ReplaySchemaVersions.TelemetryFixture,
        "native-control-parity" => ReplaySchemaVersions.NativeParityCases,
        "local-mpc-engine-comparison" => ReplaySchemaVersions.LocalMpcEngineComparison,
        "optimization-core-sidecar-comparison" => ReplaySchemaVersions.OptimizationCoreSidecarFixture,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported replay kind."),
    };

    private static string ExpectedGoldenSchema(string kind) => kind switch
    {
        "telemetry-control-cycle" => ReplaySchemaVersions.GoldenCommands,
        "native-control-parity" => ReplaySchemaVersions.NativeParityCases,
        "local-mpc-engine-comparison" => ReplaySchemaVersions.LocalMpcEngineComparison,
        "optimization-core-sidecar-comparison" => ReplaySchemaVersions.OptimizationCoreSidecarGolden,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported replay kind."),
    };

    private static bool IsValidReplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidate = path.StartsWith("repo://", StringComparison.Ordinal)
            ? path["repo://".Length..]
            : path;
        return !Path.IsPathRooted(candidate)
            && !candidate.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Contains("..", StringComparer.Ordinal);
    }
}
