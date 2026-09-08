namespace SplitOS.Contracts.Protocol;

public sealed record ProtocolHello(string Component, string ComponentVersion);

public sealed record ProtocolHelloAck(
    string ServerComponent,
    string ServerVersion,
    int NegotiatedProtocolVersion,
    bool Accepted,
    string? Reason);

public sealed record HealthReadRequest;

public sealed record HealthReadResult(
    string Component,
    string ComponentVersion,
    string Status,
    int ProcessId,
    int SessionId,
    DateTimeOffset ObservedAtUtc);

public sealed record MachineStateReadRequest(string RecordKind, string RecordId);

public sealed record MachineStateReadResult(
    string RecordKind,
    string RecordId,
    int SchemaVersion,
    int Revision,
    string PayloadJson,
    DateTimeOffset ReadAtUtc);

public sealed record MachineOperationalModeWriteRequest(
    string TargetMode,
    int ExpectedRevision);

public sealed record MachineOperationalModeWriteResult(
    string Disposition,
    string CommittedMode,
    int Revision,
    Guid OperationId,
    Guid CorrelationId,
    DateTimeOffset CommittedUtc);

public sealed record RuntimeStateReadRequest;

public sealed record RuntimeStateReadResult(
    string Status,
    string ManagedRuntimeAccess,
    string OperationalMode,
    string UserAssociationState,
    int MachineSchemaVersion,
    int UserSchemaVersion,
    int ProjectionSchemaVersion,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Semantic request only. Manager never supplies authorization endpoints, redirect URIs,
/// PKCE/state/nonce values, tokens, account IDs or entitlement claims.
/// </summary>
public sealed record RuntimeAuthStartRequest;

public sealed record RuntimeAuthStartResult(
    string Disposition,
    string ProductCode,
    Guid? AuthTransactionId,
    string? AccountId,
    string? AssociationId,
    bool HasFreshOnlineEntitlement);

public sealed record ErrorResponse(string Code, string Message);
