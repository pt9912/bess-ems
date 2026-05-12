using System;
using System.Collections.Generic;

namespace BatteryEms.Application.Mpc;

// Estimator output: the current state mean vector plus its covariance.
// The covariance matrix carries the Kalman uncertainty surface that
// Sub-Slice C's `DefaultLinearKalmanFilter` propagates; Sub-Slice A only
// produces it via the `IdentityStateEstimator` stub (P stays at its
// initial value P_0).
//
// `Timestamp` is the instant the estimate is valid for — wall-clock from
// the control-cycle host today; Sub-Slice C swaps in `IClock` /
// `MonotonicAnchoredClock` so the resync-resilience pins from §6 can
// land without rewriting `MpcState`. Equality is value-by-value over the
// vector and covariance so Sub-Slice D can hash `MpcRun` rows without
// boxing the underlying arrays.
public sealed class MpcState : IEquatable<MpcState>
{
    public DateTimeOffset Timestamp { get; }
    public IReadOnlyList<double> Mean { get; }
    public MpcMatrix Covariance { get; }

    public MpcState(DateTimeOffset timestamp, IReadOnlyList<double> mean, MpcMatrix covariance)
    {
        ArgumentNullException.ThrowIfNull(mean);
        ArgumentNullException.ThrowIfNull(covariance);
        if (mean.Count == 0)
            throw new ArgumentException("Mean must contain at least one element.", nameof(mean));
        if (covariance.Rows != mean.Count || covariance.Columns != mean.Count)
        {
            throw new ArgumentException(
                $"Covariance shape {covariance.Rows}x{covariance.Columns} does not match mean length {mean.Count}.",
                nameof(covariance));
        }

        var buf = new double[mean.Count];
        for (var i = 0; i < mean.Count; i++)
        {
            var value = mean[i];
            if (!double.IsFinite(value))
                throw new ArgumentException($"Mean[{i}] is not finite ({value}).", nameof(mean));
            buf[i] = value;
        }

        Timestamp = timestamp;
        Mean = buf;
        Covariance = covariance;
    }

    public int Dimension => Mean.Count;

    public bool Equals(MpcState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Timestamp != other.Timestamp) return false;
        if (Mean.Count != other.Mean.Count) return false;
        for (var i = 0; i < Mean.Count; i++)
        {
            if (!Mean[i].Equals(other.Mean[i])) return false;
        }
        return Covariance.Equals(other.Covariance);
    }

    public override bool Equals(object? obj) => obj is MpcState s && Equals(s);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Timestamp);
        foreach (var v in Mean) hash.Add(v);
        hash.Add(Covariance);
        return hash.ToHashCode();
    }
}
