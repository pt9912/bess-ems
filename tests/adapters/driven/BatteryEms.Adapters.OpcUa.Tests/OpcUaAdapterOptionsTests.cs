using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.OpcUa.Tests;

public sealed class OpcUaAdapterOptionsTests
{
    private static readonly NullLogger<OpcUaAdapterOptions> Logger =
        NullLogger<OpcUaAdapterOptions>.Instance;

    private static OpcUaAdapterOptions ValidUnsecuredOptions() => new()
    {
        EndpointUrl = new Uri("opc.tcp://localhost:4840"),
        AllowUnsecured = true,
        AllowUnsecuredReason = "hil-simulator-pre-m4-05",
    };

    [Fact]
    public void Defaults_pin_master_dod_values()
    {
        var options = ValidUnsecuredOptions();

        Assert.Equal("bess-ems", options.SessionName);
        Assert.Equal(TimeSpan.FromSeconds(15), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ReadTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), options.ReconnectBackoffStart);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ReconnectBackoffMax);
        Assert.Equal(1000, options.DefaultMonitoringIntervalMs);
        Assert.Equal(256, options.SubscriptionChannelCapacity);
        Assert.Equal(OpcUaSecurityMode.None, options.SecurityMode);
    }

    [Fact]
    public void Null_endpoint_url_throws()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = null!,
            AllowUnsecured = true,
            AllowUnsecuredReason = "test",
        };

        Assert.Throws<ArgumentNullException>(() => options.EnsureValid(Logger));
    }

    // Plan §6 D-04 Pin (a): Default-Options (SecurityMode=None,
    // AllowUnsecured=false) → EnsureValid throws opcua-security-not-hardened.
    [Fact]
    public void Default_security_throws_opcua_security_not_hardened()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            // AllowUnsecured=false (default), AllowUnsecuredReason=null (default)
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
        Assert.Contains("opcua-security-not-hardened", ex.Message, StringComparison.Ordinal);
    }

    // Plan §6 D-04 Pin (b): AllowUnsecured=true + leerer Reason → throws.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Allow_unsecured_without_reason_throws_opcua_security_not_hardened(string? reason)
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            AllowUnsecured = true,
            AllowUnsecuredReason = reason,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
        Assert.Contains("opcua-security-not-hardened", ex.Message, StringComparison.Ordinal);
    }

    // Plan §6 D-04 Pin (c): AllowUnsecured=true + non-empty Reason →
    // EnsureValid lets through; ILogger sees the structured Warning.
    [Fact]
    public void Allow_unsecured_with_reason_is_accepted_and_logs_warning()
    {
        var spy = new SpyLogger();
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            AllowUnsecured = true,
            AllowUnsecuredReason = "hil-simulator-pre-m4-05",
        };

        var result = options.EnsureValid(spy);

        Assert.Same(options, result);
        Assert.Single(spy.Records);
        var rec = spy.Records[0];
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, rec.Level);
        Assert.Equal(4200, rec.EventId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_default_monitoring_interval_throws(int interval)
    {
        var options = ValidUnsecuredOptions() with { DefaultMonitoringIntervalMs = interval };
        Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
    }

    [Fact]
    public void Reconnect_backoff_max_smaller_than_start_throws()
    {
        var options = ValidUnsecuredOptions() with
        {
            ReconnectBackoffStart = TimeSpan.FromSeconds(10),
            ReconnectBackoffMax = TimeSpan.FromSeconds(1),
        };
        Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
    }

    [Fact]
    public void Null_logger_throws()
    {
        var options = ValidUnsecuredOptions();
        Assert.Throws<ArgumentNullException>(() => options.EnsureValid(null!));
    }

    private sealed class SpyLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<Record> Records { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullLogger.Instance.BeginScope(state)!;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Records.Add(new Record(logLevel, eventId.Id, formatter(state, exception)));
        }

        public sealed record Record(
            Microsoft.Extensions.Logging.LogLevel Level,
            int EventId,
            string Message);
    }
}
