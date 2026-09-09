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
    public const string MachineStateReadRequest = "MachineStateReadRequest";
    public const string MachineStateReadResult = "MachineStateReadResult";
    public const string MachineOperationalModeWriteRequest = "MachineOperationalModeWriteRequest";
    public const string MachineOperationalModeWriteResult = "MachineOperationalModeWriteResult";
    public const string MachineServicePolicyApplyRequest = "MachineServicePolicyApplyRequest";
    public const string MachineServicePolicyApplyResult = "MachineServicePolicyApplyResult";
    public const string RuntimeStateReadRequest = "RuntimeStateReadRequest";
    public const string RuntimeStateReadResult = "RuntimeStateReadResult";
    public const string RuntimeAuthStartRequest = "RuntimeAuthStartRequest";
    public const string RuntimeAuthStartResult = "RuntimeAuthStartResult";
    public const string RuntimeSignOutRequest = "RuntimeSignOutRequest";
    public const string RuntimeSignOutResult = "RuntimeSignOutResult";
    public const string ErrorResponse = "ErrorResponse";
}

public static class Capabilities
{
    public const string BrokerHealthRead = "Broker.Health.Read";
    public const string MachineStateStoreRead = "Machine.StateStore.Read@1";
    public const string MachineOperationalModeWrite = "Machine.OperationalMode.Write@1";
    public const string MachineServicePolicyApply = "Machine.ServicePolicy.Apply@1";
    public const string RuntimeHealthRead = "Runtime.Health.Read";
    public const string RuntimeStateRead = "Runtime.State.Read";
    public const string RuntimeAuthStart = "Runtime.Auth.Start@1";
    public const string RuntimeSignOut = "Runtime.Auth.SignOut@1";
}

public static class ErrorCodes
{
    public const string ProtocolUnsupported = "PROTOCOL_UNSUPPORTED";
    public const string CallerNotAuthorized = "CALLER_NOT_AUTHORIZED";
    public const string UnknownCapability = "UNKNOWN_CAPABILITY";
    public const string UnsupportedMessage = "UNSUPPORTED_MESSAGE";
    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string InvalidRecordKind = "INVALID_RECORD_KIND";
    public const string PersistenceUnavailable = "PERSISTENCE_UNAVAILABLE";
    public const string PersistenceRevisionConflict = "PERSISTENCE_REVISION_CONFLICT";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string ManagedModeWriteNotAvailable = "MANAGED_MODE_WRITE_NOT_AVAILABLE";
    public const string InternalError = "INTERNAL_ERROR";
}
