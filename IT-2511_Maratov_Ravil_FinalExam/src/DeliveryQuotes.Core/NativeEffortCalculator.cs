using System.Globalization;

namespace DeliveryQuotes.Core;

/// <summary>Calculates the courier effort points of a delivery.</summary>
public interface IEffortCalculator
{
    Result<int> Calculate(DeliveryRequest request);
}

/// <summary>
/// ADAPTER around the native C function dq_load_points.
///   1. validates the domain input and converts the units (km to whole metres, kg to whole grams, rounded half away from zero)
///      BEFORE any native call;
///   2. calls the native function;
///   3. translates a loading or binding failure, and a rejected input, into a Result failure instead of an exception.
/// The native function uses simple values only, so no native memory is allocated and there is nothing to release.
/// </summary>
public sealed class NativeEffortCalculator : IEffortCalculator
{
    public const int MaxDistanceMeters = 100_000;
    public const int MaxWeightGrams = 100_000;

    private readonly Func<int, int, int> _nativeCall;

    /// <summary>Uses the real native library.</summary>
    public NativeEffortCalculator()
        : this(RawLoadPoints)
    {
    }

    /// <summary>Lets a test replace the call to simulate a loading or binding failure. The default is the real native call.</summary>
    public NativeEffortCalculator(Func<int, int, int> nativeCall)
    {
        ArgumentNullException.ThrowIfNull(nativeCall);
        _nativeCall = nativeCall;
    }

    /// <summary>The unvalidated native call, exposed for diagnostics and for tests of the C function itself.</summary>
    public static int RawLoadPoints(int distanceMeters, int weightGrams) => NativeMethods.LoadPoints(distanceMeters, weightGrams);

    public Result<int> Calculate(DeliveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var meters = decimal.Round(request.DistanceKm * 1000m, 0, MidpointRounding.AwayFromZero);
        var grams = decimal.Round(request.WeightKg * 1000m, 0, MidpointRounding.AwayFromZero);

        if (meters < 0m || meters > MaxDistanceMeters)
        {
            return Result.Failure<int>(ErrorCode.InvalidInput,
                string.Create(CultureInfo.InvariantCulture, $"Order {request.Id}: distance {request.DistanceKm} km is outside the native range 0 to {MaxDistanceMeters / 1000} km."));
        }
        if (grams < 0m || grams > MaxWeightGrams)
        {
            return Result.Failure<int>(ErrorCode.InvalidInput,
                string.Create(CultureInfo.InvariantCulture, $"Order {request.Id}: weight {request.WeightKg} kg is outside the native range 0 to {MaxWeightGrams / 1000} kg."));
        }

        try
        {
            var points = _nativeCall((int)meters, (int)grams);
            return points >= 0
                ? Result.Success(points)
                : Result.Failure<int>(ErrorCode.NativeCallFailed, $"The native function dq_load_points rejected the input of order {request.Id} (returned {points}).");
        }
        catch (DllNotFoundException ex)
        {
            return Result.Failure<int>(ErrorCode.NativeLibraryUnavailable,
                $"The native library 'deliveryscore' could not be loaded (file missing or not found in the search folders): {ex.Message}");
        }
        catch (BadImageFormatException ex)
        {
            return Result.Failure<int>(ErrorCode.NativeLibraryUnavailable,
                $"The native library 'deliveryscore' has the wrong format for this platform or architecture: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            return Result.Failure<int>(ErrorCode.NativeCallFailed,
                $"The native library was loaded but the export 'dq_load_points' was not found: {ex.Message}");
        }
    }
}
