using NetArchTest.Rules;
using Xunit;

namespace BatteryEms.ArchitectureTests;

public sealed class ArchitectureTabusTests
{
    private static readonly string[] FrameworkTaboosForHexagon =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.Http",
        "Npgsql",
        "MQTTnet",
        "NModbus",
        "Opc.Ua",
        "OpenTelemetry",
        "Serilog",
        "Grpc",
        "System.Net.Http",
    ];

    [Fact]
    public void Domain_does_not_reference_framework_packages()
    {
        var result = Types.InAssembly(ProjectAssemblies.Domain)
            .Should()
            .NotHaveDependencyOnAny(FrameworkTaboosForHexagon)
            .GetResult();

        Assert.True(result.IsSuccessful, ArchitectureTestHelpers.FormatFailures(result));
    }

    [Fact]
    public void Application_does_not_reference_framework_packages()
    {
        var result = Types.InAssembly(ProjectAssemblies.Application)
            .Should()
            .NotHaveDependencyOnAny(FrameworkTaboosForHexagon)
            .GetResult();

        Assert.True(result.IsSuccessful, ArchitectureTestHelpers.FormatFailures(result));
    }

    // LH-OPT-007: horizon-level optimisation and the regulation-cycle
    // dispatcher must stay structurally separated. The schedule
    // optimiser produces a versioned Domain.Schedule that the
    // IScheduleTracker / IDispatchOptimizer chain consumes off-cycle;
    // the dispatcher computes a per-tick setpoint inside the 1-Hz
    // regulation loop. Letting either side import the other's types
    // would collapse the two pipelines and is the failure mode this
    // test guards against.
    [Fact]
    public void Schedule_optimizer_types_do_not_depend_on_dispatch_types()
    {
        var result = Types.InAssembly(ProjectAssemblies.Application)
            .That()
            .HaveNameStartingWith("ScheduleOptimization")
            .Or()
            .HaveName("IScheduleOptimizer")
            .Should()
            .NotHaveDependencyOnAny(
                "BatteryEms.Application.Optimization.IDispatchOptimizer",
                "BatteryEms.Application.Optimization.DispatchRequest",
                "BatteryEms.Application.Optimization.DispatchResult")
            .GetResult();

        Assert.True(result.IsSuccessful, ArchitectureTestHelpers.FormatFailures(result));
    }

    [Fact]
    public void Dispatch_optimizer_types_do_not_depend_on_schedule_types()
    {
        var result = Types.InAssembly(ProjectAssemblies.Application)
            .That()
            .HaveNameStartingWith("Dispatch")
            .Or()
            .HaveName("IDispatchOptimizer")
            .Should()
            .NotHaveDependencyOnAny(
                "BatteryEms.Application.Optimization.IScheduleOptimizer",
                "BatteryEms.Application.Optimization.ScheduleOptimizationRequest",
                "BatteryEms.Application.Optimization.ScheduleOptimizationResult")
            .GetResult();

        Assert.True(result.IsSuccessful, ArchitectureTestHelpers.FormatFailures(result));
    }

    // RM-M3-FUP-02: ScheduleConcurrencyConflictException is a domain
    // exception that lives in BatteryEms.Application.Markets, is thrown
    // by BatteryEms.Adapters.Persistence and caught by
    // BatteryEms.Application.Api (the use-case layer). It must not
    // leak into any other module — driving adapters mapping HTTP
    // responses, the dispatch path, the worker, or other driven
    // adapters all stay unaware of the typed exception. If a future
    // need to reference it from outside those three modules arises,
    // promote a domain-result type instead of widening this exception.
    [Fact]
    public void Schedule_concurrency_conflict_exception_only_referenced_from_application_and_persistence()
    {
        const string exceptionType =
            "BatteryEms.Application.Markets.ScheduleConcurrencyConflictException";

        var modulesThatMustNotReference = new[]
        {
            ProjectAssemblies.Domain,
            ProjectAssemblies.Api,
            ProjectAssemblies.Worker,
            ProjectAssemblies.Modbus,
            ProjectAssemblies.Mqtt,
            ProjectAssemblies.Telemetry,
            ProjectAssemblies.Optimization,
            ProjectAssemblies.NativeInterop,
            ProjectAssemblies.Infrastructure,
            ProjectAssemblies.Host,
        };

        foreach (var assembly in modulesThatMustNotReference)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn(exceptionType)
                .GetResult();

            Assert.True(result.IsSuccessful,
                $"{assembly.GetName().Name}: {ArchitectureTestHelpers.FormatFailures(result)}");
        }
    }
}
