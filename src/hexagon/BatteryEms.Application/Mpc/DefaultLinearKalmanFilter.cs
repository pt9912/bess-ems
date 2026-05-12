using BatteryEms.Domain;

namespace BatteryEms.Application.Mpc;

public sealed class DefaultLinearKalmanFilter : IMpcStateEstimator
{
    private const double CovarianceDivergenceThreshold = 1e12;
    private readonly Dictionary<string, int> _missingMeasurementsByAsset = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public string EstimatorVariant => "linear_kalman";

    public Task<MpcStateUpdate> PredictUpdateAsync(
        MpcState? priorState,
        BatteryTelemetry? measurement,
        BatteryAsset asset,
        MpcModel model,
        MpcOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ValidateShape(priorState, model, options);

        if (measurement is not null && IsNonPhysical(measurement, asset))
        {
            var state = priorState ?? BuildColdBootState(null, asset, model, options);
            return Task.FromResult(new MpcStateUpdate(
                state,
                IsHealthy: false,
                Reason: MpcEstimatorReasons.NonPhysical));
        }

        var previous = priorState ?? BuildColdBootState(measurement, asset, model, options);
        var inputPowerKw = measurement?.ActivePowerKw ?? 0.0;
        var predicted = Predict(previous, inputPowerKw, model, options);
        if (IsCovarianceDiverged(predicted.Covariance))
        {
            return Task.FromResult(new MpcStateUpdate(
                predicted,
                IsHealthy: false,
                Reason: MpcEstimatorReasons.CovarianceDiverged));
        }

        if (measurement is null || !measurement.Available || !measurement.DataQuality.IsUsableForControl)
        {
            var missingCount = IncrementMissingCount(asset.AssetId);
            var healthy = missingCount <= options.Estimator.MaxConsecutiveMissingMeasurements;
            return Task.FromResult(new MpcStateUpdate(
                predicted,
                healthy,
                healthy ? MpcEstimatorReasons.MeasurementSkipped : MpcEstimatorReasons.StaleTooLong));
        }

        ResetMissingCount(asset.AssetId);
        var updated = Update(predicted, measurement, inputPowerKw, model, options);
        if (IsCovarianceDiverged(updated.Covariance))
        {
            return Task.FromResult(new MpcStateUpdate(
                updated,
                IsHealthy: false,
                Reason: MpcEstimatorReasons.CovarianceDiverged));
        }

        return Task.FromResult(new MpcStateUpdate(
            updated,
            IsHealthy: true,
            Reason: MpcEstimatorReasons.StateEstimated));
    }

    private static MpcState BuildColdBootState(
        BatteryTelemetry? measurement,
        BatteryAsset asset,
        MpcModel model,
        MpcOptions options)
    {
        var mean = new double[model.StateDimension];
        mean[0] = measurement?.SocPercent ?? ((asset.MinSocPercent + asset.MaxSocPercent) / 2.0);
        var timestamp = measurement?.Timestamp ?? DateTimeOffset.MinValue;
        return new MpcState(timestamp, mean, options.Estimator.InitialCovariance);
    }

    private static MpcState Predict(
        MpcState previous,
        double inputPowerKw,
        MpcModel model,
        MpcOptions options)
    {
        var xPredicted = Add(
            Multiply(model.A, previous.Mean.ToArray()),
            Scale(Column(model.B, 0), inputPowerKw));
        var pPredicted = Add(
            Multiply(Multiply(model.A, previous.Covariance), Transpose(model.A)),
            options.Estimator.ProcessNoise);

        return new MpcState(previous.Timestamp.Add(options.SampleTime), xPredicted, ToMatrix(pPredicted));
    }

    private static MpcState Update(
        MpcState predicted,
        BatteryTelemetry measurement,
        double inputPowerKw,
        MpcModel model,
        MpcOptions options)
    {
        var pPredicted = ToRows(predicted.Covariance);
        var c = ToRows(model.C);
        var cTranspose = Transpose(c);
        var s = Add(Multiply(Multiply(c, pPredicted), cTranspose), options.Estimator.MeasurementNoise);
        var gain = Multiply(Multiply(pPredicted, cTranspose), Invert(s));
        var measurementVector = new[] { measurement.SocPercent };
        var predictedOutput = Add(
            Multiply(model.C, predicted.Mean.ToArray()),
            Scale(Column(model.D, 0), inputPowerKw));
        var innovation = Subtract(measurementVector, predictedOutput);
        var updatedMean = Add(predicted.Mean.ToArray(), Multiply(gain, innovation));
        var updatedCovariance = Multiply(Subtract(Identity(model.StateDimension), Multiply(gain, c)), pPredicted);

        return new MpcState(measurement.Timestamp, updatedMean, ToMatrix(updatedCovariance));
    }

    private static bool IsNonPhysical(BatteryTelemetry measurement, BatteryAsset asset) =>
        !double.IsFinite(measurement.SocPercent) ||
        !double.IsFinite(measurement.ActivePowerKw) ||
        !double.IsFinite(measurement.TemperatureCelsius) ||
        measurement.SocPercent < asset.MinSocPercent ||
        measurement.SocPercent > asset.MaxSocPercent ||
        measurement.TemperatureCelsius < asset.MinOperatingTemperatureCelsius ||
        measurement.TemperatureCelsius > asset.MaxOperatingTemperatureCelsius;

    private static bool IsCovarianceDiverged(MpcMatrix covariance)
    {
        var determinant = Determinant(ToRows(covariance));
        return !double.IsFinite(determinant) || Math.Abs(determinant) > CovarianceDivergenceThreshold;
    }

    private int IncrementMissingCount(string assetId)
    {
        lock (_gate)
        {
            _missingMeasurementsByAsset.TryGetValue(assetId, out var current);
            var next = current + 1;
            _missingMeasurementsByAsset[assetId] = next;
            return next;
        }
    }

    private void ResetMissingCount(string assetId)
    {
        lock (_gate)
        {
            _missingMeasurementsByAsset.Remove(assetId);
        }
    }

    private static void ValidateShape(MpcState? priorState, MpcModel model, MpcOptions options)
    {
        if (model.InputDimension != 1)
        {
            throw new InvalidOperationException("DefaultLinearKalmanFilter supports exactly one active-power input.");
        }
        if (model.OutputDimension != 1)
        {
            throw new InvalidOperationException("DefaultLinearKalmanFilter supports exactly one SOC measurement output.");
        }
        if (priorState is not null && priorState.Dimension != model.StateDimension)
        {
            throw new InvalidOperationException("Prior state dimension does not match model dimension.");
        }
        if (options.Estimator.InitialCovariance.Rows != model.StateDimension ||
            options.Estimator.ProcessNoise.Rows != model.StateDimension ||
            options.Estimator.MeasurementNoise.Rows != model.OutputDimension)
        {
            throw new InvalidOperationException("Estimator covariance dimensions do not match the MPC model.");
        }
    }

    private static double[][] ToRows(MpcMatrix matrix)
    {
        var rows = NewRows(matrix.Rows, matrix.Columns);
        for (var row = 0; row < matrix.Rows; row++)
        {
            for (var column = 0; column < matrix.Columns; column++)
            {
                rows[row][column] = matrix[row, column];
            }
        }
        return rows;
    }

    private static MpcMatrix ToMatrix(double[][] rows)
    {
        var elements = new double[rows.Length * rows[0].Length];
        var index = 0;
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                elements[index++] = rows[row][column];
            }
        }
        return new MpcMatrix(rows.Length, rows[0].Length, elements);
    }

    private static double[] Column(MpcMatrix matrix, int column)
    {
        var values = new double[matrix.Rows];
        for (var row = 0; row < matrix.Rows; row++)
        {
            values[row] = matrix[row, column];
        }
        return values;
    }

    private static double[] Multiply(MpcMatrix matrix, double[] vector) =>
        Multiply(ToRows(matrix), vector);

    private static double[][] Multiply(MpcMatrix left, MpcMatrix right) =>
        Multiply(ToRows(left), ToRows(right));

    private static double[][] Transpose(MpcMatrix matrix) => Transpose(ToRows(matrix));

    private static double[] Multiply(double[][] matrix, double[] vector)
    {
        var result = new double[matrix.Length];
        for (var row = 0; row < matrix.Length; row++)
        {
            var value = 0.0;
            for (var column = 0; column < matrix[row].Length; column++)
            {
                value += matrix[row][column] * vector[column];
            }
            result[row] = value;
        }
        return result;
    }

    private static double[][] Multiply(double[][] left, double[][] right)
    {
        var result = NewRows(left.Length, right[0].Length);
        for (var row = 0; row < left.Length; row++)
        {
            for (var column = 0; column < right[0].Length; column++)
            {
                var value = 0.0;
                for (var k = 0; k < right.Length; k++)
                {
                    value += left[row][k] * right[k][column];
                }
                result[row][column] = value;
            }
        }
        return result;
    }

    private static double[] Add(double[] left, double[] right)
    {
        var result = new double[left.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = left[i] + right[i];
        }
        return result;
    }

    private static double[][] Add(double[][] left, MpcMatrix right) => Add(left, ToRows(right));

    private static double[][] Add(double[][] left, double[][] right)
    {
        var result = NewRows(left.Length, left[0].Length);
        for (var row = 0; row < left.Length; row++)
        {
            for (var column = 0; column < left[row].Length; column++)
            {
                result[row][column] = left[row][column] + right[row][column];
            }
        }
        return result;
    }

    private static double[] Subtract(double[] left, double[] right)
    {
        var result = new double[left.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = left[i] - right[i];
        }
        return result;
    }

    private static double[][] Subtract(double[][] left, double[][] right)
    {
        var result = NewRows(left.Length, left[0].Length);
        for (var row = 0; row < left.Length; row++)
        {
            for (var column = 0; column < left[row].Length; column++)
            {
                result[row][column] = left[row][column] - right[row][column];
            }
        }
        return result;
    }

    private static double[] Scale(double[] vector, double factor)
    {
        var result = new double[vector.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = vector[i] * factor;
        }
        return result;
    }

    private static double[][] Transpose(double[][] matrix)
    {
        var result = NewRows(matrix[0].Length, matrix.Length);
        for (var row = 0; row < matrix.Length; row++)
        {
            for (var column = 0; column < matrix[row].Length; column++)
            {
                result[column][row] = matrix[row][column];
            }
        }
        return result;
    }

    private static double[][] Identity(int size)
    {
        var result = NewRows(size, size);
        for (var i = 0; i < size; i++)
        {
            result[i][i] = 1.0;
        }
        return result;
    }

    private static double[][] Invert(double[][] matrix)
    {
        var n = matrix.Length;
        var augmented = NewRows(n, n * 2);
        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column < n; column++)
            {
                augmented[row][column] = matrix[row][column];
            }
            augmented[row][n + row] = 1.0;
        }

        for (var pivot = 0; pivot < n; pivot++)
        {
            var pivotRow = pivot;
            for (var row = pivot + 1; row < n; row++)
            {
                if (Math.Abs(augmented[row][pivot]) > Math.Abs(augmented[pivotRow][pivot]))
                {
                    pivotRow = row;
                }
            }
            if (Math.Abs(augmented[pivotRow][pivot]) < 1e-15)
            {
                throw new InvalidOperationException("Estimator innovation covariance is singular.");
            }
            if (pivotRow != pivot)
            {
                (augmented[pivot], augmented[pivotRow]) = (augmented[pivotRow], augmented[pivot]);
            }

            var pivotValue = augmented[pivot][pivot];
            for (var column = 0; column < n * 2; column++)
            {
                augmented[pivot][column] /= pivotValue;
            }
            for (var row = 0; row < n; row++)
            {
                if (row == pivot)
                {
                    continue;
                }
                var factor = augmented[row][pivot];
                for (var column = 0; column < n * 2; column++)
                {
                    augmented[row][column] -= factor * augmented[pivot][column];
                }
            }
        }

        var inverse = NewRows(n, n);
        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column < n; column++)
            {
                inverse[row][column] = augmented[row][n + column];
            }
        }
        return inverse;
    }

    private static double Determinant(double[][] matrix)
    {
        var n = matrix.Length;
        var work = NewRows(n, n);
        for (var row = 0; row < n; row++)
        {
            Array.Copy(matrix[row], work[row], n);
        }

        var sign = 1.0;
        var determinant = 1.0;
        for (var pivot = 0; pivot < n; pivot++)
        {
            var pivotRow = pivot;
            for (var row = pivot + 1; row < n; row++)
            {
                if (Math.Abs(work[row][pivot]) > Math.Abs(work[pivotRow][pivot]))
                {
                    pivotRow = row;
                }
            }
            if (Math.Abs(work[pivotRow][pivot]) < 1e-15)
            {
                return 0.0;
            }
            if (pivotRow != pivot)
            {
                (work[pivot], work[pivotRow]) = (work[pivotRow], work[pivot]);
                sign = -sign;
            }

            var pivotValue = work[pivot][pivot];
            determinant *= pivotValue;
            for (var row = pivot + 1; row < n; row++)
            {
                var factor = work[row][pivot] / pivotValue;
                for (var column = pivot; column < n; column++)
                {
                    work[row][column] -= factor * work[pivot][column];
                }
            }
        }
        return sign * determinant;
    }

    private static double[][] NewRows(int rows, int columns)
    {
        var result = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            result[row] = new double[columns];
        }
        return result;
    }
}
