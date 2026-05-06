using BatteryEms.Application.Markets;
using BatteryEms.Domain;

namespace BatteryEms.Application.Api;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class DefaultScheduleQuery : IScheduleQuery
{
    private readonly IScheduleRepository _repository;

    public DefaultScheduleQuery(IScheduleRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public IReadOnlyList<Schedule> FindCurrent(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return _repository.FindAll(assetId).ToArray();
    }
}
