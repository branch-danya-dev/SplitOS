using SplitOS.Ipc.Windows;

namespace SplitOS.Broker.Service;

public sealed record BrokerAuthorization(bool Allowed, string? Reason)
{
    public static BrokerAuthorization Allow() => new(true, null);
    public static BrokerAuthorization Deny(string reason) => new(false, reason);
}

public sealed class BrokerCallerValidator
{
    private const string RuntimeHostExecutable = "SplitOS.RuntimeHost.exe";
    private readonly string _expectedRuntimeHostPath;

    public BrokerCallerValidator()
        : this(ReleaseLayout.ResolveCurrentReleaseRoot("Broker"))
    {
    }

    public BrokerCallerValidator(string trustedReleaseRoot)
    {
        _expectedRuntimeHostPath = ReleaseLayout.ComponentExecutable(
            trustedReleaseRoot,
            "RuntimeHost",
            RuntimeHostExecutable);
    }

    public BrokerAuthorization Validate(PipeClientIdentity identity, uint expectedSessionId)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (identity.SessionId != expectedSessionId)
        {
            return BrokerAuthorization.Deny("CALLER_SESSION_MISMATCH");
        }

        if (string.IsNullOrWhiteSpace(identity.ImagePath))
        {
            return BrokerAuthorization.Deny("CALLER_IMAGE_UNAVAILABLE");
        }

        var imageName = Path.GetFileName(identity.ImagePath);
        if (!string.Equals(imageName, RuntimeHostExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return BrokerAuthorization.Deny("CALLER_IMAGE_NOT_RUNTIMEHOST");
        }

        if (!ReleaseLayout.IsExactPath(identity.ImagePath, _expectedRuntimeHostPath))
        {
            return BrokerAuthorization.Deny("CALLER_RELEASE_PATH_MISMATCH");
        }

        return BrokerAuthorization.Allow();
    }
}
