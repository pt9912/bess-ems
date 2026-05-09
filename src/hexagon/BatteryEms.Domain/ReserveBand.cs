namespace BatteryEms.Domain;

public enum ReserveProduct
{
    Fcr,
    Afrr,
    Mfrr,
}

public enum ReserveDirection
{
    Symmetric,
    Up,
    Down,
}

// LH-MKT-004: a held capacity band per (asset, product, direction) over
// a half-open window [Start, End). PowerKw is a magnitude (always >= 0);
// Direction encodes whether the asset must keep room for upward
// (positive — discharge), downward (negative — charge) or both
// (Symmetric — FCR) regulation. The optimiser deducts the held band
// from the asset's available capacity for any horizon step that
// overlaps the window. RM-M4-02 wires this in for FCR + aFRR; mFRR
// is modelable here without RM-M4-02 demanding a productive
// activation pathway (that lands with RM-M4-03).
public sealed class ReserveBand
{
    public string AssetId { get; }
    public ReserveProduct Product { get; }
    public ReserveDirection Direction { get; }
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public double PowerKw { get; }

    public ReserveBand(
        string assetId,
        ReserveProduct product,
        ReserveDirection direction,
        DateTimeOffset start,
        DateTimeOffset end,
        double powerKw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (start >= end)
        {
            throw new ArgumentException(
                $"Start must be before End ({start:O} -> {end:O}).", nameof(start));
        }
        if (!double.IsFinite(powerKw))
        {
            throw new ArgumentException(
                $"PowerKw must be finite (got '{powerKw}').", nameof(powerKw));
        }
        if (powerKw < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(powerKw), "PowerKw is a magnitude (>= 0); use Direction to express sign.");
        }
        // FCR is symmetric per the EU PRL/FCR product spec; AFRR and
        // MFRR are tendered as separate Up and Down bands. Some TSO
        // products tender symmetric AFRR; if that lands as a real
        // requirement, relax the matcher to accept Symmetric for
        // AFRR/MFRR rather than carve out a new ReserveProduct.
        var matchesProduct = product switch
        {
            ReserveProduct.Fcr => direction == ReserveDirection.Symmetric,
            ReserveProduct.Afrr or ReserveProduct.Mfrr =>
                direction is ReserveDirection.Up or ReserveDirection.Down,
            _ => false,
        };
        if (!matchesProduct)
        {
            throw new ArgumentException(
                $"Product '{product}' does not permit direction '{direction}': FCR is Symmetric; AFRR and MFRR are Up or Down.",
                nameof(direction));
        }

        AssetId = assetId;
        Product = product;
        Direction = direction;
        Start = start;
        End = end;
        PowerKw = powerKw;
    }

    public bool Covers(DateTimeOffset moment) => moment >= Start && moment < End;

    public TimeSpan Duration => End - Start;
}
