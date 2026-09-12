using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SplitOS.RuntimeHost.WindowsContext;

/// <summary>
/// Mirrors the supported GameInput loader boundary for managed P/Invoke. The NuGet package ships
/// GameInputRedist.msi but does not install it; after provisioning, v3 activation must target
/// GameInputRedist.dll rather than the older inbox GameInput.dll.
/// </summary>
internal static class GameInputDllImportResolver
{
    private const string LogicalGameInputLibrary = "GameInput.dll";
    private const string RedistLibrary = "GameInputRedist.dll";
    private const string RedistRegistrySubKey = @"SOFTWARE\Microsoft\GameInput";
    private const string RedistRegistryValue = "RedistDir";

    [ModuleInitializer]
    internal static void Initialize()
        => NativeLibrary.SetDllImportResolver(
            typeof(GameInputDllImportResolver).Assembly,
            ResolveImport);

    private static IntPtr ResolveImport(
        string libraryName,
        System.Reflection.Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;

        if (!OperatingSystem.IsWindows() ||
            !string.Equals(libraryName, LogicalGameInputLibrary, StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        var path = ResolveRedistPath();
        return NativeLibrary.Load(path);
    }

    internal static string ResolveRedistPath()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("GameInput is supported only on Windows.");

        // GameInput 3.5 also supports side-by-side deployment. Prefer an explicitly staged runtime
        // beside SplitOS before consulting the machine-wide redistributable installation.
        var appLocal = Path.Combine(AppContext.BaseDirectory, RedistLibrary);
        if (File.Exists(appLocal))
            return appLocal;

        var system32 = Path.Combine(Environment.SystemDirectory, RedistLibrary);
        if (File.Exists(system32))
            return system32;

        // Microsoft's GameInput loader reads RedistDir from the 32-bit HKLM view even for x64.
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var gameInputKey = baseKey.OpenSubKey(RedistRegistrySubKey, writable: false);
        if (gameInputKey?.GetValue(RedistRegistryValue) is string redistDirectory &&
            !string.IsNullOrWhiteSpace(redistDirectory))
        {
            var registered = Path.Combine(redistDirectory.Trim(), RedistLibrary);
            if (File.Exists(registered))
                return registered;
        }

        throw new DllNotFoundException(
            "GameInput v3 redistributable is not provisioned. Install the Microsoft.GameInput " +
            "GameInputRedist.msi prerequisite before starting SplitOS.RuntimeHost.");
    }
}
