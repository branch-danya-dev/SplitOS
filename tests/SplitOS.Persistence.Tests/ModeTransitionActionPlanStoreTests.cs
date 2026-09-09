using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ModeTransitionActionPlanStoreTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PersistPlanSurvivesRestartAndReplaysIdempotently()
    {
        using var storage = new TestStorage();
        var context = await CreateResolvingContextAsync(storage, bindPolicy: true);
        var actions = TestActions();

        var persisted = await context.Plans.PersistActionPlanAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            actions);

        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, persisted.Disposition, persisted.Detail);
        Assert.AreEqual(context.TransitionRevision + 1, persisted.Plan?.TransitionRevision);
        Assert.AreEqual(2, persisted.Plan?.ActionCount);
        Assert.IsTrue(persisted.Plan?.Actions.All(action => action.State == PersistedModeActionState.Planned) == true);
        Assert.IsTrue(persisted.Plan?.Actions.All(action => action.Revision == 1) == true);

        var replay = await context.Plans.PersistActionPlanAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            actions);
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Replayed, replay.Disposition, replay.Detail);
        Assert.AreEqual(persisted.Plan?.PlanDigest, replay.Plan?.PlanDigest);
        Assert.AreEqual(persisted.Plan?.TransitionRevision, replay.Plan?.TransitionRevision);

        var reopened = new ModeTransitionActionPlanStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await reopened.InitializeAsync();
        var restored = await reopened.GetAsync(context.TransitionId);
        Assert.AreEqual(persisted.Plan?.PlanDigest, restored?.PlanDigest);
        Assert.AreEqual(2, restored?.Actions.Count);
        Assert.AreEqual(actions[0].ActionId, restored?.Actions[0].ActionId);
        Assert.AreEqual(actions[1].DesiredStateJson, restored?.Actions[1].DesiredStateJson);
    }

    [TestMethod]
    public async Task DifferentSecondPlanIsRejectedPermanently()
    {
        using var storage = new TestStorage();
        var context = await CreateResolvingContextAsync(storage, bindPolicy: true);
        var actions = TestActions();
        var first = await context.Plans.PersistActionPlanAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            actions);
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, first.Disposition);

        var changed = actions.ToArray();
        changed[1] = changed[1] with { Mandatory = !changed[1].Mandatory };
        var conflict = await context.Plans.PersistActionPlanAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            changed);

        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.PlanConflict, conflict.Disposition);
        Assert.AreEqual("MODE_ACTION_PLAN_CONFLICT", conflict.ProductCode);
        Assert.AreEqual(first.Plan?.PlanDigest, (await context.Plans.GetAsync(context.TransitionId))?.PlanDigest);
    }

    [TestMethod]
    public async Task PersistRequiresPolicyCurrentFenceAndUnexpiredLease()
    {
        using var missingPolicyStorage = new TestStorage();
        var missingPolicy = await CreateResolvingContextAsync(missingPolicyStorage, bindPolicy: false);
        var denied = await missingPolicy.Plans.PersistActionPlanAsync(
            missingPolicy.TransitionId,
            missingPolicy.TransitionRevision,
            missingPolicy.Lease.LeaseId!.Value,
            missingPolicy.Lease.FenceToken,
            missingPolicy.OperationId,
            TestActions());
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.MissingPolicyBinding, denied.Disposition);

        using var staleStorage = new TestStorage();
        var stale = await CreateResolvingContextAsync(staleStorage, bindPolicy: true);
        var staleFence = await stale.Plans.PersistActionPlanAsync(
            stale.TransitionId,
            stale.TransitionRevision,
            stale.Lease.LeaseId!.Value,
            stale.Lease.FenceToken + 1,
            stale.OperationId,
            TestActions());
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.LeaseConflict, staleFence.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", staleFence.ProductCode);

        using var expiredStorage = new TestStorage();
        var expired = await CreateResolvingContextAsync(
            expiredStorage,
            bindPolicy: true,
            leaseLifetime: TimeSpan.FromSeconds(30));
        expired.Time.Advance(TimeSpan.FromSeconds(31));
        var expiredLease = await expired.Plans.PersistActionPlanAsync(
            expired.TransitionId,
            expired.TransitionRevision,
            expired.Lease.LeaseId!.Value,
            expired.Lease.FenceToken,
            expired.OperationId,
            TestActions());
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.ReconciliationRequired, expiredLease.Disposition);
        Assert.AreEqual("MUTATION_LEASE_RECONCILIATION_REQUIRED", expiredLease.ProductCode);
    }

    [TestMethod]
    public async Task ActionPlanReadyRequiresPersistedPolicyAndCompletePlan()
    {
        using var storage = new TestStorage();
        var context = await CreateResolvingContextAsync(storage, bindPolicy: true);

        var denied = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, denied.Disposition);
        StringAssert.Contains(denied.Detail, "action plan");

        var persisted = await context.Plans.PersistActionPlanAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            TestActions());
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, persisted.Disposition, persisted.Detail);

        var ready = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            persisted.Plan!.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, ready.Disposition, ready.Detail);
    }

    [TestMethod]
    public async Task ValidationRejectsDuplicateSequenceAndDigestMismatch()
    {
        using var storage = new TestStorage();
        var context = await CreateResolvingContextAsync(storage, bindPolicy: true);
        var duplicate = TestActions().ToArray();
        duplicate[1] = duplicate[1] with { SequenceNo = duplicate[0].SequenceNo };

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => context.Plans.PersistActionPlanAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            duplicate));

        var badDigest = TestActions().ToArray();
        badDigest[0] = badDigest[0] with { DesiredStateDigest = new string('f', 64) };
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => context.Plans.PersistActionPlanAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            badDigest));
    }

    private static PersistedModeActionDefinition[] TestActions()
    {
        const string firstJson = "{\"intent\":\"power.context\"}";
        const string secondJson = "{\"verification\":\"POWER_POLICY_CONFIRMED\"}";
        return
        [
            new PersistedModeActionDefinition(
                Guid.NewGuid(),
                100,
                "power",
                "context.apply",
                "power/default",
                1,
                firstJson,
                Digest(firstJson),
                true,
                "restore.pre_state",
                "power.confirmed"),
            new PersistedModeActionDefinition(
                Guid.NewGuid(),
                200,
                "verification",
                "evidence.require",
                "POWER_POLICY_CONFIRMED",
                1,
                secondJson,
                Digest(secondJson),
                true,
                "no_mutation",
                "evidence.required")
        ];
    }

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<TestContext> CreateResolvingContextAsync(
        TestStorage storage,
        bool bindPolicy,
        TimeSpan? leaseLifetime = null)
    {
        var db = storage.PathFor("machine.db");
        var marker = storage.PathFor("machine-store.initialized");
        var quarantineMarker = storage.PathFor("machine-store.quarantined.json");
        var time = new ManualTimeProvider(StartUtc);
        var machine = new MachineStateStore(
            db,
            marker,
            storage.PathFor("maintenance", "backups"),
            storage.PathFor("maintenance", "quarantine"),
            quarantineMarker);
        await machine.InitializeAsync();

        var leases = new MachineMutationLeaseStore(db, marker, quarantineMarker, time);
        await leases.InitializeAsync();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string session = "console-session-action-plan";
        var acquired = await leases.TryAcquireAsync(
            MachineMutationType.Mode,
            operationId,
            correlationId,
            session,
            leaseLifetime ?? TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, acquired.Disposition);

        var transitions = new ModeTransitionStore(db, marker, quarantineMarker, time);
        await transitions.InitializeAsync();
        var transitionId = Guid.NewGuid();
        var created = await transitions.CreateAsync(
            transitionId,
            operationId,
            correlationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "WORK",
            1,
            session,
            acquired.Lease.LeaseId!.Value,
            acquired.Lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);

        var inspecting = await transitions.AdvanceAsync(
            transitionId,
            1,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, inspecting.Disposition, inspecting.Detail);
        var resolving = await transitions.AdvanceAsync(
            transitionId,
            inspecting.Transition!.Revision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ResolutionStarted,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, resolving.Disposition, resolving.Detail);
        var revision = resolving.Transition!.Revision;

        if (bindPolicy)
        {
            var policies = new ModeTransitionPolicyStore(db, marker, quarantineMarker, time);
            await policies.InitializeAsync();
            var bound = await policies.BindResolvedPolicyAsync(
                transitionId,
                revision,
                acquired.Lease.LeaseId.Value,
                acquired.Lease.FenceToken,
                operationId,
                new PersistedModePolicyIdentity("mode-policy.action-plan", 1, "development", new string('a', 64)),
                PersistedModePolicyTarget.Work,
                new string('b', 64));
            Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);
            revision = bound.Binding!.TransitionRevision;
        }

        var plans = new ModeTransitionActionPlanStore(db, marker, quarantineMarker, time);
        await plans.InitializeAsync();
        return new TestContext(
            db,
            marker,
            quarantineMarker,
            time,
            transitions,
            plans,
            acquired.Lease,
            operationId,
            transitionId,
            revision);
    }

    private sealed record TestContext(
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        ManualTimeProvider Time,
        ModeTransitionStore Transitions,
        ModeTransitionActionPlanStore Plans,
        MachineMutationLeaseRecord Lease,
        Guid OperationId,
        Guid TransitionId,
        int TransitionRevision);

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
