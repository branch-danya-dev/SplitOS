using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class PowerModeActionApplyVerifyHandlerTests
{
    private static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid Performance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task DurablePowerActionFlowsFromPlannedThroughAppliedAndVerified()
    {
        var fixture = await CreateFixtureAsync();
        var query = new SequenceQuery(new[] { Balanced, Balanced, Performance, Performance });
        var setter = new RecordingSetter();
        var catalog = Catalog(new PowerPolicyCatalogEntry(
            "GAME_PERFORMANCE",
            PowerPolicyResolutionKind.Scheme,
            Performance));
        var identity = new FixedControlSessionIdentity(fixture.ControlSessionKey);
        var apply = new PowerModeActionApplyHandler(
            fixture.Journal,
            query,
            new PowerSchemeApplyCoordinator(catalog, query, setter),
            identity);

        var planned = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(planned);
        var applyOutcome = await apply.ApplyAsync(fixture.Command, planned);

        Assert.IsTrue(applyOutcome.IsApplied, applyOutcome.Detail);
        Assert.AreEqual("POWER_SCHEME_APPLIED_VERIFIED", applyOutcome.ProductCode);
        Assert.AreEqual(1, setter.Calls);
        Assert.AreEqual(Performance, setter.LastSchemeId);

        var applied = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(applied);
        Assert.AreEqual(PersistedModeActionState.Applied, applied.State);
        Assert.AreEqual("APPLIED", applied.ApplyResultCode);
        Assert.IsNotNull(applied.PreStateJson);
        Assert.IsNotNull(applied.PreStateDigest);
        Assert.AreEqual(Balanced, PowerModeActionContract.DeserializePreState(applied.PreStateJson).ActiveSchemeId);
        Assert.AreEqual(3, applied.Revision);

        var transitionRevision = await AdvanceAsync(
            fixture.Transitions,
            fixture.TransitionId,
            fixture.TransitionRevision,
            fixture.Lease,
            fixture.OperationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyComplete);
        transitionRevision = await AdvanceAsync(
            fixture.Transitions,
            fixture.TransitionId,
            transitionRevision,
            fixture.Lease,
            fixture.OperationId,
            PersistedModeTransitionState.Verifying,
            PersistedModeTransitionStage.VerifyStarted);

        var verify = new PowerModeActionVerifyHandler(
            fixture.Journal,
            query,
            catalog,
            identity);
        var verifyCommand = fixture.Command with { ExpectedActionRevision = applied.Revision };
        var verifyOutcome = await verify.VerifyAsync(verifyCommand, applied);

        Assert.IsTrue(verifyOutcome.IsVerified, verifyOutcome.Detail);
        Assert.AreEqual("MODE_POWER_SCHEME_VERIFIED", verifyOutcome.ProductCode);
        var verified = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(verified);
        Assert.AreEqual(PersistedModeActionState.Verified, verified.State);
        Assert.AreEqual("VERIFIED", verified.VerifyResultCode);
        Assert.AreEqual(5, verified.Revision);
        Assert.AreEqual(4, query.Calls);
    }

    [TestMethod]
    public async Task SourceDriftAfterDurablePreStateBlocksMutationAndRecordsFailure()
    {
        var fixture = await CreateFixtureAsync();
        var query = new SequenceQuery(new[] { Balanced, Performance });
        var setter = new RecordingSetter();
        var catalog = Catalog(new PowerPolicyCatalogEntry(
            "GAME_PERFORMANCE",
            PowerPolicyResolutionKind.Scheme,
            Performance));
        var handler = new PowerModeActionApplyHandler(
            fixture.Journal,
            query,
            new PowerSchemeApplyCoordinator(catalog, query, setter),
            new FixedControlSessionIdentity(fixture.ControlSessionKey));
        var planned = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(planned);

        var outcome = await handler.ApplyAsync(fixture.Command, planned);

        Assert.IsFalse(outcome.IsApplied);
        Assert.AreEqual("POWER_SCHEME_SOURCE_DRIFT", outcome.ProductCode);
        Assert.AreEqual(0, setter.Calls);
        var failed = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(failed);
        Assert.AreEqual(PersistedModeActionState.Failed, failed.State);
        Assert.AreEqual("FAILED", failed.ApplyResultCode);
        Assert.IsNotNull(failed.PreStateJson);
        Assert.AreEqual(Balanced, PowerModeActionContract.DeserializePreState(failed.PreStateJson).ActiveSchemeId);
        Assert.AreEqual(3, failed.Revision);
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "SplitOS.RuntimeHost.PowerAction.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var db = Path.Combine(_root, "machine.db");
        var marker = Path.Combine(_root, "machine-store.initialized");
        var quarantineMarker = Path.Combine(_root, "machine-store.quarantined.json");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero));
        var machine = new MachineStateStore(
            db,
            marker,
            Path.Combine(_root, "maintenance", "backups"),
            Path.Combine(_root, "maintenance", "quarantine"),
            quarantineMarker);
        await machine.InitializeAsync();

        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string controlSessionKey = "runtime-power-action-session";

        var leases = new MachineMutationLeaseStore(db, marker, quarantineMarker, time);
        await leases.InitializeAsync();
        var acquired = await leases.TryAcquireAsync(
            MachineMutationType.Mode,
            operationId,
            correlationId,
            controlSessionKey,
            TimeSpan.FromMinutes(5));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, acquired.Disposition);
        var lease = acquired.Lease;

        var transitions = new ModeTransitionStore(db, marker, quarantineMarker, time);
        await transitions.InitializeAsync();
        var transitionId = Guid.NewGuid();
        var created = await transitions.CreateAsync(
            transitionId,
            operationId,
            correlationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "GAME",
            1,
            controlSessionKey,
            lease.LeaseId!.Value,
            lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);
        var transitionRevision = created.Transition!.Revision;

        transitionRevision = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete);
        transitionRevision = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ResolutionStarted);

        var policyStore = new ModeTransitionPolicyStore(db, marker, quarantineMarker, time);
        await policyStore.InitializeAsync();
        var bound = await policyStore.BindResolvedPolicyAsync(
            transitionId,
            transitionRevision,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            new PersistedModePolicyIdentity(
                "mode-policy.runtime-power-action",
                1,
                "development",
                new string('a', 64)),
            PersistedModePolicyTarget.Game,
            new string('b', 64));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);
        transitionRevision = bound.Binding!.TransitionRevision;

        var actionId = Guid.NewGuid();
        var planStore = new ModeTransitionActionPlanStore(db, marker, quarantineMarker, time);
        await planStore.InitializeAsync();
        var plan = await planStore.PersistActionPlanAsync(
            transitionId,
            transitionRevision,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            [PowerModeActionContract.CreateDefinition(actionId, 100, "GAME_PERFORMANCE")]);
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, plan.Disposition, plan.Detail);
        transitionRevision = plan.Plan!.TransitionRevision;

        transitionRevision = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady);
        transitionRevision = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyStarted);

        var journal = new ModeTransitionActionJournalStore(db, marker, quarantineMarker, time);
        await journal.InitializeAsync();
        var command = new ModeActionExecutionCommand(
            transitionId,
            actionId,
            1,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            correlationId,
            controlSessionKey);
        return new Fixture(
            journal,
            transitions,
            command,
            actionId,
            transitionId,
            transitionRevision,
            operationId,
            lease,
            controlSessionKey);
    }

    private static async Task<int> AdvanceAsync(
        ModeTransitionStore transitions,
        Guid transitionId,
        int revision,
        MachineMutationLeaseRecord lease,
        Guid operationId,
        PersistedModeTransitionState state,
        PersistedModeTransitionStage stage)
    {
        var outcome = await transitions.AdvanceAsync(
            transitionId,
            revision,
            lease.LeaseId!.Value,
            lease.FenceToken,
            operationId,
            state,
            stage,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, outcome.Disposition, outcome.Detail);
        return outcome.Transition!.Revision;
    }

    private static PowerPolicyCatalogResolver Catalog(params PowerPolicyCatalogEntry[] entries)
        => new(entries);

    private sealed class SequenceQuery(IEnumerable<Guid> values) : IPowerSchemeQuery
    {
        private readonly Queue<Guid> _values = new(values);
        public int Calls { get; private set; }

        public Guid QueryActiveScheme()
        {
            Calls++;
            if (_values.Count == 0)
                throw new AssertFailedException("Unexpected power scheme query.");
            return _values.Dequeue();
        }
    }

    private sealed class RecordingSetter(int errorCode = 0) : IPowerSchemeSetter
    {
        public int Calls { get; private set; }
        public Guid? LastSchemeId { get; private set; }

        public PowerSetSchemeAttempt SetActiveScheme(Guid schemeId)
        {
            Calls++;
            LastSchemeId = schemeId;
            return new PowerSetSchemeAttempt(errorCode);
        }
    }

    private sealed class FixedControlSessionIdentity(string key) : IControlSessionIdentity
    {
        public string GetCurrentKey() => key;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record Fixture(
        ModeTransitionActionJournalStore Journal,
        ModeTransitionStore Transitions,
        ModeActionExecutionCommand Command,
        Guid ActionId,
        Guid TransitionId,
        int TransitionRevision,
        Guid OperationId,
        MachineMutationLeaseRecord Lease,
        string ControlSessionKey);
}
