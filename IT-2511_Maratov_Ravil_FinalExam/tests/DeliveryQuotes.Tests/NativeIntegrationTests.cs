using System.Runtime.InteropServices;
using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member C: the native C library (real calls) and the adapter failures.
public class NativeIntegrationTests
{
    // A real P/Invoke to a library that does not exist: the runtime itself raises DllNotFoundException.
    [DllImport("dq_no_such_native_library_for_tests")]
    private static extern int MissingNativeFunction(int a, int b);

    // NATIVE INTEGRATION: real calls against values calculated by hand
    [Fact]
    public void RealNativeCall_ReturnsTheManuallyCalculatedPoints()
    {
        var calculator = new NativeEffortCalculator();

        // points = full 100 m + full 250 g
        Assert.Equal(24, Assert.IsType<Result<int>.Ok>(calculator.Calculate(Req("R-001", 2m, 1m))).Value);       // 20 + 4
        Assert.Equal(332, Assert.IsType<Result<int>.Ok>(calculator.Calculate(Req("R-003", 30m, 8m))).Value);     // 300 + 32
        Assert.Equal(45, Assert.IsType<Result<int>.Ok>(calculator.Calculate(Req("R-005", 4.5m, 0m))).Value);     // 45 + 0 (zero weight is valid)
        Assert.Equal(2, Assert.IsType<Result<int>.Ok>(calculator.Calculate(Req("R-007", 0.1m, 0.25m))).Value);   // 1 + 1
    }

    [Fact]
    public void RawNativeFunction_RoundsDown_AndRejectsOutOfRangeInput_WithMinusOne()
    {
        Assert.Equal(24, NativeEffortCalculator.RawLoadPoints(2000, 1000));
        Assert.Equal(0, NativeEffortCalculator.RawLoadPoints(99, 249));          // rounded down: no full 100 m, no full 250 g
        Assert.Equal(1400, NativeEffortCalculator.RawLoadPoints(100_000, 100_000));   // the upper limit is valid: 1000 + 400
        Assert.Equal(0, NativeEffortCalculator.RawLoadPoints(0, 0));
        Assert.Equal(-1, NativeEffortCalculator.RawLoadPoints(-1, 0));
        Assert.Equal(-1, NativeEffortCalculator.RawLoadPoints(100_001, 0));
        Assert.Equal(-1, NativeEffortCalculator.RawLoadPoints(0, 100_001));
    }

    [Fact]
    public void Adapter_ValidatesTheDomainInputBeforeCallingNative()
    {
        var nativeCalls = 0;
        var calculator = new NativeEffortCalculator((m, g) =>
        {
            nativeCalls++;
            return NativeEffortCalculator.RawLoadPoints(m, g);
        });

        var tooFar = calculator.Calculate(Req("R-1", 100.001m, 1m));   // 100001 m is outside the native range
        var tooHeavy = calculator.Calculate(Req("R-2", 1m, 100.001m));
        Assert.Equal(0, nativeCalls);                                    // the native function was not called at all
        Assert.Equal(ErrorCode.InvalidInput, Assert.IsType<Result<int>.Failure>(tooFar).Error.Code);
        Assert.Contains("R-1", Assert.IsType<Result<int>.Failure>(tooFar).Error.Message);
        Assert.Equal(ErrorCode.InvalidInput, Assert.IsType<Result<int>.Failure>(tooHeavy).Error.Code);

        Assert.Equal(1400, Assert.IsType<Result<int>.Ok>(calculator.Calculate(Req("R-3", 100m, 100m))).Value);   // exact limits are accepted
        Assert.Equal(1, nativeCalls);
    }

    // NATIVE INTEGRATION: loading or binding failures become clear Result failures
    [Fact]
    public void LoadingAndBindingFailures_BecomeClearResultFailures()
    {
        var request = Req("R-001", 2m, 1m);

        var notLoaded = new NativeEffortCalculator((m, g) => throw new DllNotFoundException("deliveryscore"));
        var wrongFormat = new NativeEffortCalculator((m, g) => throw new BadImageFormatException("wrong architecture"));
        var noExport = new NativeEffortCalculator((m, g) => throw new EntryPointNotFoundException("dq_load_points"));
        var rejects = new NativeEffortCalculator((m, g) => -1);

        var f1 = Assert.IsType<Result<int>.Failure>(notLoaded.Calculate(request)).Error;
        var f2 = Assert.IsType<Result<int>.Failure>(wrongFormat.Calculate(request)).Error;
        var f3 = Assert.IsType<Result<int>.Failure>(noExport.Calculate(request)).Error;
        var f4 = Assert.IsType<Result<int>.Failure>(rejects.Calculate(request)).Error;

        Assert.Equal(ErrorCode.NativeLibraryUnavailable, f1.Code);
        Assert.Contains("deliveryscore", f1.Message);
        Assert.Equal(ErrorCode.NativeLibraryUnavailable, f2.Code);
        Assert.Contains("wrong format", f2.Message);
        Assert.Equal(ErrorCode.NativeCallFailed, f3.Code);
        Assert.Contains("dq_load_points", f3.Message);
        Assert.Equal(ErrorCode.NativeCallFailed, f4.Code);
    }

    [Fact]
    public void ARealMissingLibrary_IsReportedAsAFailure_AndTheRealLibraryStillWorksAfterwards()
    {
        var failing = new NativeEffortCalculator((m, g) => MissingNativeFunction(m, g));   // the runtime really fails to load this library
        var real = new NativeEffortCalculator();

        var failure = Assert.IsType<Result<int>.Failure>(failing.Calculate(Req("R-001", 2m, 1m))).Error;

        Assert.Equal(ErrorCode.NativeLibraryUnavailable, failure.Code);
        Assert.Contains("could not be loaded", failure.Message);
        Assert.Equal(24, Assert.IsType<Result<int>.Ok>(real.Calculate(Req("R-001", 2m, 1m))).Value);   // a failed load of one library does not disturb the other
    }

    [Fact]
    public void TheNativeBinaryIsFoundInTheRuntimeFolder_OfTheBuildOutput()
    {
        var fileName = OperatingSystem.IsWindows() ? "deliveryscore.dll" : OperatingSystem.IsMacOS() ? "libdeliveryscore.dylib" : "libdeliveryscore.so";
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var rid = $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

        var expected = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", fileName);

        Assert.True(File.Exists(expected), $"native binary not found: {expected}");
    }
}
