using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Adapters.Mqtt.Tests;

public sealed class MqttRegistrationTests
{
    [Fact]
    public async Task AddBessMqtt_registers_telemetry_source_and_command_sink_sharing_one_client()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new MqttFixtures.FixedClock());
        services.AddSingleton(MqttFixtures.SampleAsset());

        services.AddBessMqtt(MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults());

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var source = provider.GetRequiredService<IBatteryTelemetrySource>();
            var sink = provider.GetRequiredService<IBatteryCommandSink>();
            var client1 = provider.GetRequiredService<IMqttClient>();
            var client2 = provider.GetRequiredService<IMqttClient>();

            Assert.IsType<MqttTelemetrySource>(source);
            Assert.IsType<MqttCommandSink>(sink);
            Assert.Same(client1, client2);
        }
    }

    [Fact]
    public void AddBessMqtt_throws_for_null_arguments()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            MqttRegistration.AddBessMqtt(null!, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults()));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddBessMqtt(null!, MqttFixtures.Defaults()));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddBessMqtt(MqttFixtures.SimulatorMapping(), null!));
    }
}
