using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Adapters.Modbus.Tests;

public sealed class ModbusRegistrationTests
{
    [Fact]
    public async Task AddBessModbus_registers_telemetry_source_and_command_sink_sharing_one_client()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new ModbusFixtures.FixedClock());
        services.AddSingleton(ModbusFixtures.SampleAsset());

        services.AddBessModbus(ModbusFixtures.VendorNeutralMapping(), ModbusFixtures.Defaults());

        // FluentModbusClient is IAsyncDisposable, so the container has
        // to be disposed asynchronously — DisposeAsync, not Dispose.
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var source = provider.GetRequiredService<IBatteryTelemetrySource>();
            var sink = provider.GetRequiredService<IBatteryCommandSink>();
            var client1 = provider.GetRequiredService<IModbusClient>();
            var client2 = provider.GetRequiredService<IModbusClient>();

            Assert.IsType<ModbusTelemetrySource>(source);
            Assert.IsType<ModbusCommandSink>(sink);
            Assert.Same(client1, client2);  // Singleton — same connection across source + sink.
        }
    }

    [Fact]
    public void AddBessModbus_throws_for_null_arguments()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            ModbusRegistration.AddBessModbus(null!, ModbusFixtures.VendorNeutralMapping(), ModbusFixtures.Defaults()));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddBessModbus(null!, ModbusFixtures.Defaults()));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddBessModbus(ModbusFixtures.VendorNeutralMapping(), null!));
    }
}
