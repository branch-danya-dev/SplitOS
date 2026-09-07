using SplitOS.Ipc.Windows;

namespace SplitOS.RuntimeHost;

public sealed record CallerAuthorization(bool Allowed, string? Reason)
{
    public static CallerAuthorization Allow() => new(true, null);
    public static CallerAuthorization Deny(string reason) => new(false, reason);
}

public sealed class RuntimeUiCallerValidator
{
    private static readonly HashSet<string> AllowedImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "SplitOS.Manager.exe",
        "SplitOS.GameLauncher.exe"
    };

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
        return AllowedImages.Contains(imageName)
            ? CallerAuthorization.Allow()
            : CallerAuthorization.Deny("CALLER_IMAGE_NOT_ALLOWED");
    }
}
