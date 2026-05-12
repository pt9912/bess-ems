using System;

namespace BatteryEms.Application.Mpc;

// Linear time-invariant state-space hull: x_{k+1} = A x_k + B u_k,
// y_k = C x_k + D u_k. Plan §3 fixes the initial scope to LTI; piecewise
// and non-linear forms are F-M5-07 follow-ups, gated on the first replay
// gap that LTI cannot explain (plan §9).
//
// `ModelVersion` is part of the Sub-Slice-D identity tuple (D-09) — it
// must change whenever A, B, C, D, or the constraints hull changes so
// replay can distinguish runs against different model snapshots without
// re-hashing the matrix elements. The constructor does not enforce
// shape-vs-constraints consistency beyond dimension matching; the
// validator (`MpcConstraintValidator`) cross-checks the trajectory's
// power axis against `Constraints` per call.
public sealed record MpcModel
{
    public string ModelVersion { get; }
    public MpcMatrix A { get; }
    public MpcMatrix B { get; }
    public MpcMatrix C { get; }
    public MpcMatrix D { get; }
    public MpcConstraints Constraints { get; }

    public MpcModel(
        string modelVersion,
        MpcMatrix a,
        MpcMatrix b,
        MpcMatrix c,
        MpcMatrix d,
        MpcConstraints constraints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(constraints);

        if (a.Rows != a.Columns)
            throw new ArgumentException($"A must be square; got {a.Rows}x{a.Columns}.", nameof(a));
        if (b.Rows != a.Rows)
            throw new ArgumentException($"B rows ({b.Rows}) must match A rows ({a.Rows}).", nameof(b));
        if (c.Columns != a.Rows)
            throw new ArgumentException($"C columns ({c.Columns}) must match A rows ({a.Rows}).", nameof(c));
        if (d.Rows != c.Rows)
            throw new ArgumentException($"D rows ({d.Rows}) must match C rows ({c.Rows}).", nameof(d));
        if (d.Columns != b.Columns)
            throw new ArgumentException($"D columns ({d.Columns}) must match B columns ({b.Columns}).", nameof(d));

        ModelVersion = modelVersion;
        A = a;
        B = b;
        C = c;
        D = d;
        Constraints = constraints;
    }

    public int StateDimension => A.Rows;
    public int InputDimension => B.Columns;
    public int OutputDimension => C.Rows;
}
