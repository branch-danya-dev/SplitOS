namespace SplitOS.RuntimeHost.ModeRuntime;

public enum OperationalMode
{
    None,
    Work,
    Game
}

public enum ModeOperationKind
{
    Activate,
    Switch,
    Deactivate
}

public enum ModeOperationPlanDisposition
{
    Accepted,
    NoOp
}

public enum ModeTransitionState
{
    Requested,
    Inspecting,
    Blocked,
    AwaitingUser,
    Resolving,
    Applying,
    Verifying,
    Committing,
    RollingBack,
    Completed,
    Cancelled,
    FailedWithSafeFallback
}

public enum ModeTransitionStage
{
    Accepted,
    InspectionStarted,
    InspectionComplete,
    WaitingForUser,
    ResolutionStarted,
    ActionPlanReady,
    ApplyStarted,
    ApplyComplete,
    VerifyStarted,
    VerifyComplete,
    CommitStarted,
    CommitDurable,
    FinalizationStarted,
    RollbackStarted,
    RollbackVerify,
    Terminal
}

public sealed record ModeOperationPlan(
    ModeOperationPlanDisposition Disposition,
    ModeOperationKind? OperationKind,
    OperationalMode SourceMode,
    OperationalMode TargetMode,
    int SourceModeRevision,
    Guid OperationId,
    Guid CorrelationId,
    string ControlSessionKey)
{
    public bool CreatesTransition => Disposition == ModeOperationPlanDisposition.Accepted;
}

/// <summary>
/// Pure SPEC-05 semantic core. It classifies mode intents and validates lifecycle/commit preconditions,
/// but performs no persistence, IPC or Windows mutation. Those boundaries remain owned by later Slice-03
/// repository/orchestration increments.
/// </summary>
public static class ModeTransitionDomain
{
    private static readonly IReadOnlyDictionary<ModeTransitionState, HashSet<ModeTransitionState>> StateGraph =
        new Dictionary<ModeTransitionState, HashSet<ModeTransitionState>>
        {
            [ModeTransitionState.Requested] = new() { ModeTransitionState.Inspecting },
            [ModeTransitionState.Inspecting] = new()
            {
                ModeTransitionState.Blocked,
                ModeTransitionState.AwaitingUser,
                ModeTransitionState.Resolving
            },
            [ModeTransitionState.Blocked] = new()
            {
                ModeTransitionState.Inspecting,
                ModeTransitionState.Cancelled
            },
            [ModeTransitionState.AwaitingUser] = new()
            {
                ModeTransitionState.Resolving,
                ModeTransitionState.Cancelled
            },
            [ModeTransitionState.Resolving] = new()
            {
                ModeTransitionState.Applying,
                ModeTransitionState.Cancelled
            },
            [ModeTransitionState.Applying] = new()
            {
                ModeTransitionState.Verifying,
                ModeTransitionState.RollingBack
            },
            [ModeTransitionState.Verifying] = new()
            {
                ModeTransitionState.Committing,
                ModeTransitionState.RollingBack
            },
            [ModeTransitionState.Committing] = new() { ModeTransitionState.Completed },
            [ModeTransitionState.RollingBack] = new()
            {
                ModeTransitionState.Cancelled,
                ModeTransitionState.FailedWithSafeFallback
            },
            [ModeTransitionState.Completed] = new(),
            [ModeTransitionState.Cancelled] = new(),
            [ModeTransitionState.FailedWithSafeFallback] = new()
        };

    public static ModeOperationPlan Plan(
        OperationalMode sourceMode,
        OperationalMode targetMode,
        int sourceModeRevision,
        Guid operationId,
        Guid correlationId,
        string controlSessionKey)
    {
        if (sourceModeRevision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceModeRevision));
        }

        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("Mode operation id must not be empty.", nameof(operationId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Mode operation correlation id must not be empty.", nameof(correlationId));
        }

        ValidateControlSessionKey(controlSessionKey);

        if (sourceMode == targetMode)
        {
            return new ModeOperationPlan(
                ModeOperationPlanDisposition.NoOp,
                null,
                sourceMode,
                targetMode,
                sourceModeRevision,
                operationId,
                correlationId,
                controlSessionKey);
        }

        var kind = (sourceMode, targetMode) switch
        {
            (OperationalMode.None, OperationalMode.Work or OperationalMode.Game) => ModeOperationKind.Activate,
            (OperationalMode.Work, OperationalMode.Game) or
            (OperationalMode.Game, OperationalMode.Work) => ModeOperationKind.Switch,
            (OperationalMode.Work or OperationalMode.Game, OperationalMode.None) => ModeOperationKind.Deactivate,
            _ => throw new InvalidOperationException("Unsupported operational-mode transition tuple.")
        };

        ValidateTuple(kind, sourceMode, targetMode);
        return new ModeOperationPlan(
            ModeOperationPlanDisposition.Accepted,
            kind,
            sourceMode,
            targetMode,
            sourceModeRevision,
            operationId,
            correlationId,
            controlSessionKey);
    }

    public static void ValidateTuple(
        ModeOperationKind operationKind,
        OperationalMode sourceMode,
        OperationalMode targetMode)
    {
        var valid = operationKind switch
        {
            ModeOperationKind.Activate =>
                sourceMode == OperationalMode.None && targetMode is OperationalMode.Work or OperationalMode.Game,
            ModeOperationKind.Switch =>
                (sourceMode == OperationalMode.Work && targetMode == OperationalMode.Game) ||
                (sourceMode == OperationalMode.Game && targetMode == OperationalMode.Work),
            ModeOperationKind.Deactivate =>
                sourceMode is OperationalMode.Work or OperationalMode.Game && targetMode == OperationalMode.None,
            _ => false
        };

        if (!valid)
        {
            throw new InvalidOperationException(
                $"Operation kind {operationKind} is incompatible with {sourceMode} -> {targetMode}.");
        }
    }

    public static bool CanAdvanceState(ModeTransitionState current, ModeTransitionState next)
        => StateGraph.TryGetValue(current, out var allowed) && allowed.Contains(next);

    public static void ValidateStateAdvance(ModeTransitionState current, ModeTransitionState next)
    {
        if (!CanAdvanceState(current, next))
        {
            throw new InvalidOperationException($"Mode transition cannot advance from {current} to {next}.");
        }
    }

    public static void ValidateStateStage(
        ModeTransitionState state,
        ModeTransitionStage stage,
        bool commitDurable)
    {
        var validPair = state switch
        {
            ModeTransitionState.Requested => stage == ModeTransitionStage.Accepted,
            ModeTransitionState.Inspecting => stage is ModeTransitionStage.InspectionStarted or ModeTransitionStage.InspectionComplete,
            ModeTransitionState.Blocked => stage == ModeTransitionStage.InspectionComplete,
            ModeTransitionState.AwaitingUser => stage == ModeTransitionStage.WaitingForUser,
            ModeTransitionState.Resolving => stage is ModeTransitionStage.ResolutionStarted or ModeTransitionStage.ActionPlanReady,
            ModeTransitionState.Applying => stage is ModeTransitionStage.ApplyStarted or ModeTransitionStage.ApplyComplete,
            ModeTransitionState.Verifying => stage is ModeTransitionStage.VerifyStarted or ModeTransitionStage.VerifyComplete,
            ModeTransitionState.Committing => stage is ModeTransitionStage.CommitStarted or ModeTransitionStage.CommitDurable or ModeTransitionStage.FinalizationStarted,
            ModeTransitionState.RollingBack => stage is ModeTransitionStage.RollbackStarted or ModeTransitionStage.RollbackVerify,
            ModeTransitionState.Completed or ModeTransitionState.Cancelled or ModeTransitionState.FailedWithSafeFallback =>
                stage == ModeTransitionStage.Terminal,
            _ => false
        };

        if (!validPair)
        {
            throw new InvalidOperationException($"Stage {stage} is not valid for transition state {state}.");
        }

        if (stage == ModeTransitionStage.CommitDurable && !commitDurable)
        {
            throw new InvalidOperationException("COMMIT_DURABLE stage requires a durable commit marker.");
        }

        if (state == ModeTransitionState.Completed && !commitDurable)
        {
            throw new InvalidOperationException("COMPLETED transition requires a durable target commit.");
        }

        if (state is ModeTransitionState.Cancelled or ModeTransitionState.FailedWithSafeFallback && commitDurable)
        {
            throw new InvalidOperationException("Rollback/cancellation terminal outcomes cannot claim the target commit was durable.");
        }
    }

    public static bool CanCommitTarget(
        ModeOperationKind operationKind,
        OperationalMode targetMode,
        bool leaseFenceCurrent,
        bool sourceRevisionCurrent,
        bool mandatoryTargetVerified,
        bool runtimeAccessPermitsTarget,
        bool policyIdentityCompatible)
    {
        if (!leaseFenceCurrent ||
            !sourceRevisionCurrent ||
            !mandatoryTargetVerified ||
            !policyIdentityCompatible)
        {
            return false;
        }

        if (operationKind == ModeOperationKind.Deactivate)
        {
            return targetMode == OperationalMode.None;
        }

        return targetMode is OperationalMode.Work or OperationalMode.Game && runtimeAccessPermitsTarget;
    }

    private static void ValidateControlSessionKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 256 ||
            value.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException(
                "Control-session key must be a bounded OS-derived semantic identity.",
                nameof(value));
        }
    }
}
