from pathlib import Path


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


p = "src/SplitOS.Persistence.Machine/ModeTransitionStore.cs"
s = read(p)
anchor = '''        var lifecycleError = ValidateAdvance(current, nextState, nextStage, mandatoryVerified, terminalOutcome);\n        if (lifecycleError is not null)\n        {\n            return new ModeTransitionAdvanceOutcome(\n                ModeTransitionAdvanceDisposition.InvalidLifecycle,\n                current,\n                "MODE_TRANSITION_INVALID_LIFECYCLE",\n                current.Revision,\n                lifecycleError);\n        }\n\n'''
replacement = anchor + '''        if (nextStage == PersistedModeTransitionStage.ActionPlanReady &&\n            !await HasDurablePolicyBindingAsync(\n                connection,\n                transaction,\n                transitionId,\n                cancellationToken).ConfigureAwait(false))\n        {\n            return new ModeTransitionAdvanceOutcome(\n                ModeTransitionAdvanceDisposition.InvalidLifecycle,\n                current,\n                "MODE_TRANSITION_INVALID_LIFECYCLE",\n                current.Revision,\n                "Durable resolved policy binding is required before ACTION_PLAN_READY.");\n        }\n\n'''
s = replace_once(s, anchor, replacement, "action-plan policy guard")

helper_anchor = '''    private static async Task<(string Mode, int Revision)> ReadCanonicalModeAsync(\n'''
helper = '''    private static async Task<bool> HasDurablePolicyBindingAsync(\n        SqliteConnection connection,\n        SqliteTransaction transaction,\n        Guid transitionId,\n        CancellationToken cancellationToken)\n    {\n        var command = connection.CreateCommand();\n        command.Transaction = transaction;\n        command.CommandText = """\n            SELECT COUNT(*)\n            FROM mode_transition_policy_binding\n            WHERE transition_id = $transition;\n            """;\n        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));\n        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;\n    }\n\n'''
s = replace_once(s, helper_anchor, helper + helper_anchor, "policy binding helper")
write(p, s)

p = "tests/SplitOS.Persistence.Tests/ModeTransitionPolicyStoreTests.cs"
s = read(p)
old = '''        await Assert.ThrowsExactlyAsync<SqliteException>(() => context.Transitions.AdvanceAsync(\n            context.TransitionId,\n            context.TransitionRevision,\n            context.Lease.LeaseId!.Value,\n            context.Lease.FenceToken,\n            context.OperationId,\n            PersistedModeTransitionState.Resolving,\n            PersistedModeTransitionStage.ActionPlanReady,\n            mandatoryVerified: false));\n'''
new = '''        var denied = await context.Transitions.AdvanceAsync(\n            context.TransitionId,\n            context.TransitionRevision,\n            context.Lease.LeaseId!.Value,\n            context.Lease.FenceToken,\n            context.OperationId,\n            PersistedModeTransitionState.Resolving,\n            PersistedModeTransitionStage.ActionPlanReady,\n            mandatoryVerified: false);\n        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, denied.Disposition);\n        StringAssert.Contains(denied.Detail, "Durable resolved policy binding");\n'''
s = replace_once(s, old, new, "typed action-plan denial test")
write(p, s)

p = "src/SplitOS.Persistence.Machine/ModeTransitionPolicyStore.cs"
s = read(p)
anchor = '''        var seen = new HashSet<string>(StringComparer.Ordinal);\n        foreach (var fallback in fallbacks ?? Array.Empty<PersistedModePolicyFallbackSelection>())\n'''
replacement = '''        if (fallbacks is { Count: > 256 })\n        {\n            throw new ArgumentException("Fallback selection count exceeds the bounded policy rule maximum.", nameof(fallbacks));\n        }\n\n        var seen = new HashSet<string>(StringComparer.Ordinal);\n        foreach (var fallback in fallbacks ?? Array.Empty<PersistedModePolicyFallbackSelection>())\n'''
s = replace_once(s, anchor, replacement, "fallback bound")
write(p, s)

print("Slice 03 policy binding hardening patch applied.")
