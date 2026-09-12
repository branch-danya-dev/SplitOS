using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ModePreparedActionValidationTests
{
    [TestMethod]
    public void BuiltInRegistryAcceptsCanonicalManagedServiceAction()
    {
        var entries = new[] { new ManagedServicePolicyEntry("W32TIME", "RUNNING") };
        var json = ManagedServicePolicyActionContract.SerializeDesiredState(entries);
        var action = new PersistedModeActionDefinition(
            Guid.NewGuid(),
            10,
            ManagedServicePolicyActionContract.OwningModule,
            ManagedServicePolicyActionContract.ActionType,
            ManagedServicePolicyActionContract.TargetRef,
            ManagedServicePolicyActionContract.DesiredSchemaVersion,
            json,
            ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries),
            true,
            "restore_pre_state",
            "service.actual-state");

        var outcome = ModePreparedActionValidation.ValidateBuiltIn(new[] { action });

        Assert.IsTrue(outcome.IsValid, outcome.Detail);
        Assert.AreEqual("MODE_PREPARED_ACTIONS_VALID", outcome.ProductCode);
    }

    [TestMethod]
    public void BuiltInRegistryAcceptsCanonicalDisplayTopologyAndModeActions()
    {
        var selector = Selector();
        var topology = DisplayModeActionContract.CreateTopologyExtendDefinition(
            Guid.NewGuid(), 20, selector);
        var mode = DisplayModeActionContract.CreateTargetModeDefinition(
            Guid.NewGuid(),
            21,
            new DisplayTargetModeDesiredState(selector, 3840, 2160, 60000, 1001, 1));

        var outcome = ModePreparedActionValidation.ValidateBuiltIn(new[] { topology, mode });

        Assert.IsTrue(outcome.IsValid, outcome.Detail);
        Assert.AreEqual("MODE_PREPARED_ACTIONS_VALID", outcome.ProductCode);
    }

    [TestMethod]
    public void TamperedDisplayDigestFailsClosed()
    {
        var action = DisplayModeActionContract.CreateTargetModeDefinition(
            Guid.NewGuid(),
            30,
            new DisplayTargetModeDesiredState(Selector(), 2560, 1440, 120, 1, 1)) with
        {
            DesiredStateDigest = new string('0', 64)
        };

        var outcome = ModePreparedActionValidation.ValidateBuiltIn(new[] { action });

        Assert.IsFalse(outcome.IsValid);
        Assert.AreEqual("MODE_PREPARED_DISPLAY_DIGEST_MISMATCH", outcome.ProductCode);
    }

    [TestMethod]
    public void UnknownActionDomainFailsClosed()
    {
        var action = DisplayModeActionContract.CreateTargetModeDefinition(
            Guid.NewGuid(),
            40,
            new DisplayTargetModeDesiredState(Selector(), 1920, 1080, 60, 1, 1)) with
        {
            OwningModule = "unknown-domain",
            ActionType = "unknown.action"
        };

        var outcome = ModePreparedActionValidation.ValidateBuiltIn(new[] { action });

        Assert.IsFalse(outcome.IsValid);
        Assert.AreEqual("MODE_PREPARED_ACTION_VALIDATOR_UNAVAILABLE", outcome.ProductCode);
    }

    [TestMethod]
    public void DuplicateDomainValidatorsFailClosedAsAmbiguous()
    {
        var action = DisplayModeActionContract.CreateTargetModeDefinition(
            Guid.NewGuid(),
            50,
            new DisplayTargetModeDesiredState(Selector(), 1920, 1080, 60, 1, 1));
        var dispatcher = new ModePreparedActionValidationDispatcher(
            new IModePreparedActionValidator[]
            {
                new DisplayPreparedActionValidator(),
                new DisplayPreparedActionValidator()
            });

        var outcome = dispatcher.Validate(action);

        Assert.IsFalse(outcome.IsValid);
        Assert.AreEqual("MODE_PREPARED_ACTION_VALIDATOR_AMBIGUOUS", outcome.ProductCode);
    }

    [TestMethod]
    public void DuplicateActionIdsAndNonIncreasingSequenceFailClosed()
    {
        var first = DisplayModeActionContract.CreateTargetModeDefinition(
            Guid.NewGuid(),
            60,
            new DisplayTargetModeDesiredState(Selector(), 1920, 1080, 60, 1, 1));
        var duplicateId = DisplayModeActionContract.CreateTargetModeDefinition(
            first.ActionId,
            61,
            new DisplayTargetModeDesiredState(Selector(), 1920, 1080, 120, 1, 1));

        var duplicateOutcome = ModePreparedActionValidation.ValidateBuiltIn(new[] { first, duplicateId });
        Assert.IsFalse(duplicateOutcome.IsValid);
        Assert.AreEqual("MODE_PREPARED_ACTION_ID_DUPLICATE", duplicateOutcome.ProductCode);

        var second = DisplayModeActionContract.CreateTargetModeDefinition(
            Guid.NewGuid(),
            60,
            new DisplayTargetModeDesiredState(Selector(), 1920, 1080, 120, 1, 1));
        var sequenceOutcome = ModePreparedActionValidation.ValidateBuiltIn(new[] { first, second });
        Assert.IsFalse(sequenceOutcome.IsValid);
        Assert.AreEqual("MODE_PREPARED_ACTION_SEQUENCE_INVALID", sequenceOutcome.ProductCode);
    }

    private static PersistentDisplaySelector Selector()
        => new(
            MonitorDevicePath: "\\\\?\\DISPLAY#TV#0",
            EdidManufactureId: 1234,
            EdidProductCodeId: 5678,
            ConnectorInstance: 2,
            OutputTechnology: 5,
            AdapterLuidHint: 999,
            FriendlyMonitorName: "Living Room TV",
            AllowWeakFallback: false,
            PnpDeviceInstanceId: "DISPLAY\\TV\\0");
}
