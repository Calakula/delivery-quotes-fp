using System.Reflection;
using System.Runtime.InteropServices;

namespace DeliveryQuotes.Core;

/// <summary>
/// P/Invoke declaration of the C export "int32_t dq_load_points(int32_t, int32_t)" (native/include/deliveryscore.h)
/// and the documented loading path of the library.
///
/// LOADING PATH. The default .NET probing only looks next to the application and in the framework folder, and does not
/// search runtimes/&lt;rid&gt;/native for a project that is not a NuGet package. So a DllImport resolver is registered
/// once for this assembly. It looks, in this order, for the platform file name (deliveryscore.dll, libdeliveryscore.so,
/// libdeliveryscore.dylib) in:
///   1. the folder named by the environment variable DQ_NATIVE_DIR (optional),
///   2. runtimes/&lt;rid&gt;/native next to the application (win-x64, linux-x64, osx-arm64, ...),
///   3. the application folder itself.
/// If nothing is found, the resolver returns "not handled" and the runtime performs its default probing; the final
/// DllNotFoundException is turned into a Result failure by <see cref="NativeEffortCalculator"/>.
/// </summary>
internal static class NativeMethods
{
    internal const string LibraryName = "deliveryscore";

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, Resolve);
    }

    [DllImport(LibraryName, EntryPoint = "dq_load_points", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int LoadPoints(int distanceMeters, int weightGrams);

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    internal static IEnumerable<string> Candidates()
    {
        var file = FileName();
        var overrideDirectory = Environment.GetEnvironmentVariable("DQ_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            yield return Path.Combine(overrideDirectory, file);
        }

        yield return Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeId(), "native", file);
        yield return Path.Combine(AppContext.BaseDirectory, file);
    }

    private static string FileName() =>
        OperatingSystem.IsWindows() ? "deliveryscore.dll"
        : OperatingSystem.IsMacOS() ? "libdeliveryscore.dylib"
        : "libdeliveryscore.so";

    private static string RuntimeId()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        return $"{os}-{architecture}";
    }
}
