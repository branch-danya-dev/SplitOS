namespace SplitOS.RuntimeHost.GameRuntime;

/// <summary>
/// Release-local registry for interactive RuntimeHost game-client adapters.
/// Client type is the immutable routing key. The registry validates the complete capability surface
/// once at construction and re-validates runtime compatibility snapshots before exposing them.
/// </summary>
public sealed class GameClientAdapterRegistry
{
    private readonly IReadOnlyDictionary<GameClientType, Registration> _registrations;

    public GameClientAdapterRegistry(IEnumerable<IGameClientAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        var registrations = new Dictionary<GameClientType, Registration>();
        foreach (var adapter in adapters)
        {
            if (adapter is null)
                throw new InvalidDataException("Game client adapter registration cannot be null.");

            var descriptor = (adapter.Descriptor
                ?? throw new InvalidDataException("Game client adapter descriptor cannot be null."))
                .Normalize();
            if (!registrations.TryAdd(descriptor.ClientType, new Registration(adapter, descriptor)))
                throw new InvalidDataException($"More than one adapter claims client type {descriptor.ClientType}.");
        }

        _registrations = registrations;
    }

    public IReadOnlyList<GameClientType> RegisteredClientTypes
        => _registrations.Keys.OrderBy(clientType => clientType).ToArray();

    public IReadOnlyList<GameClientAdapterDescriptor> Descriptors
        => _registrations.Values
            .Select(registration => registration.Descriptor)
            .OrderBy(descriptor => descriptor.ClientType)
            .ToArray();

    public bool TryGetAdapter(GameClientType clientType, out IGameClientAdapter? adapter)
    {
        if (_registrations.TryGetValue(clientType, out var registration))
        {
            adapter = registration.Adapter;
            return true;
        }

        adapter = null;
        return false;
    }

    public IGameClientAdapter GetRequiredAdapter(GameClientType clientType)
        => RegistrationFor(clientType).Adapter;

    public GameClientAdapterDescriptor GetDescriptor(GameClientType clientType)
        => RegistrationFor(clientType).Descriptor;

    public GameClientCompatibilitySnapshot GetCompatibilityStatus(
        GameClientType clientType,
        GameClientCompatibilityContext context)
    {
        var registration = RegistrationFor(clientType);
        var normalizedContext = (context ?? throw new ArgumentNullException(nameof(context))).Normalize();
        var snapshot = registration.Adapter.GetCompatibilityStatus(normalizedContext)
            ?? throw new InvalidDataException("Adapter returned no compatibility snapshot.");
        return snapshot.Normalize(registration.Descriptor);
    }

    public GameClientCapabilityStatus GetCapabilityStatus(
        GameClientType clientType,
        GameClientCapabilityId capabilityId,
        GameClientCompatibilityContext context)
        => GetCompatibilityStatus(clientType, context).Capability(capabilityId);

    private Registration RegistrationFor(GameClientType clientType)
    {
        if (_registrations.TryGetValue(clientType, out var registration))
            return registration;
        throw new KeyNotFoundException($"No game client adapter is registered for {clientType}.");
    }

    private sealed record Registration(
        IGameClientAdapter Adapter,
        GameClientAdapterDescriptor Descriptor);
}
