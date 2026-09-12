using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ModeSourceVerificationDispatchTests
{
    [TestMethod]
    public async Task OneDomainHandlerRunsOnceForMultipleActions()
    {
        var transitionId = Guid.NewGuid();
        var first = Action(transitionId, 1, "services", "policy");
        var second = Action(transitionId, 2, "services", "policy");
        var handler = new FakeHandler("services", "policy", new("VERIFIED", "SERVICES_VERIFIED"));
        var dispatcher = new ModeSourceVerificationDispatcher(new FakePlanReader(Plan(transitionId, first, second)), new[] { handler });

        var result = await dispatcher.VerifySourceAsync(Command(transitionId));

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("SERVICES_VERIFIED", result.ProductCode);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task MissingDomainHandlerFailsClosedBeforeAnyVerifierRuns()
    {
        var transitionId = Guid.NewGuid();
        var supported = Action(transitionId, 1, "services", "policy");
        var unsupported = Action(transitionId, 2, "display", "topology");
        var handler = new FakeHandler("services", "policy", new("VERIFIED", "MUST_NOT_RUN"));
        var dispatcher = new ModeSourceVerificationDispatcher(new FakePlanReader(Plan(transitionId, supported, unsupported)), new[] { handler });

        var result = await dispatcher.VerifySourceAsync(Command(transitionId));

        Assert.AreEqual("RECONCILIATION_REQUIRED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_VERIFY_HANDLER_UNAVAILABLE", result.ProductCode);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task AmbiguousDomainHandlerFailsClosedBeforeAnyVerifierRuns()
    {
        var transitionId = Guid.NewGuid();
        var action = Action(transitionId, 1, "services", "policy");
        var first = new FakeHandler("services", "policy", new("VERIFIED", "FIRST"));
        var second = new FakeHandler("services", "policy", new("VERIFIED", "SECOND"));
        var dispatcher = new ModeSourceVerificationDispatcher(new FakePlanReader(Plan(transitionId, action)), new[] { first, second });

        var result = await dispatcher.VerifySourceAsync(Command(transitionId));

        Assert.AreEqual("RECONCILIATION_REQUIRED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_VERIFY_HANDLER_AMBIGUOUS", result.ProductCode);
        Assert.AreEqual(0, first.Calls);
        Assert.AreEqual(0, second.Calls);
    }

    [TestMethod]
    public async Task MultipleDomainsMustAllVerify()
    {
        var transitionId = Guid.NewGuid();
        var services = Action(transitionId, 1, "services", "policy");
        var display = Action(transitionId, 2, "display", "topology");
        var serviceHandler = new FakeHandler("services", "policy", new("VERIFIED", "SERVICES_VERIFIED"));
        var displayHandler = new FakeHandler("display", "topology", new("VERIFIED", "DISPLAY_VERIFIED"));
        var dispatcher = new ModeSourceVerificationDispatcher(
            new FakePlanReader(Plan(transitionId, services, display)),
            new IModeSourceVerificationHandler[] { serviceHandler, displayHandler });

        var result = await dispatcher.VerifySourceAsync(Command(transitionId));

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_ALL_DOMAINS_VERIFIED", result.ProductCode);
        Assert.AreEqual(1, serviceHandler.Calls);
        Assert.AreEqual(1, displayHandler.Calls);
    }

    [TestMethod]
    public async Task DomainMismatchStopsBeforeLaterVerifier()
    {
        var transitionId = Guid.NewGuid();
        var services = Action(transitionId, 1, "services", "policy");
        var display = Action(transitionId, 2, "display", "topology");
        var serviceHandler = new FakeHandler("services", "policy", new("MISMATCH", "SERVICES_NOT_RESTORED"));
        var displayHandler = new FakeHandler("display", "topology", new("VERIFIED", "DISPLAY_VERIFIED"));
        var dispatcher = new ModeSourceVerificationDispatcher(
            new FakePlanReader(Plan(transitionId, services, display)),
            new IModeSourceVerificationHandler[] { serviceHandler, displayHandler });

        var result = await dispatcher.VerifySourceAsync(Command(transitionId));

        Assert.AreEqual("MISMATCH", result.Disposition);
        Assert.AreEqual("SERVICES_NOT_RESTORED", result.ProductCode);
        Assert.AreEqual(1, serviceHandler.Calls);
        Assert.AreEqual(0, displayHandler.Calls);
    }

    private static ModeSourceVerificationCommand Command(Guid transitionId) => new(
        transitionId,
        Guid.NewGuid(),
        7,
        Guid.NewGuid(),
        Guid.NewGuid(),
        "session:test",
        4);

    private static ModeTransitionActionPlan Plan(Guid transitionId, params PersistedModeActionRecord[] actions) => new(
        transitionId,
        1,
        new string('b', 64),
        actions.Length,
        DateTimeOffset.UtcNow,
        4,
        actions);

    private static PersistedModeActionRecord Action(Guid transitionId, int sequence, string module, string actionType) => new(
        Guid.NewGuid(),
        transitionId,
        sequence,
        module,
        actionType,
        "target",
        1,
        "{}",
        new string('a', 64),
        true,
        "RESTORE_PRE_STATE",
        "READ_BACK",
        PersistedModeActionState.RolledBack,
        null,
        null,
        "APPLIED",
        "VERIFIED",
        "ROLLED_BACK",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        5);

    private sealed class FakePlanReader(ModeTransitionActionPlan? plan) : IModeActionPlanReader
    {
        public Task<ModeTransitionActionPlan?> GetAsync(Guid transitionId, CancellationToken cancellationToken = default)
            => Task.FromResult(plan);
    }

    private sealed class FakeHandler(
        string module,
        string actionType,
        RuntimeModeRollbackStepOutcome outcome) : IModeSourceVerificationHandler
    {
        public int Calls { get; private set; }

        public bool CanHandle(PersistedModeActionRecord action) =>
            string.Equals(action.OwningModule, module, StringComparison.Ordinal) &&
            string.Equals(action.ActionType, actionType, StringComparison.Ordinal);

        public Task<RuntimeModeRollbackStepOutcome> VerifySourceAsync(
            ModeSourceVerificationCommand command,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(outcome);
        }
    }
}
