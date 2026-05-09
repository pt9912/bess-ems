using System.Reflection;

namespace BatteryEms.ArchitectureTests;

internal static class ProjectAssemblies
{
    public static readonly Assembly Domain = typeof(BatteryEms.Domain.AssemblyMarker).Assembly;
    public static readonly Assembly Application = typeof(BatteryEms.Application.AssemblyMarker).Assembly;
    public static readonly Assembly Api = typeof(BatteryEms.Api.AssemblyMarker).Assembly;
    public static readonly Assembly Worker = typeof(BatteryEms.Worker.AssemblyMarker).Assembly;
    public static readonly Assembly Modbus = typeof(BatteryEms.Adapters.Modbus.AssemblyMarker).Assembly;
    public static readonly Assembly Mqtt = typeof(BatteryEms.Adapters.Mqtt.AssemblyMarker).Assembly;
    public static readonly Assembly Persistence = typeof(BatteryEms.Adapters.Persistence.AssemblyMarker).Assembly;
    public static readonly Assembly Telemetry = typeof(BatteryEms.Adapters.Telemetry.AssemblyMarker).Assembly;
    public static readonly Assembly Optimization = typeof(BatteryEms.Adapters.Optimization.AssemblyMarker).Assembly;
    public static readonly Assembly NativeInterop = typeof(BatteryEms.Adapters.NativeInterop.AssemblyMarker).Assembly;
    public static readonly Assembly Infrastructure = typeof(BatteryEms.Infrastructure.AssemblyMarker).Assembly;
    public static readonly Assembly Host = typeof(BatteryEms.Host.AssemblyMarker).Assembly;

    public const string DomainNamespace = "BatteryEms.Domain";
    public const string ApplicationNamespace = "BatteryEms.Application";
    public const string ApiNamespace = "BatteryEms.Api";
    public const string WorkerNamespace = "BatteryEms.Worker";
    public const string ModbusNamespace = "BatteryEms.Adapters.Modbus";
    public const string MqttNamespace = "BatteryEms.Adapters.Mqtt";
    public const string PersistenceNamespace = "BatteryEms.Adapters.Persistence";
    public const string TelemetryNamespace = "BatteryEms.Adapters.Telemetry";
    public const string OptimizationNamespace = "BatteryEms.Adapters.Optimization";
    public const string NativeInteropNamespace = "BatteryEms.Adapters.NativeInterop";
    public const string InfrastructureNamespace = "BatteryEms.Infrastructure";
    public const string HostNamespace = "BatteryEms.Host";
}
