using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ModeActionRollbackDispatchTests
{
    [TestMethod]
    public async Task DispatchesExactlyOneRollbackHandler()
    {
        var action = Action();
        var handler = new FakeHandler(true, new RuntimeModeRollbackStepOutcome("VERIFIED", "HANDLED"));
        var dispatcher = new ModeActionRollbackDispatcher(new[] { handler });
        var command = Command(action);

        var result = await dispatcher.RollbackAsync(command, action);

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("HANDLED", result.ProductCode);
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(command, handler.LastCommand);
        Assert.AreEqual(action, handler.LastAction);
    }

    [TestMethod]
    public async Task MissingRollbackHandlerRequiresReconciliationWithoutMutation()
    {
        var action = Action();
        var handler = new FakeHandler(false, new RuntimeModeRollbackStepOutcome("VERIFIED", "MUST_NOT_RUN"));
        var dispatcher = new ModeActionRollbackDispatcher(new[] { handler });

        var result = await dispatcher.RollbackAsync(Command(action), action);

        Assert.AreEqual("RECONCILIATION_REQUIRED", result.Disposition);
        Assert.AreEqual("MODE_ACTION_ROLLBACK_HANDLER_UNAVAILABLE", result.ProductCode);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task AmbiguousRollbackHandlersRequireReconciliationWithoutMutation()
    {
        var action = Action();
        var first = new FakeHandler(true, new RuntimeModeRollbackStepOutcome("VERIFIED", "FIRST"));
        var second = new FakeHandler(true, new RuntimeModeRollbackStepOutcome("VERIFIED", "SECOND"));
        var dispatcher = new ModeActionRollbackDispatcher(new[] { first, second });

        var result = await dispatcher.RollbackAsync(Command(action), action);

        Assert.AreEqual("RECONCILIATION_REQUIRED", result.Disposition);
        Assert.AreEqual("MODE_ACTION_ROLLBACK_HANDLER_AMBIGUOUS", result.ProductCode);
        Assert.AreEqual(0, first.Calls);
        Assert.AreEqual(0, second.Calls);
    }

    [TestMethod]
    public async Task RejectsStaleOrNonRollingBackActionBeforeHandler()
    {
        var action = Action();
        var handler = new FakeHandler(true, new RuntimeModeRollbackStepOutcome("VERIFIED", "MUST_NOT_RUN"));
        var dispatcher = new ModeActionRollbackDispatcher(new[] { handler });

        await AssertThrowsAsync<InvalidDataException>(() => dispatcher.RollbackAsync(
            Command(action) with { ActionRevision = checked(action.Revision + 1) }, action));
        await AssertThrowsAsync<InvalidDataException>(() => dispatcher.RollbackAsync(
            Command(action), action with { State = PersistedModeActionState.Applied }));

        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task RejectsMismatchedDurableIdentityBeforeHandler()
    {
        var action = Action();
        var handler = new FakeHandler(true, new RuntimeModeRollbackStepOutcome("VERIFIED", "MUST_NOT_RUN"));
        var dispatcher = new ModeActionRollbackDispatcher(new[] { handler });

        await AssertThrowsAsync<InvalidDataException>(() => dispatcher.RollbackAsync(
            Command(action) with { TransitionId = Guid.NewGuid() }, action));
        await AssertThrowsAsync<InvalidDataException>(() => dispatcher.RollbackAsync(
            Command(action) with { ActionId = Guid.NewGuid() }, action));

        Assert.AreEqual(0, handler.Calls);
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private static ModeActionRollbackCommand Command(PersistedModeActionRecord action)
        => new(
            action.TransitionId,
            action.ActionId,
            action.Revision,
            Guid.NewGuid(),
            11,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session:test");

    private static PersistedModeActionRecord Action()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "test-module",
            "test-action",
            "test-target",
            1,
            "{}",
            new string('a', 64),
            true,
            "RESTORE_PRE_STATE",
            "READ_BACK",
            PersistedModeActionState.RollingBack,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            5);

    private sealed class FakeHandler(bool canHandle, RuntimeModeRollbackStepOutcome outcome) : IModeActionRollbackHandler
    {
        public int Calls { get; private set; }
        public ModeActionRollbackCommand? LastCommand { get; private set; }
        public PersistedModeActionRecord? LastAction { get; private set; }

        public bool CanHandle(PersistedModeActionRecord action) => canHandle;

        public Task<RuntimeModeRollbackStepOutcome> RollbackAsync(
            ModeActionRollbackCommand command,
            PersistedModeActionRecord action,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastCommand = command;
            LastAction = action;
            return Task.FromResult(outcome);
        }
    }
}
