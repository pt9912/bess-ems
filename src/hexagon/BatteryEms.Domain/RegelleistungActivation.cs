namespace BatteryEms.Domain;

// External Regelleistung activation signal (LH-MKT-005/006). The signal
// carries the TSO/Vendor-emitted setpoint and the validity window over
// which the EMS must follow it. The (SourceId, ActivationId) tuple is
// the dedupe identity (Sub-Slice B uses it as the unique key in the
// persistent tracker); SequenceNumber and SignalTimestampUtc resolve
// tiebreaks between concurrent valid candidates (Sub-Slice E).
//
// PowerKw is a magnitude (>= 0) — Direction encodes sign — mirroring
// ReserveBand. Up = upward regulation = discharge by the EMS sign
// convention (positive grid feed); Down = downward regulation = charge.
//
// Product/Direction reuse ReserveProduct + ReserveDirection from
// RM-M4-02 per plan-RM-M4-03 D-08 (no parallel product-family enums).
// FCR activation by external signal is unusual (FCR is auto-activated
// by frequency response), but the type can model it as Symmetric;
// AFRR/MFRR activations are Up or Down.
//
// Timestamps are DateTimeOffset; the source adapter (RM-M4-04 / F-09)
// normalises to UTC before constructing — same convention as Schedule
// and ReserveBand.
public sealed class RegelleistungActivation
{
    public string SourceId { get; }
    public string ActivationId { get; }
    public long SequenceNumber { get; }
    public DateTimeOffset SignalTimestampUtc { get; }
    public ReserveProduct Product { get; }
    public ReserveDirection Direction { get; }
    public double PowerKw { get; }
    public DateTimeOffset ValidFrom { get; }
    public DateTimeOffset ValidUntil { get; }
    public string PayloadHash { get; }

    public RegelleistungActivation(
        string sourceId,
        string activationId,
        long sequenceNumber,
        DateTimeOffset signalTimestampUtc,
        ReserveProduct product,
        ReserveDirection direction,
        double powerKw,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        string payloadHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);
        if (sequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber),
                "SequenceNumber must be non-negative.");
        }
        if (!double.IsFinite(powerKw))
        {
            throw new ArgumentException(
                $"PowerKw must be finite (got '{powerKw}').", nameof(powerKw));
        }
        if (powerKw < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(powerKw),
                "PowerKw is a magnitude (>= 0); use Direction to express sign.");
        }
        if (validFrom >= validUntil)
        {
            throw new ArgumentException(
                $"ValidFrom must be before ValidUntil ({validFrom:O} -> {validUntil:O}).",
                nameof(validFrom));
        }
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

        SourceId = sourceId;
        ActivationId = activationId;
        SequenceNumber = sequenceNumber;
        SignalTimestampUtc = signalTimestampUtc;
        Product = product;
        Direction = direction;
        PowerKw = powerKw;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        PayloadHash = payloadHash;
    }

    public bool CoversValidity(DateTimeOffset moment)
        => moment >= ValidFrom && moment < ValidUntil;
}
