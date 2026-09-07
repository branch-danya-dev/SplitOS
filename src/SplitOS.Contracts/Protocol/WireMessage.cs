using System.Text.Json;

namespace SplitOS.Contracts.Protocol;

public sealed record WireMessage
{
    public required string MessageType { get; init; }
    public int ProtocolVersion { get; init; } = ProtocolConstants.CurrentVersion;
    public required Guid RequestId { get; init; }
    public required Guid OperationId { get; init; }
    public required Guid CorrelationId { get; init; }
    public string? Capability { get; init; }
    public required JsonElement Payload { get; init; }

    public static WireMessage Create<T>(
        string messageType,
        T payload,
        string? capability = null,
        Guid? operationId = null,
        Guid? correlationId = null,
        Guid? requestId = null)
        => new()
        {
            MessageType = messageType,
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            RequestId = requestId ?? Guid.NewGuid(),
            OperationId = operationId ?? Guid.NewGuid(),
            CorrelationId = correlationId ?? Guid.NewGuid(),
            Capability = capability,
            Payload = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options)
        };

    public static WireMessage Respond<T>(WireMessage request, string messageType, T payload)
        => Create(
            messageType,
            payload,
            request.Capability,
            request.OperationId,
            request.CorrelationId,
            request.RequestId);

    public T ReadPayload<T>()
        => Payload.Deserialize<T>(ProtocolJson.Options)
           ?? throw new InvalidDataException($"Payload for {MessageType} could not be deserialized as {typeof(T).Name}.");
}
