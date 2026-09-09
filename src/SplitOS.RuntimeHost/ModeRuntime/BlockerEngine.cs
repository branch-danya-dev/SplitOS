using System.Security.Cryptography;
using System.Text;

namespace SplitOS.RuntimeHost.ModeRuntime;

public enum BlockerClass
{
    NonBlocking,
    AutoResolvable,
    UserDecisionRequired,
    HardBlock
}

public enum BlockerInspectionDisposition
{
    Resolving,
    AwaitingUser,
    Blocked,
    InspectionFailed
}

public sealed record BlockerDecisionOption(string Code, string MessageKey);

public sealed record BlockerObservation(
    Guid BlockerId,
    string ProviderId,
    string BlockerCode,
    BlockerClass BlockerClass,
    string SubjectRef,
    string MessageKey,
    DateTimeOffset EvidenceTimestamp,
    string EvidenceDigest,
    DateTimeOffset? FreshnessDeadline,
    int? DecisionSchemaVersion,
    IReadOnlyList<BlockerDecisionOption> DecisionOptions);

public sealed record ModeBlockerInspectionContext(
    Guid TransitionId,
    Guid OperationId,
    Guid CorrelationId,
    ModeOperationKind OperationKind,
    OperationalMode SourceMode,
    OperationalMode TargetMode,
    string ControlSessionKey,
    RuntimeAccessEvaluation RuntimeAccess,
    DateTimeOffset RuntimeAccessObservedUtc,
    DateTimeOffset RequestedUtc);

public sealed record BlockerProviderInspectionResult(
    bool IsAvailable,
    string ProductCode,
    IReadOnlyList<BlockerObservation> Observations)
{
    public static BlockerProviderInspectionResult Available(params BlockerObservation[] observations)
        => new(true, "BLOCKER_PROVIDER_OBSERVED", observations);

    public static BlockerProviderInspectionResult Unavailable(string productCode)
        => new(false, productCode, Array.Empty<BlockerObservation>());
}

public interface IModeBlockerProvider
{
    string ProviderId { get; }
    string ProviderVersion { get; }

    ValueTask<BlockerProviderInspectionResult> InspectAsync(
        ModeBlockerInspectionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record BlockerInspectionOutcome(
    BlockerInspectionDisposition Disposition,
    IReadOnlyList<BlockerObservation> Observations,
    string? FailedProviderId,
    string? ProductCode)
{
    public bool MayContinue => Disposition == BlockerInspectionDisposition.Resolving;
}

/// <summary>
/// SPEC-05 blocker aggregation core. Providers own evidence; the engine validates and orders it,
/// but never fabricates a positive fact when a provider is unavailable or violates its contract.
/// </summary>
public sealed class ModeBlockerEngine
{
    private static readonly TimeSpan MaximumFutureEvidenceSkew = TimeSpan.FromMinutes(5);
    private readonly IReadOnlyList<IModeBlockerProvider> _providers;
    private readonly TimeProvider _timeProvider;

    public ModeBlockerEngine(
        IEnumerable<IModeBlockerProvider> providers,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _providers = providers.ToArray();

        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in _providers)
        {
            if (provider is null)
            {
                throw new ArgumentException("Blocker provider registry contains a null provider.", nameof(providers));
            }

            ValidateIdentifier(provider.ProviderId, 128, nameof(provider.ProviderId));
            ValidateIdentifier(provider.ProviderVersion, 64, nameof(provider.ProviderVersion));
            if (!providerIds.Add(provider.ProviderId))
            {
                throw new ArgumentException($"Duplicate blocker provider id '{provider.ProviderId}'.", nameof(providers));
            }
        }
    }

    public async ValueTask<BlockerInspectionOutcome> InspectAsync(
        ModeBlockerInspectionContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var observations = new List<BlockerObservation>();
        var blockerIds = new HashSet<Guid>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BlockerProviderInspectionResult result;
            try
            {
                result = await provider.InspectAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return Failed(provider.ProviderId, "BLOCKER_PROVIDER_INSPECTION_FAILED", observations);
            }

            if (result is null || string.IsNullOrWhiteSpace(result.ProductCode))
            {
                return Failed(provider.ProviderId, "BLOCKER_PROVIDER_CONTRACT_VIOLATION", observations);
            }

            if (!result.IsAvailable)
            {
                if (result.Observations is { Count: > 0 })
                {
                    return Failed(provider.ProviderId, "BLOCKER_PROVIDER_CONTRACT_VIOLATION", observations);
                }

                return Failed(provider.ProviderId, result.ProductCode, observations);
            }

            if (result.Observations is null)
            {
                return Failed(provider.ProviderId, "BLOCKER_PROVIDER_CONTRACT_VIOLATION", observations);
            }

            foreach (var observation in result.Observations)
            {
                try
                {
                    ValidateObservation(provider, observation, _timeProvider.GetUtcNow());
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
                {
                    return Failed(provider.ProviderId, "BLOCKER_PROVIDER_CONTRACT_VIOLATION", observations);
                }

                if (!blockerIds.Add(observation.BlockerId))
                {
                    return Failed(provider.ProviderId, "BLOCKER_DUPLICATE_ID", observations);
                }

                observations.Add(observation);
            }
        }

        var ordered = observations
            .OrderByDescending(static observation => Severity(observation.BlockerClass))
            .ThenBy(static observation => observation.ProviderId, StringComparer.Ordinal)
            .ThenBy(static observation => observation.BlockerCode, StringComparer.Ordinal)
            .ThenBy(static observation => observation.BlockerId)
            .ToArray();

        var disposition = ordered.Any(static observation => observation.BlockerClass == BlockerClass.HardBlock)
            ? BlockerInspectionDisposition.Blocked
            : ordered.Any(static observation => observation.BlockerClass == BlockerClass.UserDecisionRequired)
                ? BlockerInspectionDisposition.AwaitingUser
                : BlockerInspectionDisposition.Resolving;

        return new BlockerInspectionOutcome(disposition, ordered, null, null);
    }

    private static BlockerInspectionOutcome Failed(
        string providerId,
        string productCode,
        IEnumerable<BlockerObservation> observations)
        => new(
            BlockerInspectionDisposition.InspectionFailed,
            observations.ToArray(),
            providerId,
            productCode);

    private static void ValidateContext(ModeBlockerInspectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TransitionId == Guid.Empty) throw new ArgumentException("Transition id is required.", nameof(context));
        if (context.OperationId == Guid.Empty) throw new ArgumentException("Operation id is required.", nameof(context));
        if (context.CorrelationId == Guid.Empty) throw new ArgumentException("Correlation id is required.", nameof(context));
        if (context.RuntimeAccess is null) throw new ArgumentException("Runtime access evidence is required.", nameof(context));
        if (context.RuntimeAccessObservedUtc == default) throw new ArgumentException("Runtime access observation time is required.", nameof(context));
        if (context.RequestedUtc == default) throw new ArgumentException("Requested time is required.", nameof(context));
        ValidateIdentifier(context.ControlSessionKey, 256, nameof(context.ControlSessionKey));
        ModeTransitionDomain.ValidateTuple(context.OperationKind, context.SourceMode, context.TargetMode);
    }

    private static void ValidateObservation(
        IModeBlockerProvider provider,
        BlockerObservation observation,
        DateTimeOffset now)
    {
        if (observation is null) throw new InvalidDataException("Provider returned a null blocker observation.");
        if (observation.BlockerId == Guid.Empty) throw new InvalidDataException("Blocker id is required.");
        if (!string.Equals(observation.ProviderId, provider.ProviderId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Blocker provider identity does not match the provider that emitted it.");
        }

        ValidateIdentifier(observation.ProviderId, 128, nameof(observation.ProviderId));
        ValidateIdentifier(observation.BlockerCode, 128, nameof(observation.BlockerCode));
        ValidateIdentifier(observation.SubjectRef, 256, nameof(observation.SubjectRef));
        ValidateIdentifier(observation.MessageKey, 256, nameof(observation.MessageKey));
        ValidateDigest(observation.EvidenceDigest);

        if (observation.EvidenceTimestamp == default ||
            observation.EvidenceTimestamp > now.Add(MaximumFutureEvidenceSkew))
        {
            throw new InvalidDataException("Blocker evidence timestamp is invalid.");
        }

        if (observation.FreshnessDeadline is DateTimeOffset deadline && deadline < observation.EvidenceTimestamp)
        {
            throw new InvalidDataException("Blocker freshness deadline precedes its evidence timestamp.");
        }

        var options = observation.DecisionOptions ?? throw new InvalidDataException("Decision option collection is required.");
        if (observation.BlockerClass == BlockerClass.UserDecisionRequired)
        {
            if (observation.DecisionSchemaVersion is null or < 1 || options.Count == 0 || options.Count > 16)
            {
                throw new InvalidDataException("User-decision blocker requires a bounded versioned closed option set.");
            }

            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in options)
            {
                if (option is null) throw new InvalidDataException("Decision option must not be null.");
                ValidateIdentifier(option.Code, 128, nameof(option.Code));
                ValidateIdentifier(option.MessageKey, 256, nameof(option.MessageKey));
                if (!codes.Add(option.Code)) throw new InvalidDataException("Decision option codes must be unique.");
            }
        }
        else if (observation.DecisionSchemaVersion is not null || options.Count != 0)
        {
            throw new InvalidDataException("Only USER_DECISION_REQUIRED blockers may define decision options.");
        }
    }

    private static void ValidateIdentifier(string value, int maximumLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("Semantic identifier is missing or outside supported bounds.", paramName);
        }
    }

    private static void ValidateDigest(string digest)
    {
        if (digest.Length != 64 || digest.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Evidence digest must be a SHA-256 hexadecimal value.");
        }
    }

    private static int Severity(BlockerClass blockerClass)
        => blockerClass switch
        {
            BlockerClass.HardBlock => 4,
            BlockerClass.UserDecisionRequired => 3,
            BlockerClass.AutoResolvable => 2,
            BlockerClass.NonBlocking => 1,
            _ => 0
        };
}

/// <summary>
/// First release-defined blocker provider. It converts already-computed RuntimeAccess evidence into
/// a mode blocker without reinterpreting account plan/tier metadata.
/// </summary>
public sealed class RuntimeAccessBlockerProvider : IModeBlockerProvider
{
    public const string Id = "RuntimeAccessProvider";
    public string ProviderId => Id;
    public string ProviderVersion => "1";

    public ValueTask<BlockerProviderInspectionResult> InspectAsync(
        ModeBlockerInspectionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);

        if (context.OperationKind == ModeOperationKind.Deactivate && context.TargetMode == OperationalMode.None)
        {
            return ValueTask.FromResult(BlockerProviderInspectionResult.Available());
        }

        if (context.RuntimeAccess.IsEnabled)
        {
            return ValueTask.FromResult(BlockerProviderInspectionResult.Available());
        }

        var evidence = $"{context.RuntimeAccess.ManagedRuntimeAccess}\n{context.RuntimeAccess.Reason}\n{context.RuntimeAccess.EntitlementVersion?.ToString() ?? "none"}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))).ToLowerInvariant();
        var observation = new BlockerObservation(
            Guid.NewGuid(),
            ProviderId,
            "MANAGED_RUNTIME_ACCESS_REQUIRED",
            BlockerClass.HardBlock,
            "runtime.managed_modes",
            "mode.blocker.runtime_access_required",
            context.RuntimeAccessObservedUtc,
            digest,
            null,
            null,
            Array.Empty<BlockerDecisionOption>());

        return ValueTask.FromResult(BlockerProviderInspectionResult.Available(observation));
    }
}
