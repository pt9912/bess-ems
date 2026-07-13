using System.Text.Json.Nodes;
using BatteryEms.Application.Configuration;
using Xunit;

namespace BatteryEms.Adapters.Modbus.Tests;

// ADR 0013 §5.4 sub-slice 2: two directions on the Modbus golden vectors.
//  (1) Drift gate: the committed per-profile manifests must structurally
//      equal the freshly lifted ones (codec or profile change without a
//      vector refresh fails here). Comparison is numeric-structural, not
//      textual — 99 and 99.0 are the same number (ADR 0013 §3).
//  (2) Decode round-trip for READ AND WRITE cases: Decode(words) == value
//      exactly. Write words come from the real ModbusCommandSink dispatch;
//      without this guard a value whose scale division truncates would
//      produce a manifest that is internally wrong while the drift test
//      stays green (both sides equally wrong — plan-review finding 2).
public sealed class ModbusGoldenVectorsTests
{
    public static TheoryData<string> Profiles()
    {
        var data = new TheoryData<string>();
        foreach (var profile in ModbusGoldenVectors.ProfileFiles)
        {
            data.Add(profile);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task Committed_manifest_matches_generator_no_drift(string profileFile)
    {
        var committed = JsonNode.Parse(File.ReadAllText(ModbusGoldenVectors.ManifestPath(profileFile)));
        var regeneratedJson = await ModbusGoldenVectors.GenerateManifestJsonAsync(profileFile);
        var regenerated = JsonNode.Parse(regeneratedJson);

        Assert.True(
            StructurallyEqual(committed, regenerated),
            $"config/schema/vectors/modbus-golden-vectors.{ModbusGoldenVectors.ProfileKey(profileFile)}.v1.json "
            + "is out of sync with the codec/profile; replace its content with:\n" + regeneratedJson);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Decode_round_trip_is_exact_for_every_case(string profileFile)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(ModbusGoldenVectors.ManifestPath(profileFile)))!.AsObject();
        var cases = manifest["cases"]!.AsArray();
        Assert.NotEmpty(cases);

        foreach (var node in cases)
        {
            var vectorCase = node!.AsObject();
            var mapping = CaseMapping(vectorCase);
            var words = vectorCase["words"]!.AsArray()
                .Select(w => (ushort)w!.GetValue<int>())
                .ToArray();

            var decoded = RegisterDecoder.Decode(mapping, words);
            Assert.True(
                decoded == vectorCase["value"]!.GetValue<double>(),
                $"case {vectorCase["name"]}: Decode(words) == {decoded}, manifest value == "
                + $"{vectorCase["value"]} — raw-value exactness rule violated (plan decision 3)");
        }
    }

    [Fact]
    public void Every_published_modbus_manifest_has_a_codec_gate()
    {
        // Second-review finding 2: python's REQUIRED_VECTOR_MANIFESTS is a
        // presence floor, not a codec gate — a manifest added there but not
        // here would ship published-but-ungated. This pin makes the two
        // sets mechanically equal: publishing a new modbus manifest REQUIRES
        // wiring it into ProfileFiles (and thereby into drift + round-trip).
        var published = Directory.GetFiles(ModbusGoldenVectors.VectorsDir(), "modbus-golden-vectors.*.v1.json")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        var gated = ModbusGoldenVectors.ProfileFiles
            .Select(p => Path.GetFileName(ModbusGoldenVectors.ManifestPath(p)))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(gated, published);
    }

    [Fact]
    public void Case_set_covers_every_profile_register_exactly_once()
    {
        foreach (var profileFile in ModbusGoldenVectors.ProfileFiles)
        {
            var mapping = ModbusGoldenVectors.LoadProfile(profileFile);
            var manifest = JsonNode.Parse(File.ReadAllText(ModbusGoldenVectors.ManifestPath(profileFile)))!.AsObject();
            var byRegister = manifest["cases"]!.AsArray()
                .Select(c => c!.AsObject())
                .ToDictionary(c => c["register"]!.GetValue<string>());

            // string registers carry no vectors by schema promise.
            var vectorised = mapping.Registers.Where(r => r.Type != "string").ToList();
            Assert.Equal(vectorised.Count, byRegister.Count);
            foreach (var register in vectorised)
            {
                var vectorCase = byRegister[register.Name];
                Assert.Equal(register.Writable ? "write" : "read", vectorCase["direction"]!.GetValue<string>());
                Assert.Equal(register.Address, vectorCase["address"]!.GetValue<int>());
            }
        }
    }

    // Rebuilds a minimal mapping record from the case's RESOLVED fields —
    // the decode round-trip must hold from the manifest alone, exactly what
    // an external consumer of the published bundle would do.
    private static ModbusRegisterMapping CaseMapping(JsonObject vectorCase) => new(
        Name: vectorCase["register"]!.GetValue<string>(),
        Address: vectorCase["address"]!.GetValue<int>(),
        Type: vectorCase["type"]!.GetValue<string>(),
        ScaleFactor: vectorCase["scale_factor"]!.GetValue<double>(),
        RangeMin: double.MinValue,
        RangeMax: double.MaxValue,
        Writable: vectorCase["direction"]!.GetValue<string>() == "write",
        WriteCadence: "cyclic",
        AuthRequired: "none",
        Enum: null,
        FirmwareConstraint: null,
        SunspecModel: null)
    {
        RegisterTable = vectorCase["register_table"]!.GetValue<string>(),
        WordOrder = vectorCase["word_order"]!.GetValue<string>(),
    };

    // Numeric-structural equality (member order irrelevant, numbers compared
    // as numbers): the committed file may write 99.0 where the generator
    // writes 99 — the contract is field-normative, not byte-normative.
    private static bool StructurallyEqual(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (a is JsonObject objA && b is JsonObject objB)
        {
            if (objA.Count != objB.Count)
            {
                return false;
            }

            foreach (var (key, valueA) in objA)
            {
                if (!objB.TryGetPropertyValue(key, out var valueB) || !StructurallyEqual(valueA, valueB))
                {
                    return false;
                }
            }

            return true;
        }

        if (a is JsonArray arrA && b is JsonArray arrB)
        {
            return arrA.Count == arrB.Count && arrA.Zip(arrB).All(pair => StructurallyEqual(pair.First, pair.Second));
        }

        if (a is JsonValue valA && b is JsonValue valB)
        {
            if (valA.TryGetValue<double>(out var numA) && valB.TryGetValue<double>(out var numB))
            {
                return numA == numB;
            }

            if (valA.TryGetValue<bool>(out var boolA) && valB.TryGetValue<bool>(out var boolB))
            {
                return boolA == boolB;
            }

            return valA.TryGetValue<string>(out var strA)
                && valB.TryGetValue<string>(out var strB)
                && string.Equals(strA, strB, StringComparison.Ordinal);
        }

        return false;
    }
}
