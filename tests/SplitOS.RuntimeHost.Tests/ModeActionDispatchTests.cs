using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ModeActionDispatchTests
{
    [TestMethod]
    public async Task ApplyDispatchesExactlyOneMatchingDurableActionHandler()
    {
        var action = Action();
        var reader = new FakeActionReader(action);
        var handler = new FakeApplyHandler(true, new ModeActionApplyStageOutcome(true, "APPLY_HANDLED", action.Revision));
        var dispatcher = new ModeActionApplyDispatcher(reader, new[] { handler });
        var command = Command(action);

        var result = await dispatcher.ApplyAsync(command);

        Assert.IsTrue(result.IsApplied);
        Assert.AreEqual("APPLY_HANDLED", result.ProductCode);
        Assert.AreEqual(1, reader.InitializeCalls);
        Assert.AreEqual(1, reader.GetCalls);
        Assert.AreEqual(1, handler.ApplyCalls);
        Assert.AreEqual(action, handler.LastAction);
        Assert.AreEqual(command, handler.LastCommand);
    }

    [TestMethod]
    public async Task ApplyFailsClosedWhenNoHandlerMatches()
    {
        var action = Action();
        var handler = new FakeApplyHandler(false, new ModeActionApplyStageOutcome(true, "MUST_NOT_RUN"));
        var dispatcher = new ModeActionApplyDispatcher(new FakeActionReader(action), new[] { handler });

        var result = await dispatcher.ApplyAsync(Command(action));

        Assert.IsFalse(result.IsApplied);
        Assert.AreEqual("MODE_ACTION_APPLY_HANDLER_UNAVAILABLE", result.ProductCode);
        Assert.AreEqual(0, handler.ApplyCalls);
    }

    [TestMethod]
    public async Task ApplyFailsClosedWhenMoreThanOneHandlerMatches()
    {
        var action = Action();
        var first = new FakeApplyHandler(true, new ModeActionApplyStageOutcome(true, "FIRST"));
        var second = new FakeApplyHandler(true, new ModeActionApplyStageOutcome(true, "SECOND"));
        var dispatcher = new ModeActionApplyDispatcher(new FakeActionReader(action), new[] { first, second });

        var result = await dispatcher.ApplyAsync(Command(action));

        Assert.IsFalse(result.IsApplied);
        Assert.AreEqual("MODE_ACTION_APPLY_HANDLER_AMBIGUOUS", result.ProductCode);
        Assert.AreEqual(0, first.ApplyCalls);
        Assert.AreEqual(0, second.ApplyCalls);
    }

    [TestMethod]
    public async Task ApplyRejectsDurableActionFromDifferentTransitionBeforeHandler()
    {
        var action = Action();
        var handler = new FakeApplyHandler(true, new ModeActionApplyStageOutcome(true, "MUST_NOT_RUN"));
        var dispatcher = new ModeActionApplyDispatcher(new FakeActionReader(action), new[] { handler });
        var command = Command(action) with { TransitionId = Guid.NewGuid() };

        var result = await dispatcher.ApplyAsync(command);

        Assert.IsFalse(result.IsApplied);
        Assert.AreEqual("MODE_ACTION_TRANSITION_MISMATCH", result.ProductCode);
        Assert.AreEqual(0, handler.ApplyCalls);
    }

    [TestMethod]
    public async Task ApplyRejectsStaleActionRevisionBeforeHandler()
    {
        var action = Action();
        var handler = new FakeApplyHandler(true, new ModeActionApplyStageOutcome(true, "MUST_NOT_RUN"));
        var dispatcher = new ModeActionApplyDispatcher(new FakeActionReader(action), new[] { handler });
        var command = Command(action) with { ExpectedActionRevision = checked(action.Revision + 1) };

        var result = await dispatcher.ApplyAsync(command);

        Assert.IsFalse(result.IsApplied);
        Assert.AreEqual("MODE_ACTION_REVISION_MISMATCH", result.ProductCode);
        Assert.AreEqual(0, handler.ApplyCalls);
    }

    [TestMethod]
    public async Task VerifyDispatchesExactlyOneMatchingDurableActionHandler()
    {
        var action = Action(state: PersistedModeActionState.Applied);
        var reader = new FakeActionReader(action);
        var handler = new FakeVerifyHandler(true, new ModeActionVerifyStageOutcome(true, "VERIFY_HANDLED", action.Revision));
        var dispatcher = new ModeActionVerifyDispatcher(reader, new[] { handler });
        var command = Command(action);

        var result = await dispatcher.VerifyAsync(command);

        Assert.IsTrue(result.IsVerified);
        Assert.AreEqual("VERIFY_HANDLED", result.ProductCode);
        Assert.AreEqual(1, handler.VerifyCalls);
        Assert.AreEqual(action, handler.LastAction);
        Assert.AreEqual(command, handler.LastCommand);
    }

    [TestMethod]
    public async Task VerifyFailsClosedForZeroOrMultipleMatchingHandlers()
    {
        var action = Action(state: PersistedModeActionState.Applied);
        var noMatch = new FakeVerifyHandler(false, new ModeActionVerifyStageOutcome(true, "MUST_NOT_RUN"));
        var unavailable = new ModeActionVerifyDispatcher(new FakeActionReader(action), new[] { noMatch });

        var unavailableResult = await unavailable.VerifyAsync(Command(action));

        Assert.IsFalse(unavailableResult.IsVerified);
        Assert.AreEqual("MODE_ACTION_VERIFY_HANDLER_UNAVAILABLE", unavailableResult.ProductCode);
        Assert.AreEqual(0, noMatch.VerifyCalls);

        var first = new FakeVerifyHandler(true, new ModeActionVerifyStageOutcome(true, "FIRST"));
        var second = new FakeVerifyHandler(true, new ModeActionVerifyStageOutcome(true, "SECOND"));
        var ambiguous = new ModeActionVerifyDispatcher(new FakeActionReader(action), new[] { first, second });

        var ambiguousResult = await ambiguous.VerifyAsync(Command(action));

        Assert.IsFalse(ambiguousResult.IsVerified);
        Assert.AreEqual("MODE_ACTION_VERIFY_HANDLER_AMBIGUOUS", ambiguousResult.ProductCode);
        Assert.AreEqual(0, first.VerifyCalls);
        Assert.AreEqual(0, second.VerifyCalls);
    }

    [TestMethod]
    public async Task ApplyAndVerifyRejectInvalidCommandsBeforeReadingDurableState()
    {
        var reader = new FakeActionReader(Action());
        var invalid = Command(reader.Action!) with { ControlSessionKey = "" };
        var apply = new ModeActionApplyDispatcher(reader, Array.Empty<IModeActionApplyHandler>());
        var verify = new ModeActionVerifyDispatcher(reader, Array.Empty<IModeActionVerifyHandler>());

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => apply.ApplyAsync(invalid));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => verify.VerifyAsync(invalid));

        Assert.AreEqual(0, reader.InitializeCalls);
        Assert.AreEqual(0, reader.GetCalls);
    }

    private static ModeActionExecutionCommand Command(PersistedModeActionRecord action)
        => new(
            action.TransitionId,
            action.ActionId,
            action.Revision,
            Guid.NewGuid(),
            7,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session:test");

    private static PersistedModeActionRecord Action(PersistedModeActionState state = PersistedModeActionState.Planned)
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
            state,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            1);

    private sealed class FakeActionReader(PersistedModeActionRecord? action) : IModeActionRecordReader
    {
        public PersistedModeActionRecord? Action { get; } = action;
        public int InitializeCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            return Task.CompletedTask;
        }

        public Task<PersistedModeActionRecord?> GetAsync(Guid actionId, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(Action);
        }
    }

    private sealed class FakeApplyHandler(bool canHandle, ModeActionApplyStageOutcome outcome) : IModeActionApplyHandler
    {
        public int ApplyCalls { get; private set; }
        public ModeActionExecutionCommand? LastCommand { get; private set; }
        public PersistedModeActionRecord? LastAction { get; private set; }

        public bool CanHandle(PersistedModeActionRecord action) => canHandle;

        public Task<ModeActionApplyStageOutcome> ApplyAsync(
            ModeActionExecutionCommand command,
            PersistedModeActionRecord action,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            LastCommand = command;
            LastAction = action;
            return Task.FromResult(outcome);
        }
    }

    private sealed class FakeVerifyHandler(bool canHandle, ModeActionVerifyStageOutcome outcome) : IModeActionVerifyHandler
    {
        public int VerifyCalls { get; private set; }
        public ModeActionExecutionCommand? LastCommand { get; private set; }
        public PersistedModeActionRecord? LastAction { get; private set; }

        public bool CanHandle(PersistedModeActionRecord action) => canHandle;

        public Task<ModeActionVerifyStageOutcome> VerifyAsync(
            ModeActionExecutionCommand command,
            PersistedModeActionRecord action,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            LastCommand = command;
            LastAction = action;
            return Task.FromResult(outcome);
        }
    }
}
