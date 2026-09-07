namespace SplitOS.Contracts.Diagnostics;

public sealed record DiagnosticEvent(
    DateTimeOffset TimestampUtc,
    string EventName,
    string Component,
    string Severity,
    Guid? CorrelationId,
    Guid? OperationId,
    Guid? RequestId,
    IReadOnlyDictionary<string, string?> Fields);
