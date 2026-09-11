using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class RuntimeModeSourceAuthorityTests
{
    [TestMethod]
    public void WindowsLogonKeySurvivesProcessRestartButChangesForAnotherLogon()
    {
        var original = WindowsControlSessionIdentity.FormatKey("S-1-5-21-1-2-3-1001", 0, 123, 1000, 1);
        Assert.AreEqual(original, WindowsControlSessionIdentity.FormatKey("S-1-5-21-1-2-3-1001", 0, 123, 1000, 1));
        Assert.AreNotEqual(original, WindowsControlSessionIdentity.FormatKey("S-1-5-21-1-2-3-1001", 0, 124, 1000, 1));
        Assert.AreNotEqual(original, WindowsControlSessionIdentity.FormatKey("S-1-5-21-1-2-3-1001", 0, 123, 2000, 1));
        Assert.AreNotEqual(original, WindowsControlSessionIdentity.FormatKey("S-1-5-21-1-2-3-1002", 0, 123, 1000, 1));
    }

    [TestMethod]
    [DataRow("WORK")]
    [DataRow("GAME")]
    public async Task ManagedSourceReevaluatesAccessEveryTime(string mode)
    {
        var fixture = Create(mode);
        Assert.IsTrue((await fixture.Authority.EvaluateAsync(fixture.Transition)).MayRestoreSource);
        fixture.Access.Enabled = false;
        var denied = await fixture.Authority.EvaluateAsync(fixture.Transition);
        Assert.IsFalse(denied.MayRestoreSource);
        Assert.AreEqual("MODE_SOURCE_AUTHORITY_BASE_CONVERGENCE_REQUIRED", denied.ProductCode);
        Assert.AreEqual(2, fixture.Access.Calls);
    }

    [TestMethod]
    public async Task NoneSourceDoesNotRequireManagedEntitlement()
    {
        var fixture = Create("NONE");
        fixture.Access.Enabled = false;
        Assert.IsTrue((await fixture.Authority.EvaluateAsync(fixture.Transition)).MayRestoreSource);
        Assert.AreEqual(0, fixture.Access.Calls);
    }

    [TestMethod]
    public async Task AnotherLogonCannotReuseSourceAuthority()
    {
        var fixture = Create("WORK");
        fixture.Session.Key = "another-logon";
        Assert.IsFalse((await fixture.Authority.EvaluateAsync(fixture.Transition)).MayRestoreSource);
        Assert.AreEqual(0, fixture.Access.Calls);
    }

    [TestMethod]
    public async Task CanonicalRevisionAndPolicyIdentityMustMatch()
    {
        var fixture = Create("WORK");
        fixture.Machine.Value = fixture.Machine.Value with { Revision = 2 };
        Assert.IsFalse((await fixture.Authority.EvaluateAsync(fixture.Transition)).MayRestoreSource);
        fixture.Machine.Value = fixture.Machine.Value with { Revision = 1, PolicyIdentity = null };
        Assert.IsFalse((await fixture.Authority.EvaluateAsync(fixture.Transition)).MayRestoreSource);
        Assert.AreEqual(0, fixture.Access.Calls);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ContextChangeDuringAccessEvaluationFailsClosed(bool changeSession)
    {
        var fixture = Create("GAME");
        fixture.Access.OnEvaluate = () =>
        {
            if (changeSession) fixture.Session.Key = "new-session";
            else fixture.Machine.Value = fixture.Machine.Value with { Revision = 2 };
        };
        Assert.IsFalse((await fixture.Authority.EvaluateAsync(fixture.Transition)).MayRestoreSource);
    }

    [TestMethod]
    public async Task DurableTargetCommitNeverAuthorizesSourceRestoration()
    {
        var fixture = Create("WORK");
        Assert.IsFalse((await fixture.Authority.EvaluateAsync(fixture.Transition with { CommitDurable = true })).MayRestoreSource);
        Assert.AreEqual(0, fixture.Access.Calls);
    }

    private static Fixture Create(string source)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new Session();
        var access = new Access();
        var machine = new Machine(new(source, now, Guid.NewGuid().ToString(), null, 1, now,
            session.Key, Guid.NewGuid(), new("catalog", 1, "development", new string('a', 64)),
            source == "WORK" ? PersistedModePolicyTarget.Work : PersistedModePolicyTarget.Game, new string('b', 64)));
        var transition = new ModeTransitionRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PersistedModeOperationKind.Switch,
            source, "GAME", 1, session.Key, Guid.NewGuid(), 1, PersistedModeTransitionState.RollingBack,
            PersistedModeTransitionStage.RollbackStarted, now, now, false, false, null, null, 1);
        return new(machine, session, access, transition, new(machine, session, access));
    }
    private sealed record Fixture(Machine Machine, Session Session, Access Access, ModeTransitionRecord Transition, RuntimeModeSourceAuthority Authority);
    private sealed class Session : IControlSessionIdentity
    {
        public string Key { get; set; } = "session";
        public string GetCurrentKey() => Key;
    }
    private sealed class Access : ICurrentModeAccess
    {
        public bool Enabled { get; set; } = true;
        public int Calls { get; private set; }
        public Action? OnEvaluate { get; set; }
        public ValueTask<RuntimeAccessEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            OnEvaluate?.Invoke();
            return ValueTask.FromResult(new RuntimeAccessEvaluation(Enabled ? "ENABLED" : "DISABLED", "TEST", 1));
        }
    }
    private sealed class Machine(OperationalModeRecord value) : IMachineStateStore
    {
        public OperationalModeRecord Value { get; set; } = value;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OperationalModeRecord> GetOperationalModeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
    }
}
