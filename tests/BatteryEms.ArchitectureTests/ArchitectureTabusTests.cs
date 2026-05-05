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
}
