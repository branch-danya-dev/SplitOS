using SplitOS.Ipc.Windows;

namespace SplitOS.Broker.Service;

public sealed record BrokerAuthorization(bool Allowed, string? Reason)
{
    public static BrokerAuthorization Allow() => new(true, null);
    public static BrokerAuthorization Deny(string reason) => new(false, reason);
}

public sealed class BrokerCallerValidator
{
    private readonly string _allowedRuntimeImage = "SplitOS.RuntimeHost.exe";

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
        if (!string.Equals(imageName, _allowedRuntimeImage, StringComparison.OrdinalIgnoreCase))
        {
            return BrokerAuthorization.Deny("CALLER_IMAGE_NOT_RUNTIMEHOST");
        }

        return BrokerAuthorization.Allow();
    }
}
