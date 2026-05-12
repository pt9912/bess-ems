using System;
using System.Collections.Generic;
using System.Linq;

namespace BatteryEms.Application.Mpc;

// Row-major dense matrix used for LTI state-space (A,B,C,D) and Kalman
// covariance (P,Q,R) shapes. The element store is a defensive copy of a
// flat `double[]` so callers can't mutate the matrix post-construction;
// equality is value-by-value across `Elements` so two matrices that hold
// the same numbers under the same shape hash and compare identically.
// That property feeds the Sub-Slice-D determinism stamps where matrix
// configuration is one of the identity-tuple inputs (D-09 estimator/
// solver config hashes).
//
// Sub-Slice A introduces only the shape; the solver line that actually
// computes A x_k + B u_k arrives in B/C. The validator (next file) treats
// `MpcModel.A/B` as opaque dimensions and pins the constraint surface.
public sealed class MpcMatrix : IEquatable<MpcMatrix>
{
    public int Rows { get; }
    public int Columns { get; }
    public IReadOnlyList<double> Elements { get; }

    public MpcMatrix(int rows, int columns, IReadOnlyList<double> elements)
    {
        if (rows <= 0)
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "Rows must be positive.");
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns), columns, "Columns must be positive.");
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count != rows * columns)
        {
            throw new ArgumentException(
                $"Element count {elements.Count} does not match shape {rows}x{columns} (expected {rows * columns}).",
                nameof(elements));
        }

        var buffer = new double[elements.Count];
        for (var i = 0; i < elements.Count; i++)
        {
            var value = elements[i];
            if (!double.IsFinite(value))
            {
                throw new ArgumentException(
                    $"Element at index {i} is not finite ({value}).",
                    nameof(elements));
            }
            buffer[i] = value;
        }

        Rows = rows;
        Columns = columns;
        Elements = buffer;
    }

    public double this[int row, int column]
    {
        get
        {
            if ((uint)row >= (uint)Rows)
                throw new ArgumentOutOfRangeException(nameof(row), row, $"Row out of range [0,{Rows}).");
            if ((uint)column >= (uint)Columns)
                throw new ArgumentOutOfRangeException(nameof(column), column, $"Column out of range [0,{Columns}).");
            return Elements[(row * Columns) + column];
        }
    }

    public static MpcMatrix Identity(int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be positive.");
        var buf = new double[size * size];
        for (var i = 0; i < size; i++) buf[(i * size) + i] = 1.0;
        return new MpcMatrix(size, size, buf);
    }

    public bool Equals(MpcMatrix? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Rows != other.Rows || Columns != other.Columns) return false;
        for (var i = 0; i < Elements.Count; i++)
        {
            if (!Elements[i].Equals(other.Elements[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is MpcMatrix m && Equals(m);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Rows);
        hash.Add(Columns);
        foreach (var v in Elements) hash.Add(v);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"MpcMatrix[{Rows}x{Columns}]({string.Join(",", Elements.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))})";
}
