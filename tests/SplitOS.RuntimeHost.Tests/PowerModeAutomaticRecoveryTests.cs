using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

public sealed partial class RuntimeModeOrchestratorTests
{
    private static readonly Guid BalancedPowerScheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid PerformancePowerScheme = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    [TestMethod]
    public async Task PowerAutomaticRecoveryRestoresDurableSourceAfterRuntimeRestartAndReplayIsIdempotent()
    {
        var fixture = await CreateFixtureAsync(false);
        var session = new FixedPowerSession(fixture.Command.ControlSessionKey);
        var power = new CrashRecoveryPowerState(BalancedPowerScheme)
        {
            OverrideQueryCall = 4,
            OverrideQueryValue = BalancedPowerScheme
        };
        var catalog = PowerCatalog();
        var orchestrator = CreatePowerOrchestrator(fixture, power, catalog, session);

        var forward = await orchestrator.ExecuteAsync(fixture.Command);

        Assert.AreEqual(RuntimeModeExecutionDisposition.ActionRejected, forward.Disposition, forward.Detail);
        Assert.IsNotNull(forward.Transition);
        Assert.AreEqual(PersistedModeTransitionState.RollingBack, forward.Transition.TransitionState);
        Assert.AreEqual(PerformancePowerScheme, power.ActiveSchemeId);
        Assert.AreEqual(1, power.SetCalls);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);

        power.OverrideQueryCall = null;
        fixture.Time.Advance(TimeSpan.FromMinutes(3));

        var recovered = await CreatePowerAutomaticRecovery(fixture, power, session).ReconcileAsync();

        Assert.IsTrue(recovered.MayCheckAccess, recovered.ProductCode);
        Assert.AreEqual(BalancedPowerScheme, power.ActiveSchemeId);
        Assert.AreEqual(2, power.SetCalls);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        var terminal = (await fixture.Transitions.GetAsync(fixture.Command.TransitionId))!;
        Assert.AreEqual(PersistedModeTransitionState.FailedWithSafeFallback, terminal.TransitionState);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);

        var plan = await ((IModeTransitionActionPlanStore)fixture.Proxy).GetAsync(fixture.Command.TransitionId);
        Assert.IsNotNull(plan);
        var durable = await ((IModeTransitionActionJournalStore)fixture.Proxy).GetAsync(plan.Actions.Single().ActionId);
        Assert.IsNotNull(durable);
        Assert.AreEqual(PersistedModeActionState.RolledBack, durable.State);
        Assert.AreEqual("ROLLED_BACK", durable.RollbackResultCode);
        Assert.AreEqual(BalancedPowerScheme, PowerModeActionContract.DeserializePreState(durable.PreStateJson!).ActiveSchemeId);

        // A second process restart over the same durable database must be idle and must not replay
        // PowerSetActiveScheme once the transition is terminal and the actual source is restored.
        var replay = await CreatePowerAutomaticRecovery(fixture, power, session).ReconcileAsync();
        Assert.IsTrue(replay.MayCheckAccess, replay.ProductCode);
        Assert.AreEqual(2, power.SetCalls);
        Assert.AreEqual(BalancedPowerScheme, power.ActiveSchemeId);
    }

    [TestMethod]
    public async Task PowerAutomaticRecoveryFinishesJournalAfterCrashFollowingNativeRollbackWithoutRepeatingMutation()
    {
        var fixture = await CreateFixtureAsync(false);
        var session = new FixedPowerSession(fixture.Command.ControlSessionKey);
        var power = new CrashRecoveryPowerState(BalancedPowerScheme)
        {
            OverrideQueryCall = 4,
            OverrideQueryValue = BalancedPowerScheme
        };
        var catalog = PowerCatalog();
        var orchestrator = CreatePowerOrchestrator(fixture, power, catalog, session);

        var forward = await orchestrator.ExecuteAsync(fixture.Command);
        Assert.AreEqual(RuntimeModeExecutionDisposition.ActionRejected, forward.Disposition, forward.Detail);
        Assert.AreEqual(PerformancePowerScheme, power.ActiveSchemeId);
        Assert.AreEqual(1, power.SetCalls);
        power.OverrideQueryCall = null;

        // Model a process dying after Windows was restored but before the durable rollback result was
        // recorded. BeginRollback is durable, then invoke only the owning Power handler and drop its
        // successful reply instead of calling RecordRollbackResultAsync.
        var transition = (await fixture.Transitions.GetAsync(fixture.Command.TransitionId))!;
        IModeTransitionRollbackStore rollbackStore = fixture.Proxy;
        var candidate = await rollbackStore.GetNextRollbackCandidateAsync(transition.TransitionId);
        Assert.IsNotNull(candidate);
        var begun = await rollbackStore.BeginRollbackAsync(
            transition.TransitionId,
            candidate.ActionId,
            candidate.Revision,
            transition.LeaseId,
            transition.FenceToken,
            transition.OperationId);
        Assert.IsNotNull(begun.Action);
        Assert.AreEqual(PersistedModeActionState.RollingBack, begun.Action.State);

        var handler = new PowerModeActionRollbackHandler(power, power, session);
        var native = await handler.RollbackAsync(
            new ModeActionRollbackCommand(
                transition.TransitionId,
                begun.Action.ActionId,
                begun.Action.Revision,
                transition.LeaseId,
                transition.FenceToken,
                transition.OperationId,
                transition.CorrelationId,
                transition.ControlSessionKey),
            begun.Action);
        Assert.AreEqual("VERIFIED", native.Disposition);
        Assert.AreEqual(BalancedPowerScheme, power.ActiveSchemeId);
        Assert.AreEqual(2, power.SetCalls);

        var stillUnsettled = await ((IModeTransitionActionJournalStore)fixture.Proxy).GetAsync(begun.Action.ActionId);
        Assert.IsNotNull(stillUnsettled);
        Assert.AreEqual(PersistedModeActionState.RollingBack, stillUnsettled.State);
        Assert.IsNull(stillUnsettled.RollbackResultCode);

        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var recovered = await CreatePowerAutomaticRecovery(fixture, power, session).ReconcileAsync();

        Assert.IsTrue(recovered.MayCheckAccess, recovered.ProductCode);
        Assert.AreEqual(BalancedPowerScheme, power.ActiveSchemeId);
        Assert.AreEqual(2, power.SetCalls, "Recovery must not repeat a native rollback already proved by fresh read-back.");
        var settled = await ((IModeTransitionActionJournalStore)fixture.Proxy).GetAsync(begun.Action.ActionId);
        Assert.IsNotNull(settled);
        Assert.AreEqual(PersistedModeActionState.RolledBack, settled.State);
        Assert.AreEqual("ROLLED_BACK", settled.RollbackResultCode);
        Assert.AreEqual(PersistedModeTransitionState.FailedWithSafeFallback,
            (await fixture.Transitions.GetAsync(fixture.Command.TransitionId))!.TransitionState);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
    }

    private static RuntimeModeOrchestrator CreatePowerOrchestrator(
        Fixture fixture,
        CrashRecoveryPowerState power,
        IPowerPolicyCatalogResolver catalog,
        IControlSessionIdentity session)
    {
        var reader = new ModeActionJournalRecordReader(fixture.Proxy);
        var coordinator = new PowerSchemeApplyCoordinator(catalog, power, power);
        var apply = new ModeActionApplyDispatcher(
            reader,
            new IModeActionApplyHandler[]
            {
                new PowerModeActionApplyHandler(fixture.Proxy, power, coordinator, session)
            });
        var verify = new ModeActionVerifyDispatcher(
            reader,
            new IModeActionVerifyHandler[]
            {
                new PowerModeActionVerifyHandler(fixture.Proxy, power, catalog, session)
            });

        return new RuntimeModeOrchestrator(
            fixture.Proxy,
            fixture.Proxy,
            fixture.Proxy,
            fixture.Proxy,
            fixture.Proxy,
            apply,
            verify,
            fixture.Proxy,
            new ModeBlockerEngine(Array.Empty<IModeBlockerProvider>(), fixture.Time),
            new PowerRecoveryPreparationProvider(),
            fixture.Time);
    }

    private static RuntimeModeAutomaticRecoveryCoordinator CreatePowerAutomaticRecovery(
        Fixture fixture,
        CrashRecoveryPowerState power,
        IControlSessionIdentity session)
    {
        var authority = new RuntimeModeSourceAuthority(fixture.Proxy, session, new EnabledPowerRecoveryAccess());
        var rollbackExecutor = new ModeActionRollbackDispatcher(
            new IModeActionRollbackHandler[] { new PowerModeActionRollbackHandler(power, power, session) });
        var rollback = new ManagedServiceActionRollbackCoordinator(
            fixture.Proxy,
            fixture.Proxy,
            fixture.Proxy,
            fixture.Proxy,
            authority,
            rollbackExecutor);

        var planReader = new ModeActionPlanReader(fixture.Proxy);
        var actionReader = new ModeActionJournalRecordReader(fixture.Proxy);
        var sourceVerification = new ModeSourceVerificationDispatcher(
            planReader,
            new IModeSourceVerificationHandler[]
            {
                new PowerModeSourceVerificationHandler(planReader, actionReader, power, session)
            });
        var completion = new RuntimeModeRollbackCompletionCoordinator(
            fixture.Proxy,
            fixture.Proxy,
            authority,
            sourceVerification);

        return new RuntimeModeAutomaticRecoveryCoordinator(
            new RuntimeModeRecoveryCoordinator(fixture.Proxy, fixture.Proxy),
            fixture.Proxy,
            fixture.Proxy,
            fixture.Proxy,
            session,
            authority,
            rollback,
            completion);
    }

    private static PowerPolicyCatalogResolver PowerCatalog()
        => new(new[]
        {
            new PowerPolicyCatalogEntry(
                "GAME_PERFORMANCE",
                PowerPolicyResolutionKind.Scheme,
                PerformancePowerScheme)
        });

    private sealed class PowerRecoveryPreparationProvider : IRuntimeModeTargetPreparationProvider
    {
        public ValueTask<RuntimeModePreparedTarget> PrepareAsync(
            ModeOperationPlan operation,
            RuntimeAccessEvaluation runtimeAccess,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = operation.TargetMode switch
            {
                OperationalMode.None => ModePolicyTarget.Base,
                OperationalMode.Work => ModePolicyTarget.Work,
                OperationalMode.Game => ModePolicyTarget.Game,
                _ => throw new ArgumentOutOfRangeException()
            };
            var identity = new ModePolicyIdentity(
                "mode-policy.power-crash-recovery-test",
                1,
                "development",
                new string('c', 64));
            var policy = new ResolvedModePolicySnapshot(
                identity,
                target,
                Array.Empty<ModePolicyRule>(),
                Array.Empty<ModePolicyFallbackSelection>(),
                new string('d', 64));
            return ValueTask.FromResult(new RuntimeModePreparedTarget(
                policy,
                new[] { PowerModeActionContract.CreateDefinition(Guid.NewGuid(), 100, "GAME_PERFORMANCE") }));
        }
    }

    private sealed class FixedPowerSession(string key) : IControlSessionIdentity
    {
        public string GetCurrentKey() => key;
    }

    private sealed class EnabledPowerRecoveryAccess : ICurrentModeAccess
    {
        public ValueTask<RuntimeAccessEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RuntimeAccessEvaluation("ENABLED", "PRO_ONLINE_CONFIRMED", 1));
    }

    private sealed class CrashRecoveryPowerState(Guid activeSchemeId) : IPowerSchemeQuery, IPowerSchemeSetter
    {
        public Guid ActiveSchemeId { get; private set; } = activeSchemeId;
        public int QueryCalls { get; private set; }
        public int SetCalls { get; private set; }
        public int? OverrideQueryCall { get; set; }
        public Guid OverrideQueryValue { get; set; }

        public Guid QueryActiveScheme()
        {
            QueryCalls++;
            return OverrideQueryCall == QueryCalls ? OverrideQueryValue : ActiveSchemeId;
        }

        public PowerSetSchemeAttempt SetActiveScheme(Guid schemeId)
        {
            if (schemeId == Guid.Empty)
                throw new ArgumentException("Power scheme GUID must not be empty.", nameof(schemeId));
            SetCalls++;
            ActiveSchemeId = schemeId;
            return new PowerSetSchemeAttempt(0);
        }
    }
}
