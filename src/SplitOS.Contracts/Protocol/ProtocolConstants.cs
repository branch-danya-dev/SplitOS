namespace SplitOS.Contracts.Protocol;

public static class ProtocolConstants
{
    public const int CurrentVersion = 1;
    public const int MaxFrameBytes = 256 * 1024;
}

public static class MessageTypes
{
    public const string ProtocolHello = "ProtocolHello";
    public const string ProtocolHelloAck = "ProtocolHelloAck";
    public const string HealthReadRequest = "HealthReadRequest";
    public const string HealthReadResult = "HealthReadResult";
    public const string ErrorResponse = "ErrorResponse";
}

public static class Capabilities
{
    public const string BrokerHealthRead = "Broker.Health.Read";
    public const string RuntimeHealthRead = "Runtime.Health.Read";
}

public static class ErrorCodes
{
    public const string ProtocolUnsupported = "PROTOCOL_UNSUPPORTED";
    public const string CallerNotAuthorized = "CALLER_NOT_AUTHORIZED";
    public const string UnknownCapability = "UNKNOWN_CAPABILITY";
    public const string UnsupportedMessage = "UNSUPPORTED_MESSAGE";
    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string InternalError = "INTERNAL_ERROR";
}
