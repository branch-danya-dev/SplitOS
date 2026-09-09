using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ModeTransitionActionJournalStoreTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task BeginApplyCapturesPreStateAndAuthorizesBrokerFence()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage);
        const string preState = "{\"activePowerScheme\":\"balanced\"}";
        var preDigest = Digest(preState);
        var action = context.Actions[0];

        var started = await context.Journal.BeginApplyAsync(
            context.TransitionId,
            action.ActionId,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            preState,
            preDigest);

        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, started.Disposition, started.Detail);
        Assert.AreEqual(PersistedModeActionState.Applying, started.Action?.State);
        Assert.AreEqual(2, started.Action?.Revision);
        Assert.AreEqual(preState, started.Action?.PreStateJson);
        Assert.AreEqual(preDigest, started.Action?.PreStateDigest);
        Assert.AreEqual(StartUtc, started.Action?.StartedUtc);

        var fence = await context.Fence.ValidateAsync(new ModeMutationFenceContext(
            context.TransitionId,
            action.ActionId,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            context.CorrelationId,
            context.ControlSessionKey,
            started.Action!.Revision));
        Assert.AreEqual(ModeMutationFenceValidationDisposition.Authorized, fence.Disposition, fence.Detail);

        var replay = await context.Journal.BeginApplyAsync(
            context.TransitionId,
            action.ActionId,
            1,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            preState,
            preDigest);
        Assert.AreEqual(ModeActionAdvanceDisposition.Replayed, replay.Disposition, replay.Detail);
        Assert.AreEqual(2, replay.Action?.Revision);

        var reopened = new ModeTransitionActionJournalStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await reopened.InitializeAsync();
        var restored = await reopened.GetAsync(action.ActionId);
        Assert.AreEqual(PersistedModeActionState.Applying, restored?.State);
        Assert.AreEqual(preState, restored?.PreStateJson);
        Assert.AreEqual(preDigest, restored?.PreStateDigest);
        Assert.AreEqual(2, restored?.Revision);
    }

    [TestMethod]
    public async Task ApplyIsOrderedAndImmediateResultsAreDurable()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage);
        var first = context.Actions[0];
        var second = context.Actions[1];

        var outOfOrder = await context.Journal.BeginApplyAsync(
            context.TransitionId,
            second.ActionId,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.InvalidLifecycle, outOfOrder.Disposition);
        Assert.AreEqual("MODE_ACTION_APPLY_ORDER_BLOCKED", outOfOrder.ProductCode);

        var firstStarted = await context.Journal.BeginApplyAsync(
            context.TransitionId,
            first.ActionId,
            1,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, firstStarted.Disposition, firstStarted.Detail);

        var firstApplied = await context.Journal.RecordApplyResultAsync(
            context.TransitionId,
            first.ActionId,
            firstStarted.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeApplyResult.Applied);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, firstApplied.Disposition, firstApplied.Detail);
        Assert.AreEqual(PersistedModeActionState.Applied, firstApplied.Action?.State);
        Assert.AreEqual("APPLIED", firstApplied.Action?.ApplyResultCode);
        Assert.AreEqual(StartUtc, firstApplied.Action?.AppliedUtc);
        Assert.AreEqual(3, firstApplied.Action?.Revision);

        var secondStarted = await context.Journal.BeginApplyAsync(
            context.TransitionId,
            second.ActionId,
            1,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, secondStarted.Disposition, secondStarted.Detail);

        var secondUnknown = await context.Journal.RecordApplyResultAsync(
            context.TransitionId,
            second.ActionId,
            secondStarted.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeApplyResult.Unknown);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, secondUnknown.Disposition, secondUnknown.Detail);
        Assert.AreEqual(PersistedModeActionState.Failed, secondUnknown.Action?.State);
        Assert.AreEqual("UNKNOWN", secondUnknown.Action?.ApplyResultCode);
        Assert.IsNull(secondUnknown.Action?.AppliedUtc);

        var restoredFirst = await context.Journal.GetAsync(first.ActionId);
        var restoredSecond = await context.Journal.GetAsync(second.ActionId);
        Assert.AreEqual(PersistedModeActionState.Applied, restoredFirst?.State);
        Assert.AreEqual(PersistedModeActionState.Failed, restoredSecond?.State);
    }

    [TestMethod]
    public async Task OptionalActionMayBeSkippedButMandatoryActionMayNot()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage);
        var first = context.Actions[0];
        var second = context.Actions[1];

        var mandatorySkip = await context.Journal.SkipOptionalAsync(
            context.TransitionId,
            first.ActionId,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.InvalidLifecycle, mandatorySkip.Disposition);
        Assert.AreEqual("MODE_ACTION_MANDATORY_CANNOT_SKIP", mandatorySkip.ProductCode);

        var firstStarted = await context.Journal.BeginApplyAsync(
            context.TransitionId,
            first.ActionId,
            1,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        var firstApplied = await context.Journal.RecordApplyResultAsync(
            context.TransitionId,
            first.ActionId,
            firstStarted.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeApplyResult.Applied);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, firstApplied.Disposition, firstApplied.Detail);

        var skipped = await context.Journal.SkipOptionalAsync(
            context.TransitionId,
            second.ActionId,
            1,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, skipped.Disposition, skipped.Detail);
        Assert.AreEqual(PersistedModeActionState.Skipped, skipped.Action?.State);
        Assert.AreEqual("SKIPPED", skipped.Action?.ApplyResultCode);
    }

    [TestMethod]
    public async Task VerifyLifecyclePersistsVerifiedAndMismatchEvidence()
    {
        using var verifiedStorage = new TestStorage();
        var verified = await CreateVerifyingContextAsync(verifiedStorage);
        var first = verified.Actions[0];

        var verifyStarted = await verified.Journal.BeginVerifyAsync(
            verified.TransitionId,
            first.ActionId,
            3,
            verified.Lease.LeaseId!.Value,
            verified.Lease.FenceToken,
            verified.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, verifyStarted.Disposition, verifyStarted.Detail);
        Assert.AreEqual(PersistedModeActionState.Verifying, verifyStarted.Action?.State);

        var verifiedResult = await verified.Journal.RecordVerifyResultAsync(
            verified.TransitionId,
            first.ActionId,
            verifyStarted.Action!.Revision,
            verified.Lease.LeaseId.Value,
            verified.Lease.FenceToken,
            verified.OperationId,
            PersistedModeVerifyResult.Verified);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, verifiedResult.Disposition, verifiedResult.Detail);
        Assert.AreEqual(PersistedModeActionState.Verified, verifiedResult.Action?.State);
        Assert.AreEqual("VERIFIED", verifiedResult.Action?.VerifyResultCode);
        Assert.AreEqual(StartUtc, verifiedResult.Action?.VerifiedUtc);

        using var mismatchStorage = new TestStorage();
        var mismatch = await CreateVerifyingContextAsync(mismatchStorage);
        var mismatchFirst = mismatch.Actions[0];
        var mismatchStarted = await mismatch.Journal.BeginVerifyAsync(
            mismatch.TransitionId,
            mismatchFirst.ActionId,
            3,
            mismatch.Lease.LeaseId!.Value,
            mismatch.Lease.FenceToken,
            mismatch.OperationId);
        var mismatchResult = await mismatch.Journal.RecordVerifyResultAsync(
            mismatch.TransitionId,
            mismatchFirst.ActionId,
            mismatchStarted.Action!.Revision,
            mismatch.Lease.LeaseId.Value,
            mismatch.Lease.FenceToken,
            mismatch.OperationId,
            PersistedModeVerifyResult.Mismatch);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, mismatchResult.Disposition, mismatchResult.Detail);
        Assert.AreEqual(PersistedModeActionState.Failed, mismatchResult.Action?.State);
        Assert.AreEqual("MISMATCH", mismatchResult.Action?.VerifyResultCode);
        Assert.IsNull(mismatchResult.Action?.VerifiedUtc);
    }

    [TestMethod]
    public async Task StaleFenceAndExpiredLeaseFailClosed()
    {
        using var staleStorage = new TestStorage();
        var stale = await CreateApplyingContextAsync(staleStorage);
        var staleOutcome = await stale.Journal.BeginApplyAsync(
            stale.TransitionId,
            stale.Actions[0].ActionId,
            1,
            stale.Lease.LeaseId!.Value,
            stale.Lease.FenceToken + 1,
            stale.OperationId);
        Assert.IsTrue(
            staleOutcome.Disposition is ModeActionAdvanceDisposition.LeaseConflict or ModeActionAdvanceDisposition.OwnershipConflict,
            staleOutcome.Detail);

        using var expiredStorage = new TestStorage();
        var expired = await CreateApplyingContextAsync(expiredStorage, TimeSpan.FromSeconds(30));
        expired.Time.Advance(TimeSpan.FromSeconds(31));
        var expiredOutcome = await expired.Journal.BeginApplyAsync(
            expired.TransitionId,
            expired.Actions[0].ActionId,
            1,
            expired.Lease.LeaseId!.Value,
            expired.Lease.FenceToken,
            expired.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.ReconciliationRequired, expiredOutcome.Disposition, expiredOutcome.Detail);
        Assert.AreEqual("MUTATION_LEASE_RECONCILIATION_REQUIRED", expiredOutcome.ProductCode);
    }

    private static async Task<TestContext> CreateVerifyingContextAsync(TestStorage storage)
    {
        var context = await CreateApplyingContextAsync(storage);
        var first = context.Actions[0];
        var second = context.Actions[1];

        var firstStarted = await context.Journal.BeginApplyAsync(
            context.TransitionId,
            first.ActionId,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId);
        var firstApplied = await context.Journal.RecordApplyResultAsync(
            context.TransitionId,
            first.ActionId,
            firstStarted.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeApplyResult.Applied);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, firstApplied.Disposition, firstApplied.Detail);

        var skipped = await context.Journal.SkipOptionalAsync(
            context.TransitionId,
            second.ActionId,
            1,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, skipped.Disposition, skipped.Detail);

        var applyComplete = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyComplete,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, applyComplete.Disposition, applyComplete.Detail);

        var verifying = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            applyComplete.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Verifying,
            PersistedModeTransitionStage.VerifyStarted,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, verifying.Disposition, verifying.Detail);

        return context with { TransitionRevision = verifying.Transition!.Revision };
    }

    private static async Task<TestContext> CreateApplyingContextAsync(
        TestStorage storage,
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
        const string session = "console-session-action-journal";
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

        var policies = new ModeTransitionPolicyStore(db, marker, quarantineMarker, time);
        await policies.InitializeAsync();
        var bound = await policies.BindResolvedPolicyAsync(
            transitionId,
            resolving.Transition!.Revision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            new PersistedModePolicyIdentity("mode-policy.action-journal", 1, "development", new string('a', 64)),
            PersistedModePolicyTarget.Work,
            new string('b', 64));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);

        var actions = TestActions();
        var plans = new ModeTransitionActionPlanStore(db, marker, quarantineMarker, time);
        await plans.InitializeAsync();
        var persisted = await plans.PersistActionPlanAsync(
            transitionId,
            bound.Binding!.TransitionRevision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            actions);
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, persisted.Disposition, persisted.Detail);

        var ready = await transitions.AdvanceAsync(
            transitionId,
            persisted.Plan!.TransitionRevision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, ready.Disposition, ready.Detail);

        var applying = await transitions.AdvanceAsync(
            transitionId,
            ready.Transition!.Revision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyStarted,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, applying.Disposition, applying.Detail);

        var journal = new ModeTransitionActionJournalStore(db, marker, quarantineMarker, time);
        await journal.InitializeAsync();
        var fence = new ModeMutationFenceStore(db, marker, quarantineMarker, time);
        await fence.InitializeAsync();

        return new TestContext(
            db,
            marker,
            quarantineMarker,
            time,
            transitions,
            journal,
            fence,
            acquired.Lease,
            operationId,
            correlationId,
            session,
            transitionId,
            applying.Transition!.Revision,
            actions);
    }

    private static PersistedModeActionDefinition[] TestActions()
    {
        const string firstJson = "{\"powerMode\":\"target\"}";
        const string secondJson = "{\"optionalService\":\"target\"}";
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
                "service",
                "managed.apply",
                "service/optional",
                1,
                secondJson,
                Digest(secondJson),
                false,
                "restore.pre_state",
                "service.confirmed")
        ];
    }

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record TestContext(
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        ManualTimeProvider Time,
        ModeTransitionStore Transitions,
        ModeTransitionActionJournalStore Journal,
        ModeMutationFenceStore Fence,
        MachineMutationLeaseRecord Lease,
        Guid OperationId,
        Guid CorrelationId,
        string ControlSessionKey,
        Guid TransitionId,
        int TransitionRevision,
        PersistedModeActionDefinition[] Actions);

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
