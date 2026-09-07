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

public sealed record ErrorResponse(string Code, string Message);
