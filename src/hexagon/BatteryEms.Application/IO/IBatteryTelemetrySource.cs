using BatteryEms.Domain;

namespace BatteryEms.Application.IO;

public interface IBatteryTelemetrySource
{
    IAsyncEnumerable<BatteryTelemetry> ReadAsync(CancellationToken cancellationToken);

    AdapterStatus Status { get; }
}
