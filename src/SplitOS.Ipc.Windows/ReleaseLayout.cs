using System.Diagnostics;

namespace SplitOS.Ipc.Windows;

public static class ReleaseLayout
{
    public static string ResolveCurrentReleaseRoot(string currentComponentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentComponentDirectory);

        using var process = Process.GetCurrentProcess();
        var imagePath = process.MainModule?.FileName ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new InvalidOperationException("Current process image path is unavailable.");
        }

        var componentDirectory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(componentDirectory))
        {
            throw new InvalidOperationException("Current component directory cannot be resolved.");
        }

        if (!string.Equals(
                Path.GetFileName(componentDirectory),
                currentComponentDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SplitOS component must run from the release layout directory '{currentComponentDirectory}'. Current directory: '{componentDirectory}'.");
        }

        var releaseRoot = Directory.GetParent(componentDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(releaseRoot))
        {
            throw new InvalidOperationException("SplitOS release root cannot be resolved.");
        }

        return NormalizeRoot(releaseRoot);
    }

    public static string ComponentExecutable(
        string releaseRoot,
        string componentDirectory,
        string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        return Path.GetFullPath(Path.Combine(
            NormalizeRoot(releaseRoot),
            componentDirectory,
            executableName));
    }

    public static bool IsExactPath(string? actualPath, string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(actualPath))
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPath);

        try
        {
            return string.Equals(
                Path.GetFullPath(actualPath),
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static string NormalizeRoot(string releaseRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseRoot);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(releaseRoot));
    }
}
