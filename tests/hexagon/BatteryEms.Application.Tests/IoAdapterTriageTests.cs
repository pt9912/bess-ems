using BatteryEms.Application.IO;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class IoAdapterTriageTests
{
    [Fact]
    public void None_configured_returns_none()
    {
        var family = IoAdapterTriage.SelectConfiguredFamily(false, false, false);
        Assert.Equal(IoAdapterTriage.Family.None, family);
    }

    [Theory]
    [InlineData(true, false, false, IoAdapterTriage.Family.Modbus)]
    [InlineData(false, true, false, IoAdapterTriage.Family.Mqtt)]
    [InlineData(false, false, true, IoAdapterTriage.Family.OpcUa)]
    public void Single_family_configured_returns_that_family(
        bool modbus, bool mqtt, bool opcua, IoAdapterTriage.Family expected)
    {
        Assert.Equal(expected,
            IoAdapterTriage.SelectConfiguredFamily(modbus, mqtt, opcua));
    }

    // Plan-RM-M4-04 §4 Sub-Slice C fail-closed pin: Mehrfach-
    // Konfiguration wirft mit kebab-case-Reason und der Liste der
    // erkannten Familien. Die Reason-String-Form ist Vertrag — der
    // Operator soll im Log sofort sehen welche zwei (oder drei)
    // Adapter er konfiguriert hat.
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Multiple_families_configured_throws_multiple_io_adapters_configured(
        bool modbus, bool mqtt, bool opcua)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            IoAdapterTriage.SelectConfiguredFamily(modbus, mqtt, opcua));
        Assert.Contains("multiple-io-adapters-configured",
            ex.Message, StringComparison.Ordinal);
        // The reason carries the configured families in lowercase.
        if (modbus) { Assert.Contains("modbus", ex.Message, StringComparison.Ordinal); }
        if (mqtt) { Assert.Contains("mqtt", ex.Message, StringComparison.Ordinal); }
        if (opcua) { Assert.Contains("opcua", ex.Message, StringComparison.Ordinal); }
    }
}
