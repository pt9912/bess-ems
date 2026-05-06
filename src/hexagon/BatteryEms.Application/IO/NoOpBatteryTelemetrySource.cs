using BatteryEms.Domain;

namespace BatteryEms.Application.IO;

// Default IBatteryTelemetrySource for hosts that have not yet wired a
// real Modbus/MQTT source. ReadAsync yields no telemetry, so the
// regulation cycle observes 'no-snapshot' and emits SafeStop — the
// safe baseline behaviour mandated by LH-CTRL-007.
public sealed class NoOpBatteryTelemetrySource : IBatteryTelemetrySource
{
    public AdapterStatus Status => AdapterStatus.Disconnected;

#pragma warning disable CS1998 // The empty async iterator is intentional — the no-op source
    public async IAsyncEnumerable<BatteryTelemetry> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }
#pragma warning restore CS1998
}
