using SplitOS.Ipc.Windows;

namespace SplitOS.RuntimeHost;

public sealed record CallerAuthorization(bool Allowed, string? Reason)
{
    public static CallerAuthorization Allow() => new(true, null);
    public static CallerAuthorization Deny(string reason) => new(false, reason);
}

public sealed class RuntimeUiCallerValidator
{
    private readonly IReadOnlyDictionary<string, string> _allowedImagePaths;

    public RuntimeUiCallerValidator()
        : this(ReleaseLayout.ResolveCurrentReleaseRoot("RuntimeHost"))
    {
    }

    public RuntimeUiCallerValidator(string trustedReleaseRoot)
    {
        _allowedImagePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SplitOS.Manager.exe"] = ReleaseLayout.ComponentExecutable(
                trustedReleaseRoot,
                "Manager",
                "SplitOS.Manager.exe"),
            ["SplitOS.GameLauncher.exe"] = ReleaseLayout.ComponentExecutable(
                trustedReleaseRoot,
                "GameLauncher",
                "SplitOS.GameLauncher.exe")
        };
    }

    public CallerAuthorization Validate(PipeClientIdentity identity, uint expectedSessionId)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (identity.SessionId != expectedSessionId)
        {
            return CallerAuthorization.Deny("CALLER_SESSION_MISMATCH");
        }

        if (string.IsNullOrWhiteSpace(identity.ImagePath))
        {
            return CallerAuthorization.Deny("CALLER_IMAGE_UNAVAILABLE");
        }

        var imageName = Path.GetFileName(identity.ImagePath);
        if (!_allowedImagePaths.TryGetValue(imageName, out var expectedPath))
        {
            return CallerAuthorization.Deny("CALLER_IMAGE_NOT_ALLOWED");
        }

        return ReleaseLayout.IsExactPath(identity.ImagePath, expectedPath)
            ? CallerAuthorization.Allow()
            : CallerAuthorization.Deny("CALLER_RELEASE_PATH_MISMATCH");
    }
}
