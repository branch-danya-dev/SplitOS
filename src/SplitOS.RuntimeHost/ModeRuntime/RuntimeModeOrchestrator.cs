using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public enum RuntimeModeExecutionDisposition
{
    Completed,
    NoOp,
    AuthorityDenied,
    Busy,
    Blocked,
    UserDecisionRequired,
    PreparationRejected,
    ActionRejected,
    CommitRejected,
    ReconciliationRequired
}

public sealed record RuntimeModeExecutionCommand(
    OperationalMode TargetMode,
    Guid OperationId,
    Guid CorrelationId,
    Guid TransitionId,
    string ControlSessionKey,
    RuntimeAccessEvaluation RuntimeAccess,
    DateTimeOffset RuntimeAccessObservedUtc,
    Guid? ActivationEpochId,
    TimeSpan LeaseLifetime);

public sealed record RuntimeModeExecutionOutcome(
    RuntimeModeExecutionDisposition Disposition,
    string ProductCode,
    OperationalModeRecord OperationalMode,
    ModeTransitionRecord? Transition = null,
    IReadOnlyList<BlockerObservation>? Blockers = null,
    string? Detail = null)
{
    public bool IsCompleted => Disposition is RuntimeModeExecutionDisposition.Completed or RuntimeModeExecutionDisposition.NoOp;
}

public sealed record RuntimeModePreparedTarget(
    ResolvedModePolicySnapshot Policy,
    IReadOnlyList<PersistedModeActionDefinition> Actions);

public interface IRuntimeModeTargetPreparationProvider
{
    ValueTask<RuntimeModePreparedTarget> PrepareAsync(
        ModeOperationPlan operation,
        RuntimeAccessEvaluation runtimeAccess,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// First executable SPEC-05 vertical mode path. RuntimeHost owns operation ordering while policy,
/// persistence and privileged mutation remain in their existing boundaries. This increment supports
/// only fully automatic transitions: blocker decisions are refused before the first mutation until
/// their durable observation/decision repository exists.
/// </summary>
public interface IRuntimeModeCommandExecutor
{
    Task<RuntimeModeExecutionOutcome> ExecuteAsync(RuntimeModeExecutionCommand command, CancellationToken cancellationToken = default);
}

public sealed class RuntimeModeOrchestrator(
    IMachineStateStore machineStateStore,
    IMachineMutationLeaseStore leaseStore,
    IModeTransitionStore transitionStore,
    IModeTransitionPolicyStore policyStore,
    IModeTransitionActionPlanStore actionPlanStore,
    IModeActionApplyCoordinator applyCoordinator,
    IModeActionVerifyCoordinator verifyCoordinator,
    IModeTransitionCommitStore commitStore,
    ModeBlockerEngine blockerEngine,
    IRuntimeModeTargetPreparationProvider preparationProvider,
    TimeProvider? timeProvider = null) : IRuntimeModeCommandExecutor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public RuntimeModeOrchestrator(
        IMachineStateStore machineStateStore,
        IMachineMutationLeaseStore leaseStore,
        IModeTransitionStore transitionStore,
        IModeTransitionPolicyStore policyStore,
        IModeTransitionActionPlanStore actionPlanStore,
        ManagedServiceActionApplyCoordinator applyCoordinator,
        ManagedServiceActionVerifyCoordinator verifyCoordinator,
        IModeTransitionCommitStore commitStore,
        ModeBlockerEngine blockerEngine,
        IRuntimeModeTargetPreparationProvider preparationProvider,
        TimeProvider? timeProvider = null)
        : this(
            machineStateStore,
            leaseStore,
            transitionStore,
            policyStore,
            actionPlanStore,
            new ManagedServiceDirectActionApplyCoordinatorAdapter(applyCoordinator),
            new ManagedServiceDirectActionVerifyCoordinatorAdapter(verifyCoordinator),
            commitStore,
            blockerEngine,
            preparationProvider,
            timeProvider)
    {
    }

    public async Task<RuntimeModeExecutionOutcome> ExecuteAsync(
        RuntimeModeExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);
        await machineStateStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await leaseStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await transitionStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await policyStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await actionPlanStore.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var source = await machineStateStore.GetOperationalModeAsync(cancellationToken).ConfigureAwait(false);
        var sourceMode = ParseMode(source.CommittedMode);
        var plan = ModeTransitionDomain.Plan(
            sourceMode,
            command.TargetMode,
            source.Revision,
            command.OperationId,
            command.CorrelationId,
            command.ControlSessionKey);

        if (!plan.CreatesTransition)
        {
            return new RuntimeModeExecutionOutcome(
                RuntimeModeExecutionDisposition.NoOp,
                "MODE_NO_OP",
                source);
        }

        if (plan.OperationKind != ModeOperationKind.Deactivate && !command.RuntimeAccess.IsEnabled)
        {
            return new RuntimeModeExecutionOutcome(
                RuntimeModeExecutionDisposition.AuthorityDenied,
                "MODE_TARGET_AUTHORITY_DENIED",
                source,
                Detail: command.RuntimeAccess.Reason);
        }

        var activationError = ValidateActivationEpoch(plan, source, command.ActivationEpochId);
        if (activationError is not null)
        {
            return new RuntimeModeExecutionOutcome(
                RuntimeModeExecutionDisposition.PreparationRejected,
                "MODE_ACTIVATION_EPOCH_INVALID",
                source,
                Detail: activationError);
        }

        var acquired = await leaseStore.TryAcquireAsync(
            MachineMutationType.Mode,
            command.OperationId,
            command.CorrelationId,
            command.ControlSessionKey,
            command.LeaseLifetime,
            cancellationToken).ConfigureAwait(false);
        if (acquired.Disposition is MachineMutationLeaseAcquireDisposition.Busy)
        {
            return new RuntimeModeExecutionOutcome(
                RuntimeModeExecutionDisposition.Busy,
                acquired.ProductCode,
                source);
        }
        if (acquired.Disposition is MachineMutationLeaseAcquireDisposition.ReconciliationRequired)
        {
            return new RuntimeModeExecutionOutcome(
                RuntimeModeExecutionDisposition.ReconciliationRequired,
                acquired.ProductCode,
                source);
        }

        var lease = acquired.Lease;
        if (!lease.LeaseId.HasValue)
            throw new InvalidDataException("Successful MODE lease acquisition omitted lease identity.");

        var created = await transitionStore.CreateAsync(
            command.TransitionId,
            command.OperationId,
            command.CorrelationId,
            ToPersisted(plan.OperationKind!.Value),
            ToStorageMode(plan.SourceMode),
            ToStorageMode(plan.TargetMode),
            plan.SourceModeRevision,
            command.ControlSessionKey,
            lease.LeaseId.Value,
            lease.FenceToken,
            cancellationToken).ConfigureAwait(false);
        if (created.Disposition is not (ModeTransitionCreateDisposition.Created or ModeTransitionCreateDisposition.Replayed) ||
            created.Transition is null)
        {
            await ReleaseBestEffortAsync(lease, command.OperationId, cancellationToken).ConfigureAwait(false);
            return new RuntimeModeExecutionOutcome(
                created.Disposition == ModeTransitionCreateDisposition.ReconciliationRequired
                    ? RuntimeModeExecutionDisposition.ReconciliationRequired
                    : RuntimeModeExecutionDisposition.PreparationRejected,
                created.ProductCode,
                source,
                created.Transition,
                Detail: created.Detail);
        }

        var transition = created.Transition;
        if (transition.TransitionState != PersistedModeTransitionState.Requested ||
            transition.Stage != PersistedModeTransitionStage.Accepted)
        {
            return new RuntimeModeExecutionOutcome(
                RuntimeModeExecutionDisposition.ReconciliationRequired,
                "MODE_EXISTING_TRANSITION_RECONCILIATION_REQUIRED",
                source,
                transition,
                Detail: "ExecuteAsync only starts a fresh REQUESTED/ACCEPTED transition; restart continuation belongs to reconciliation.");
        }

        var advanced = await AdvanceAsync(
            transition,
            lease,
            command.OperationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionStarted,
            false,
            cancellationToken).ConfigureAwait(false);
        if (advanced is null) return await ReconciliationAsync(source, transition, "MODE_INSPECTION_START_REJECTED", cancellationToken).ConfigureAwait(false);
        transition = advanced;

        var inspection = await blockerEngine.InspectAsync(
            new ModeBlockerInspectionContext(
                transition.TransitionId,
                transition.OperationId,
                transition.CorrelationId,
                plan.OperationKind.Value,
                plan.SourceMode,
                plan.TargetMode,
                transition.ControlSessionKey,
                command.RuntimeAccess,
                command.RuntimeAccessObservedUtc,
                transition.StartedUtc),
            cancellationToken).ConfigureAwait(false);

        advanced = await AdvanceAsync(
            transition,
            lease,
            command.OperationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete,
            false,
            cancellationToken).ConfigureAwait(false);
        if (advanced is null) return await ReconciliationAsync(source, transition, "MODE_INSPECTION_EVIDENCE_REJECTED", cancellationToken).ConfigureAwait(false);
        transition = advanced;

        if (inspection.Disposition != BlockerInspectionDisposition.Resolving)
        {
            var terminal = await StopBeforeMutationAsync(source, transition, lease, command.OperationId, inspection, cancellationToken).ConfigureAwait(false);
            return terminal;
        }

        advanced = await AdvanceAsync(
            transition,
            lease,
            command.OperationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ResolutionStarted,
            false,
            cancellationToken).ConfigureAwait(false);
        if (advanced is null) return await ReconciliationAsync(source, transition, "MODE_RESOLUTION_START_REJECTED", cancellationToken).ConfigureAwait(false);
        transition = advanced;

        RuntimeModePreparedTarget prepared;
        try
        {
            prepared = await preparationProvider.PrepareAsync(plan, command.RuntimeAccess, cancellationToken).ConfigureAwait(false);
            ValidatePreparedTarget(prepared, plan.TargetMode);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            return await ReconciliationAsync(source, transition, "MODE_TARGET_PREPARATION_REJECTED", cancellationToken, ex.Message).ConfigureAwait(false);
        }

        var policyIdentity = new PersistedModePolicyIdentity(
            prepared.Policy.Identity.PolicyCatalogId,
            prepared.Policy.Identity.PolicyVersion,
            prepared.Policy.Identity.ReleaseId,
            prepared.Policy.Identity.CatalogDigest);
        var bound = await policyStore.BindResolvedPolicyAsync(
            transition.TransitionId,
            transition.Revision,
            lease.LeaseId.Value,
            lease.FenceToken,
            command.OperationId,
            policyIdentity,
            ToPersisted(prepared.Policy.Target),
            prepared.Policy.ResolvedDigest,
            prepared.Policy.SelectedFallbacks.Select(ToPersisted).ToArray(),
            cancellationToken).ConfigureAwait(false);
        if (bound.Disposition is not (ModeTransitionPolicyBindDisposition.Bound or ModeTransitionPolicyBindDisposition.Replayed) ||
            bound.Binding is null)
        {
            return await ReconciliationAsync(source, transition, bound.ProductCode, cancellationToken, bound.Detail).ConfigureAwait(false);
        }
        transition = (await transitionStore.GetAsync(transition.TransitionId, cancellationToken).ConfigureAwait(false))!;

        var persistedPlan = await actionPlanStore.PersistActionPlanAsync(
            transition.TransitionId,
            transition.Revision,
            lease.LeaseId.Value,
            lease.FenceToken,
            command.OperationId,
            prepared.Actions,
            cancellationToken).ConfigureAwait(false);
        if (persistedPlan.Disposition is not (ModeTransitionActionPlanPersistDisposition.Persisted or ModeTransitionActionPlanPersistDisposition.Replayed) ||
            persistedPlan.Plan is null)
        {
            return await ReconciliationAsync(source, transition, persistedPlan.ProductCode, cancellationToken, persistedPlan.Detail).ConfigureAwait(false);
        }
        transition = (await transitionStore.GetAsync(transition.TransitionId, cancellationToken).ConfigureAwait(false))!;

        advanced = await AdvanceAsync(transition, lease, command.OperationId,
            PersistedModeTransitionState.Resolving, PersistedModeTransitionStage.ActionPlanReady, false, cancellationToken).ConfigureAwait(false);
        if (advanced is null) return await ReconciliationAsync(source, transition, "MODE_ACTION_PLAN_READY_REJECTED", cancellationToken).ConfigureAwait(false);
        transition = advanced;

        advanced = await AdvanceAsync(transition, lease, command.OperationId,
            PersistedModeTransitionState.Applying, PersistedModeTransitionStage.ApplyStarted, false, cancellationToken).ConfigureAwait(false);
        if (advanced is null) return await ReconciliationAsync(source, transition, "MODE_APPLY_START_REJECTED", cancellationToken).ConfigureAwait(false);
        transition = advanced;

        foreach (var action in persistedPlan.Plan.Actions.OrderBy(static item => item.SequenceNo))
        {
            var apply = await applyCoordinator.ApplyAsync(
                new ModeActionExecutionCommand(
                    transition.TransitionId,
                    action.ActionId,
                    action.Revision,
                    lease.LeaseId.Value,
                    lease.FenceToken,
                    command.OperationId,
                    command.CorrelationId,
                    command.ControlSessionKey),
                cancellationToken).ConfigureAwait(false);
            if (!apply.IsApplied)
            {
                return await EnterRollbackRequiredAsync(source, transition, lease, command.OperationId, apply.ProductCode, apply.Detail, cancellationToken).ConfigureAwait(false);
            }
        }

        advanced = await AdvanceAsync(transition, lease, command.OperationId,
            PersistedModeTransitionState.Applying, PersistedModeTransitionStage.ApplyComplete, false, cancellationToken).ConfigureAwait(false);
        if (advanced is null) return await EnterRollbackRequiredAsync(source, transition, lease, command.OperationId, "MODE_APPLY_COMPLETE_REJECTED", null, cancellationToken).ConfigureAwait(false);
        transition = advanced;

        advanced = await AdvanceAsync(transition, lease, command.OperationId,
            PersistedModeTransitionState.Verifying, PersistedModeTransitionStage.VerifyStarted, false, cancellationToken).ConfigureAwait(false);
        if (advanced is null) return await EnterRollbackRequiredAsync(source, transition, lease, command.OperationId, "MODE_VERIFY_START_REJECTED", null, cancellationToken).ConfigureAwait(false);
        transition = advanced;

        var refreshedPlan = await actionPlanStore.GetAsync(transition.TransitionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Durable action plan disappeared before verification.");
        foreach (var action in refreshedPlan.Actions.OrderBy(static item => item.SequenceNo))
        {
            if (action.State == PersistedModeActionState.Skipped) continue;
            var verify = await verifyCoordinator.VerifyAsync(
                new ModeActionExecutionCommand(
                    transition.TransitionId,
                    action.ActionId,
                    action.Revision,
                    lease.LeaseId.Value,
                    lease.FenceToken,
                    command.OperationId,
                    command.CorrelationId,
                    command.ControlSessionKey),
                cancellationToken).ConfigureAwait(false);
            if (!verify.IsVerified)
            {
                return await EnterRollbackRequiredAsync(source, transition, lease, command.OperationId, verify.ProductCode, verify.Detail, cancellationToken).ConfigureAwait(false);
            }
        }

        advanced = await AdvanceAsync(transition, lease, command.OperationId,
            PersistedModeTransitionState.Verifying, PersistedModeTransitionStage.VerifyComplete, true, cancellationToken).ConfigureAwait(false);
        if (advanced is null) return await EnterRollbackRequiredAsync(source, transition, lease, command.OperationId, "MODE_VERIFY_COMPLETE_REJECTED", null, cancellationToken).ConfigureAwait(false);
        transition = advanced;

        advanced = await AdvanceAsync(transition, lease, command.OperationId,
            PersistedModeTransitionState.Committing, PersistedModeTransitionStage.CommitStarted, true, cancellationToken).ConfigureAwait(false);
        if (advanced is null) return await EnterRollbackRequiredAsync(source, transition, lease, command.OperationId, "MODE_COMMIT_START_REJECTED", null, cancellationToken).ConfigureAwait(false);
        transition = advanced;

        var committed = await commitStore.CommitTransitionAndModeAsync(
            transition.TransitionId,
            transition.Revision,
            source.Revision,
            lease.LeaseId.Value,
            lease.FenceToken,
            command.OperationId,
            command.TargetMode == OperationalMode.None || command.RuntimeAccess.IsEnabled,
            command.TargetMode == OperationalMode.None ? null : command.ActivationEpochId,
            command.TargetMode == OperationalMode.None ? null : policyIdentity,
            cancellationToken).ConfigureAwait(false);
        if (committed.Disposition is not (ModeTransitionCommitDisposition.Committed or ModeTransitionCommitDisposition.Replayed) ||
            committed.OperationalMode is null || committed.Transition is null)
        {
            return await EnterRollbackRequiredAsync(source, transition, lease, command.OperationId, committed.ProductCode, committed.Detail, cancellationToken, RuntimeModeExecutionDisposition.CommitRejected).ConfigureAwait(false);
        }

        var canonical = committed.OperationalMode;
        transition = committed.Transition;
        advanced = await AdvanceAsync(transition, lease, command.OperationId,
            PersistedModeTransitionState.Committing, PersistedModeTransitionStage.FinalizationStarted, true, cancellationToken).ConfigureAwait(false);
        if (advanced is null)
        {
            return new RuntimeModeExecutionOutcome(
                RuntimeModeExecutionDisposition.ReconciliationRequired,
                "MODE_POST_COMMIT_FINALIZATION_RECONCILIATION_REQUIRED",
                canonical,
                transition,
                Detail: "Target commit is already durable; recovery must converge around target truth.");
        }
        transition = advanced;

        advanced = await AdvanceAsync(transition, lease, command.OperationId,
            PersistedModeTransitionState.Completed, PersistedModeTransitionStage.Terminal, true, cancellationToken, "COMPLETED").ConfigureAwait(false);
        if (advanced is null)
        {
            return new RuntimeModeExecutionOutcome(
                RuntimeModeExecutionDisposition.ReconciliationRequired,
                "MODE_POST_COMMIT_TERMINALIZATION_RECONCILIATION_REQUIRED",
                canonical,
                transition,
                Detail: "Target commit is already durable; recovery must finish finalization without reverting it.");
        }
        transition = advanced;

        var release = await leaseStore.ReleaseAsync(
            lease.LeaseId.Value,
            lease.FenceToken,
            command.OperationId,
            cancellationToken).ConfigureAwait(false);
        var releaseDetail = release.Disposition is MachineMutationLeaseReleaseDisposition.Released or MachineMutationLeaseReleaseDisposition.AlreadyReleased
            ? null
            : $"Transition completed, but MODE lease cleanup requires reconciliation: {release.ProductCode}.";

        return new RuntimeModeExecutionOutcome(
            RuntimeModeExecutionDisposition.Completed,
            releaseDetail is null ? "MODE_TRANSITION_COMPLETED" : "MODE_TRANSITION_COMPLETED_LEASE_RECONCILIATION_REQUIRED",
            canonical,
            transition,
            Detail: releaseDetail);
    }

    private async Task<ModeTransitionRecord?> AdvanceAsync(
        ModeTransitionRecord current,
        MachineMutationLeaseRecord lease,
        Guid operationId,
        PersistedModeTransitionState state,
        PersistedModeTransitionStage stage,
        bool mandatoryVerified,
        CancellationToken cancellationToken,
        string? terminalOutcome = null)
    {
        var result = await transitionStore.AdvanceAsync(
            current.TransitionId,
            current.Revision,
            lease.LeaseId!.Value,
            lease.FenceToken,
            operationId,
            state,
            stage,
            mandatoryVerified,
            terminalOutcome,
            cancellationToken).ConfigureAwait(false);
        return result.Disposition is ModeTransitionAdvanceDisposition.Advanced or ModeTransitionAdvanceDisposition.Unchanged
            ? result.Transition
            : null;
    }

    private async Task<RuntimeModeExecutionOutcome> StopBeforeMutationAsync(
        OperationalModeRecord source,
        ModeTransitionRecord transition,
        MachineMutationLeaseRecord lease,
        Guid operationId,
        BlockerInspectionOutcome inspection,
        CancellationToken cancellationToken)
    {
        var state = inspection.Disposition == BlockerInspectionDisposition.AwaitingUser
            ? PersistedModeTransitionState.AwaitingUser
            : PersistedModeTransitionState.Blocked;
        var stage = state == PersistedModeTransitionState.AwaitingUser
            ? PersistedModeTransitionStage.WaitingForUser
            : PersistedModeTransitionStage.InspectionComplete;
        var stopped = await AdvanceAsync(transition, lease, operationId, state, stage, false, cancellationToken).ConfigureAwait(false);
        if (stopped is null) return await ReconciliationAsync(source, transition, "MODE_BLOCKER_STATE_REJECTED", cancellationToken).ConfigureAwait(false);

        var cancelled = await AdvanceAsync(
            stopped,
            lease,
            operationId,
            PersistedModeTransitionState.Cancelled,
            PersistedModeTransitionStage.Terminal,
            false,
            cancellationToken,
            "CANCELLED").ConfigureAwait(false);
        if (cancelled is null) return await ReconciliationAsync(source, stopped, "MODE_BLOCKER_CANCEL_REJECTED", cancellationToken).ConfigureAwait(false);

        await ReleaseBestEffortAsync(lease, operationId, cancellationToken).ConfigureAwait(false);
        var disposition = inspection.Disposition switch
        {
            BlockerInspectionDisposition.AwaitingUser => RuntimeModeExecutionDisposition.UserDecisionRequired,
            _ => RuntimeModeExecutionDisposition.Blocked
        };
        return new RuntimeModeExecutionOutcome(
            disposition,
            inspection.ProductCode ?? (inspection.Disposition == BlockerInspectionDisposition.AwaitingUser
                ? "MODE_USER_DECISION_PERSISTENCE_NOT_AVAILABLE"
                : "MODE_BLOCKED"),
            source,
            cancelled,
            inspection.Observations,
            inspection.Disposition == BlockerInspectionDisposition.AwaitingUser
                ? "User-decision blocker was observed before mutation, but durable blocker decision persistence is not implemented yet; operation was cancelled fail-closed."
                : null);
    }

    private async Task<RuntimeModeExecutionOutcome> EnterRollbackRequiredAsync(
        OperationalModeRecord source,
        ModeTransitionRecord transition,
        MachineMutationLeaseRecord lease,
        Guid operationId,
        string productCode,
        string? detail,
        CancellationToken cancellationToken,
        RuntimeModeExecutionDisposition disposition = RuntimeModeExecutionDisposition.ActionRejected)
    {
        var latest = await transitionStore.GetAsync(transition.TransitionId, cancellationToken).ConfigureAwait(false) ?? transition;
        if (!latest.CommitDurable && latest.TransitionState is PersistedModeTransitionState.Applying or PersistedModeTransitionState.Verifying or PersistedModeTransitionState.Committing)
        {
            var rollback = await AdvanceAsync(
                latest,
                lease,
                operationId,
                PersistedModeTransitionState.RollingBack,
                PersistedModeTransitionStage.RollbackStarted,
                latest.MandatoryVerified,
                cancellationToken).ConfigureAwait(false);
            latest = rollback ?? latest;
        }

        return new RuntimeModeExecutionOutcome(
            latest.CommitDurable ? RuntimeModeExecutionDisposition.ReconciliationRequired : disposition,
            productCode,
            source,
            latest,
            Detail: detail ?? "Forward execution stopped. Durable rollback/reconciliation must settle machine state before a new mode operation.");
    }

    private async Task<RuntimeModeExecutionOutcome> ReconciliationAsync(
        OperationalModeRecord source,
        ModeTransitionRecord transition,
        string productCode,
        CancellationToken cancellationToken,
        string? detail = null)
    {
        var latest = await transitionStore.GetAsync(transition.TransitionId, cancellationToken).ConfigureAwait(false) ?? transition;
        return new RuntimeModeExecutionOutcome(
            RuntimeModeExecutionDisposition.ReconciliationRequired,
            productCode,
            source,
            latest,
            Detail: detail);
    }

    private async Task ReleaseBestEffortAsync(
        MachineMutationLeaseRecord lease,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (!lease.LeaseId.HasValue) return;
        _ = await leaseStore.ReleaseAsync(lease.LeaseId.Value, lease.FenceToken, operationId, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidatePreparedTarget(RuntimeModePreparedTarget prepared, OperationalMode targetMode)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(prepared.Policy);
        ArgumentNullException.ThrowIfNull(prepared.Actions);
        if (prepared.Actions.Count == 0)
            throw new InvalidDataException("Executable mode target must contain at least one durable action.");
        var expectedTarget = targetMode switch
        {
            OperationalMode.None => ModePolicyTarget.Base,
            OperationalMode.Work => ModePolicyTarget.Work,
            OperationalMode.Game => ModePolicyTarget.Game,
            _ => throw new ArgumentOutOfRangeException(nameof(targetMode))
        };
        if (prepared.Policy.Target != expectedTarget)
            throw new InvalidDataException("Prepared policy target does not match requested operational mode.");
        if (prepared.Policy.Identity.PolicyVersion < 1 ||
            string.IsNullOrWhiteSpace(prepared.Policy.Identity.PolicyCatalogId) ||
            string.IsNullOrWhiteSpace(prepared.Policy.Identity.ReleaseId) ||
            !IsDigest(prepared.Policy.Identity.CatalogDigest) ||
            !IsDigest(prepared.Policy.ResolvedDigest))
        {
            throw new InvalidDataException("Prepared policy identity/digest is malformed.");
        }
        foreach (var action in prepared.Actions)
        {
            if (!string.Equals(action.OwningModule, SplitOS.Contracts.Protocol.ManagedServicePolicyActionContract.OwningModule, StringComparison.Ordinal) ||
                !string.Equals(action.ActionType, SplitOS.Contracts.Protocol.ManagedServicePolicyActionContract.ActionType, StringComparison.Ordinal) ||
                !string.Equals(action.TargetRef, SplitOS.Contracts.Protocol.ManagedServicePolicyActionContract.TargetRef, StringComparison.Ordinal))
            {
                throw new InvalidDataException("This orchestrator increment accepts only typed managed-service actions.");
            }
        }
    }

    private static string? ValidateActivationEpoch(
        ModeOperationPlan plan,
        OperationalModeRecord source,
        Guid? activationEpochId)
    {
        if (plan.TargetMode == OperationalMode.None)
            return activationEpochId.HasValue ? "DEACTIVATE to NONE must not carry an activation epoch." : null;
        if (!activationEpochId.HasValue || activationEpochId.Value == Guid.Empty)
            return "Managed target requires an activation epoch.";
        if (plan.OperationKind == ModeOperationKind.Switch && source.ActivationEpochId != activationEpochId)
            return "WORK/GAME switch must preserve the canonical activation epoch.";
        return null;
    }

    private static bool IsDigest(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void ValidateCommand(RuntimeModeExecutionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.RuntimeAccess);
        if (command.OperationId == Guid.Empty || command.CorrelationId == Guid.Empty || command.TransitionId == Guid.Empty)
            throw new ArgumentException("Mode execution identifiers must not be empty.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.ControlSessionKey) || command.ControlSessionKey.Length > 256 || command.ControlSessionKey.Any(char.IsControl))
            throw new ArgumentException("ControlSessionKey is outside supported bounds.", nameof(command));
        if (command.RuntimeAccessObservedUtc == default)
            throw new ArgumentException("Runtime access observation timestamp is required.", nameof(command));
        if (command.LeaseLifetime <= TimeSpan.Zero || command.LeaseLifetime > SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.MaximumLeaseLifetime)
            throw new ArgumentOutOfRangeException(nameof(command), "Lease lifetime is outside supported bounds.");
    }

    private static OperationalMode ParseMode(string value) => value switch
    {
        "NONE" => OperationalMode.None,
        "WORK" => OperationalMode.Work,
        "GAME" => OperationalMode.Game,
        _ => throw new InvalidDataException($"Unknown canonical operational mode '{value}'.")
    };

    private static string ToStorageMode(OperationalMode value) => value switch
    {
        OperationalMode.None => "NONE",
        OperationalMode.Work => "WORK",
        OperationalMode.Game => "GAME",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static PersistedModeOperationKind ToPersisted(ModeOperationKind value) => value switch
    {
        ModeOperationKind.Activate => PersistedModeOperationKind.Activate,
        ModeOperationKind.Switch => PersistedModeOperationKind.Switch,
        ModeOperationKind.Deactivate => PersistedModeOperationKind.Deactivate,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static PersistedModePolicyTarget ToPersisted(ModePolicyTarget value) => value switch
    {
        ModePolicyTarget.Base => PersistedModePolicyTarget.Base,
        ModePolicyTarget.Work => PersistedModePolicyTarget.Work,
        ModePolicyTarget.Game => PersistedModePolicyTarget.Game,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static PersistedModePolicyFallbackSelection ToPersisted(ModePolicyFallbackSelection value)
        => new(
            value.RuleId,
            value.FallbackClass switch
            {
                ModePolicyFallbackClass.ReleaseDefault => PersistedModePolicyFallbackClass.ReleaseDefault,
                ModePolicyFallbackClass.ApprovedAlternate => PersistedModePolicyFallbackClass.ApprovedAlternate,
                ModePolicyFallbackClass.PreserveCurrent => PersistedModePolicyFallbackClass.PreserveCurrent,
                _ => throw new InvalidDataException("Resolved policy selected an unsupported fallback class.")
            },
            value.TargetId);
}
