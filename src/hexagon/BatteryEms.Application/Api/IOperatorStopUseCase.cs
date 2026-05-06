using BatteryEms.Application.Control;

namespace BatteryEms.Application.Api;

// LH-API-006 driving port. The API binds an HTTP shape to this method;
// the use case mediates between the request and the IOperatorStopRegistry
// so the audit hook (RM-M1-16) can sit cleanly on top of the same call.
public interface IOperatorStopUseCase
{
    OperatorStopState Execute(OperatorStopRequest request);
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OperatorStopRequest(string AssetId, string Operator, string Reason);
