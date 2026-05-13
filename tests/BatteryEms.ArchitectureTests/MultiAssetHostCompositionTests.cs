using BatteryEms.Application.Assets;
using BatteryEms.Host;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.ArchitectureTests;

public sealed class MultiAssetHostCompositionTests
{
    [Fact]
    public async Task Host_build_with_multi_asset_noop_io_seeds_all_assets()
    {
        await using var app = BessHostBuilder.BuildApp(
        [
            $"--Bess:SchemaDirectory={RepoPath("config", "schema")}",
            $"--Bess:AssetConfigPath={RepoPath("config", "examples", "assets.multi-bess.json")}",
        ]);

        var registry = app.Services.GetRequiredService<IBatteryAssetRegistry>();
        var assetIds = registry.GetAll()
            .Select(a => a.AssetId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["bess-a", "bess-b"], assetIds);
    }

    [Fact]
    public void Host_build_rejects_multi_asset_config_with_concrete_io_adapter()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BessHostBuilder.BuildApp(
            [
                $"--Bess:SchemaDirectory={RepoPath("config", "schema")}",
                $"--Bess:AssetConfigPath={RepoPath("config", "examples", "assets.multi-bess.json")}",
                $"--Bess:ModbusMappingPath={RepoPath("config", "examples", "adapters", "modbus.simulator.json")}",
                "--Bess:ModbusHost=127.0.0.1",
                "--Bess:ModbusPort=1502",
            ]));

        Assert.Contains("requires exactly one asset", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Multi-asset configs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_build_accepts_mqtt_plaintext_only_with_test_profile_ack()
    {
        await using var app = BessHostBuilder.BuildApp(
        [
            $"--Bess:SchemaDirectory={RepoPath("config", "schema")}",
            $"--Bess:AssetConfigPath={RepoPath("config", "examples", "asset.single-bess.json")}",
            $"--Bess:MqttMappingPath={RepoPath("config", "examples", "adapters", "mqtt.simulator.json")}",
            "--Bess:MqttBrokerHost=127.0.0.1",
            "--Bess:MqttBrokerPort=1883",
            "--Bess:MqttClientId=test-host",
            "--Bess:MqttRuntimeProfile=HilSimulator",
            "--Bess:MqttAllowPlaintext=true",
            "--Bess:MqttAllowPlaintextReason=architecture-test",
        ]);

        var registry = app.Services.GetRequiredService<IBatteryAssetRegistry>();
        Assert.NotNull(registry.Find("single-bess-1"));
    }

    [Fact]
    public void Host_build_rejects_default_production_mqtt_plaintext()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BessHostBuilder.BuildApp(
            [
                $"--Bess:SchemaDirectory={RepoPath("config", "schema")}",
                $"--Bess:AssetConfigPath={RepoPath("config", "examples", "asset.single-bess.json")}",
                $"--Bess:MqttMappingPath={RepoPath("config", "examples", "adapters", "mqtt.simulator.json")}",
                "--Bess:MqttBrokerHost=127.0.0.1",
                "--Bess:MqttBrokerPort=1883",
                "--Bess:MqttClientId=test-host",
            ]));

        Assert.Contains("mqtt-security-not-hardened-in-production", ex.Message, StringComparison.Ordinal);
    }

    private static string RepoPath(params string[] parts) =>
        Path.Combine([RepoRoot(), .. parts]);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "BatteryEms.sln")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing BatteryEms.sln.");
    }
}
