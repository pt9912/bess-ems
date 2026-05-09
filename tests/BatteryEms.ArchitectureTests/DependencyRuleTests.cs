using NetArchTest.Rules;
using Xunit;

namespace BatteryEms.ArchitectureTests;

public sealed class DependencyRuleTests
{
    [Fact]
    public void Domain_does_not_depend_on_application_adapters_or_infrastructure()
    {
        var result = Types.InAssembly(ProjectAssemblies.Domain)
            .Should()
            .NotHaveDependencyOnAny(
                ProjectAssemblies.ApplicationNamespace,
                ProjectAssemblies.ApiNamespace,
                ProjectAssemblies.WorkerNamespace,
                ProjectAssemblies.ModbusNamespace,
                ProjectAssemblies.MqttNamespace,
                ProjectAssemblies.PersistenceNamespace,
                ProjectAssemblies.TelemetryNamespace,
                ProjectAssemblies.OptimizationNamespace,
                ProjectAssemblies.NativeInteropNamespace,
                ProjectAssemblies.InfrastructureNamespace,
                ProjectAssemblies.HostNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, ArchitectureTestHelpers.FormatFailures(result));
    }

    [Fact]
    public void Application_does_not_depend_on_adapters_or_infrastructure()
    {
        var result = Types.InAssembly(ProjectAssemblies.Application)
            .Should()
            .NotHaveDependencyOnAny(
                ProjectAssemblies.ApiNamespace,
                ProjectAssemblies.WorkerNamespace,
                ProjectAssemblies.ModbusNamespace,
                ProjectAssemblies.MqttNamespace,
                ProjectAssemblies.PersistenceNamespace,
                ProjectAssemblies.TelemetryNamespace,
                ProjectAssemblies.OptimizationNamespace,
                ProjectAssemblies.NativeInteropNamespace,
                ProjectAssemblies.InfrastructureNamespace,
                ProjectAssemblies.HostNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, ArchitectureTestHelpers.FormatFailures(result));
    }

    [Theory]
    [InlineData(nameof(ProjectAssemblies.Api))]
    [InlineData(nameof(ProjectAssemblies.Worker))]
    public void Driving_adapter_does_not_depend_on_other_driving_or_any_driven_or_infrastructure(string adapterName)
    {
        var (subject, forbidden) = adapterName switch
        {
            nameof(ProjectAssemblies.Api) => (ProjectAssemblies.Api, new[]
            {
                ProjectAssemblies.WorkerNamespace,
                ProjectAssemblies.ModbusNamespace,
                ProjectAssemblies.MqttNamespace,
                ProjectAssemblies.PersistenceNamespace,
                ProjectAssemblies.TelemetryNamespace,
                ProjectAssemblies.OptimizationNamespace,
                ProjectAssemblies.NativeInteropNamespace,
                ProjectAssemblies.InfrastructureNamespace,
                ProjectAssemblies.HostNamespace,
            }),
            nameof(ProjectAssemblies.Worker) => (ProjectAssemblies.Worker, new[]
            {
                ProjectAssemblies.ApiNamespace,
                ProjectAssemblies.ModbusNamespace,
                ProjectAssemblies.MqttNamespace,
                ProjectAssemblies.PersistenceNamespace,
                ProjectAssemblies.TelemetryNamespace,
                ProjectAssemblies.OptimizationNamespace,
                ProjectAssemblies.NativeInteropNamespace,
                ProjectAssemblies.InfrastructureNamespace,
                ProjectAssemblies.HostNamespace,
            }),
            _ => throw new System.ArgumentOutOfRangeException(nameof(adapterName), adapterName, "Unknown driving adapter."),
        };

        var result = Types.InAssembly(subject)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful, ArchitectureTestHelpers.FormatFailures(result));
    }

    [Theory]
    [InlineData(nameof(ProjectAssemblies.Modbus))]
    [InlineData(nameof(ProjectAssemblies.Mqtt))]
    [InlineData(nameof(ProjectAssemblies.Persistence))]
    [InlineData(nameof(ProjectAssemblies.Telemetry))]
    [InlineData(nameof(ProjectAssemblies.Optimization))]
    [InlineData(nameof(ProjectAssemblies.NativeInterop))]
    public void Driven_adapter_does_not_depend_on_driving_other_driven_or_infrastructure(string adapterName)
    {
        var allDriven = new (string Name, string Namespace)[]
        {
            (nameof(ProjectAssemblies.Modbus), ProjectAssemblies.ModbusNamespace),
            (nameof(ProjectAssemblies.Mqtt), ProjectAssemblies.MqttNamespace),
            (nameof(ProjectAssemblies.Persistence), ProjectAssemblies.PersistenceNamespace),
            (nameof(ProjectAssemblies.Telemetry), ProjectAssemblies.TelemetryNamespace),
            (nameof(ProjectAssemblies.Optimization), ProjectAssemblies.OptimizationNamespace),
            (nameof(ProjectAssemblies.NativeInterop), ProjectAssemblies.NativeInteropNamespace),
        };

        var subject = adapterName switch
        {
            nameof(ProjectAssemblies.Modbus) => ProjectAssemblies.Modbus,
            nameof(ProjectAssemblies.Mqtt) => ProjectAssemblies.Mqtt,
            nameof(ProjectAssemblies.Persistence) => ProjectAssemblies.Persistence,
            nameof(ProjectAssemblies.Telemetry) => ProjectAssemblies.Telemetry,
            nameof(ProjectAssemblies.Optimization) => ProjectAssemblies.Optimization,
            nameof(ProjectAssemblies.NativeInterop) => ProjectAssemblies.NativeInterop,
            _ => throw new System.ArgumentOutOfRangeException(nameof(adapterName), adapterName, "Unknown driven adapter."),
        };

        var forbidden = new System.Collections.Generic.List<string>
        {
            ProjectAssemblies.ApiNamespace,
            ProjectAssemblies.WorkerNamespace,
            ProjectAssemblies.InfrastructureNamespace,
            ProjectAssemblies.HostNamespace,
        };
        forbidden.AddRange(System.Linq.Enumerable.Select(
            System.Linq.Enumerable.Where(allDriven, d => d.Name != adapterName),
            d => d.Namespace));

        var result = Types.InAssembly(subject)
            .Should()
            .NotHaveDependencyOnAny(forbidden.ToArray())
            .GetResult();

        Assert.True(result.IsSuccessful, ArchitectureTestHelpers.FormatFailures(result));
    }
}
