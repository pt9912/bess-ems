using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class RegelleistungActivationTests
{
    private static readonly DateTimeOffset SignalTime =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ValidFrom = SignalTime;
    private static readonly DateTimeOffset ValidUntil = SignalTime + TimeSpan.FromMinutes(15);

    private static RegelleistungActivation Make(
        ReserveProduct product = ReserveProduct.Afrr,
        ReserveDirection direction = ReserveDirection.Up,
        double powerKw = 25,
        long sequenceNumber = 1,
        string sourceId = "tso-source-1",
        string activationId = "act-1",
        string payloadHash = "sha256:deadbeef")
        => new(
            sourceId,
            activationId,
            sequenceNumber,
            SignalTime,
            product,
            direction,
            powerKw,
            ValidFrom,
            ValidUntil,
            payloadHash);

    [Fact]
    public void Construction_with_valid_fields_succeeds()
    {
        var activation = Make();

        Assert.Equal("tso-source-1", activation.SourceId);
        Assert.Equal("act-1", activation.ActivationId);
        Assert.Equal(1, activation.SequenceNumber);
        Assert.Equal(SignalTime, activation.SignalTimestampUtc);
        Assert.Equal(ReserveProduct.Afrr, activation.Product);
        Assert.Equal(ReserveDirection.Up, activation.Direction);
        Assert.Equal(25, activation.PowerKw);
        Assert.Equal(ValidFrom, activation.ValidFrom);
        Assert.Equal(ValidUntil, activation.ValidUntil);
        Assert.Equal("sha256:deadbeef", activation.PayloadHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_or_blank_source_id_throws(string sourceId)
    {
        Assert.Throws<ArgumentException>(() => Make(sourceId: sourceId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_or_blank_activation_id_throws(string activationId)
    {
        Assert.Throws<ArgumentException>(() => Make(activationId: activationId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_or_blank_payload_hash_throws(string payloadHash)
    {
        Assert.Throws<ArgumentException>(() => Make(payloadHash: payloadHash));
    }

    [Fact]
    public void Negative_sequence_number_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(sequenceNumber: -1));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_power_throws(double powerKw)
    {
        Assert.Throws<ArgumentException>(() => Make(powerKw: powerKw));
    }

    [Fact]
    public void Negative_power_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(powerKw: -1));
    }

    [Fact]
    public void Zero_power_is_accepted()
    {
        var activation = Make(powerKw: 0);
        Assert.Equal(0, activation.PowerKw);
    }

    [Fact]
    public void Valid_from_must_be_before_valid_until()
    {
        Assert.Throws<ArgumentException>(() => new RegelleistungActivation(
            "src", "act", 0, SignalTime,
            ReserveProduct.Afrr, ReserveDirection.Up, 10,
            ValidUntil, ValidFrom, "h"));
        Assert.Throws<ArgumentException>(() => new RegelleistungActivation(
            "src", "act", 0, SignalTime,
            ReserveProduct.Afrr, ReserveDirection.Up, 10,
            ValidFrom, ValidFrom, "h"));
    }

    [Theory]
    [InlineData(ReserveProduct.Fcr, ReserveDirection.Symmetric)]
    [InlineData(ReserveProduct.Afrr, ReserveDirection.Up)]
    [InlineData(ReserveProduct.Afrr, ReserveDirection.Down)]
    [InlineData(ReserveProduct.Mfrr, ReserveDirection.Up)]
    [InlineData(ReserveProduct.Mfrr, ReserveDirection.Down)]
    public void Product_direction_combinations_accepted(
        ReserveProduct product,
        ReserveDirection direction)
    {
        var activation = Make(product: product, direction: direction);
        Assert.Equal(product, activation.Product);
        Assert.Equal(direction, activation.Direction);
    }

    [Theory]
    [InlineData(ReserveProduct.Fcr, ReserveDirection.Up)]
    [InlineData(ReserveProduct.Fcr, ReserveDirection.Down)]
    [InlineData(ReserveProduct.Afrr, ReserveDirection.Symmetric)]
    [InlineData(ReserveProduct.Mfrr, ReserveDirection.Symmetric)]
    public void Product_direction_mismatches_throw(
        ReserveProduct product,
        ReserveDirection direction)
    {
        Assert.Throws<ArgumentException>(() => Make(product: product, direction: direction));
    }

    [Fact]
    public void Covers_validity_uses_half_open_window()
    {
        var activation = Make();

        Assert.True(activation.CoversValidity(ValidFrom));
        Assert.True(activation.CoversValidity(ValidFrom + TimeSpan.FromMinutes(7)));
        Assert.False(activation.CoversValidity(ValidUntil));
        Assert.False(activation.CoversValidity(ValidFrom - TimeSpan.FromSeconds(1)));
    }
}
