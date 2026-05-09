using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class ReserveBandTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fcr_with_symmetric_direction_is_accepted()
    {
        var band = new ReserveBand(
            "asset-1",
            ReserveProduct.Fcr,
            ReserveDirection.Symmetric,
            Start,
            Start + TimeSpan.FromMinutes(15),
            5);

        Assert.Equal("asset-1", band.AssetId);
        Assert.Equal(ReserveProduct.Fcr, band.Product);
        Assert.Equal(ReserveDirection.Symmetric, band.Direction);
        Assert.Equal(5, band.PowerKw);
        Assert.Equal(TimeSpan.FromMinutes(15), band.Duration);
    }

    [Theory]
    [InlineData(ReserveProduct.Afrr, ReserveDirection.Up)]
    [InlineData(ReserveProduct.Afrr, ReserveDirection.Down)]
    [InlineData(ReserveProduct.Mfrr, ReserveDirection.Up)]
    [InlineData(ReserveProduct.Mfrr, ReserveDirection.Down)]
    public void Afrr_and_mfrr_with_up_or_down_direction_are_accepted(
        ReserveProduct product,
        ReserveDirection direction)
    {
        var band = new ReserveBand(
            "asset-1", product, direction,
            Start, Start + TimeSpan.FromMinutes(15), 10);

        Assert.Equal(product, band.Product);
        Assert.Equal(direction, band.Direction);
    }

    [Theory]
    [InlineData(ReserveProduct.Fcr, ReserveDirection.Up)]
    [InlineData(ReserveProduct.Fcr, ReserveDirection.Down)]
    [InlineData(ReserveProduct.Afrr, ReserveDirection.Symmetric)]
    [InlineData(ReserveProduct.Mfrr, ReserveDirection.Symmetric)]
    public void Direction_must_match_product(
        ReserveProduct product,
        ReserveDirection direction)
    {
        Assert.Throws<ArgumentException>(() => new ReserveBand(
            "asset-1", product, direction,
            Start, Start + TimeSpan.FromMinutes(15), 5));
    }

    [Fact]
    public void Empty_or_blank_asset_id_throws()
    {
        Assert.Throws<ArgumentException>(() => new ReserveBand(
            "", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            Start, Start + TimeSpan.FromMinutes(15), 5));
        Assert.Throws<ArgumentException>(() => new ReserveBand(
            "  ", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            Start, Start + TimeSpan.FromMinutes(15), 5));
    }

    [Fact]
    public void Start_must_be_before_end()
    {
        Assert.Throws<ArgumentException>(() => new ReserveBand(
            "asset-1", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            Start, Start, 5));
        Assert.Throws<ArgumentException>(() => new ReserveBand(
            "asset-1", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            Start + TimeSpan.FromMinutes(1), Start, 5));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_power_throws(double power)
    {
        Assert.Throws<ArgumentException>(() => new ReserveBand(
            "asset-1", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            Start, Start + TimeSpan.FromMinutes(15), power));
    }

    [Fact]
    public void Negative_power_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReserveBand(
            "asset-1", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            Start, Start + TimeSpan.FromMinutes(15), -1));
    }

    [Fact]
    public void Zero_power_is_accepted()
    {
        // A 0 kW reserve is degenerate but lawful: a no-op band that
        // documents intent without restricting capacity. The optimiser
        // simply produces no extra constraint.
        var band = new ReserveBand(
            "asset-1", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            Start, Start + TimeSpan.FromMinutes(15), 0);

        Assert.Equal(0, band.PowerKw);
    }

    [Fact]
    public void Covers_uses_half_open_window()
    {
        var band = new ReserveBand(
            "asset-1", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            Start, Start + TimeSpan.FromMinutes(15), 5);

        Assert.True(band.Covers(Start));
        Assert.True(band.Covers(Start + TimeSpan.FromMinutes(7)));
        Assert.False(band.Covers(Start + TimeSpan.FromMinutes(15)));
        Assert.False(band.Covers(Start - TimeSpan.FromSeconds(1)));
    }
}
