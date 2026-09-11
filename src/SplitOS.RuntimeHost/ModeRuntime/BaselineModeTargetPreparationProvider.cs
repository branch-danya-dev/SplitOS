using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public interface IModeBasePolicyClient
{
    Task<MachineModeBasePolicyResult> ResolveBaseAsync(Guid operationId, Guid correlationId,
        MachineModeBasePolicyRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Service-only DEACTIVATE provider. Managed target policies remain a separate release dependency.</summary>
public sealed class BaselineModeTargetPreparationProvider(IModeBasePolicyClient broker) : IRuntimeModeTargetPreparationProvider
{
    public async ValueTask<RuntimeModePreparedTarget> PrepareAsync(ModeOperationPlan operation,
        RuntimeAccessEvaluation runtimeAccess, CancellationToken cancellationToken = default)
    {
        if (operation.OperationKind != ModeOperationKind.Deactivate || operation.TargetMode != OperationalMode.None)
            throw new InvalidDataException("This provider supports only restoration of the Windows service baseline.");
        var baseline = await broker.ResolveBaseAsync(operation.OperationId, operation.CorrelationId,
            new(operation.SourceModeRevision, operation.ControlSessionKey), cancellationToken).ConfigureAwait(false);
        if (baseline.Disposition != "RESOLVED") throw new InvalidDataException(baseline.ProductCode);
        var entries = ManagedServicePolicyActionContract.NormalizeEntries(baseline.Entries);
        var digest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries);
        if (digest != baseline.DesiredDigest) throw new InvalidDataException("BASE policy digest mismatch.");
        var policy = new ResolvedModePolicySnapshot(new("mode-policy.windows-services-baseline", 1, "development", digest),
            ModePolicyTarget.Base,
            entries.Select(entry => new ModePolicyRule("restore." + entry.ManagedServiceId.ToLowerInvariant(), ModePolicyDomain.RuntimeServices,
                entry.ManagedServiceId, entry.DesiredState == "RUNNING" ? ModePolicyIntent.ManagedActive : ModePolicyIntent.ManagedInactive,
                ModePolicyRequirement.Mandatory, ModePolicyFallback.None)).ToArray(),
            Array.Empty<ModePolicyFallbackSelection>(), digest);
        var action = new PersistedModeActionDefinition(Guid.NewGuid(), 100, ManagedServicePolicyActionContract.OwningModule,
            ManagedServicePolicyActionContract.ActionType, ManagedServicePolicyActionContract.TargetRef,
            ManagedServicePolicyActionContract.DesiredSchemaVersion, ManagedServicePolicyActionContract.SerializeDesiredState(entries),
            digest, true, "restore_pre_state", "service.actual-state");
        return new(policy, [action]);
    }
}
