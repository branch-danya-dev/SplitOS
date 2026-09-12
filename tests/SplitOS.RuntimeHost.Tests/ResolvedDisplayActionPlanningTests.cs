using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ResolvedDisplayActionPlanningTests
{
    [TestMethod]
    public void ExtendProducesTopologyThenModeWithExactResolvedSemantics()
    {
        var topologyId = Guid.NewGuid();
        var modeId = Guid.NewGuid();
        var target = new ResolvedDisplayActionTarget(
            Selector(),
            ResolvedDisplayTopologyIntent.Extend,
            3840,
            2160,
            120000,
            1000,
            1);

        var plan = ResolvedDisplayActionPlanner.Create(target, 40, topologyId, modeId);

        Assert.AreEqual(2, plan.Actions.Count);
        Assert.AreEqual(topologyId, plan.Actions[0].ActionId);
        Assert.AreEqual(40, plan.Actions[0].SequenceNo);
        Assert.AreEqual(DisplayModeActionContract.TopologyExtendActionType, plan.Actions[0].ActionType);
        Assert.AreEqual(modeId, plan.Actions[1].ActionId);
        Assert.AreEqual(41, plan.Actions[1].SequenceNo);
        Assert.AreEqual(DisplayModeActionContract.TargetModeActionType, plan.Actions[1].ActionType);

        var topology = DisplayModeActionContract.DeserializeTopologyExtend(plan.Actions[0].DesiredStateJson);
        var mode = DisplayModeActionContract.DeserializeTargetMode(plan.Actions[1].DesiredStateJson);
        Assert.AreEqual("DISPLAY\\TV\\0", topology.Selector.PnpDeviceInstanceId);
        Assert.AreEqual("DISPLAY\\TV\\0", mode.Selector.PnpDeviceInstanceId);
        Assert.AreEqual(3840u, mode.Width);
        Assert.AreEqual(2160u, mode.Height);
        Assert.AreEqual(120000u, mode.RefreshNumerator);
        Assert.AreEqual(1000u, mode.RefreshDenominator);
        Assert.IsFalse(plan.Actions[0].DesiredStateJson.Contains("targetId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(plan.Actions[1].DesiredStateJson.Contains("generation", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void PreserveActiveTopologyProducesOnlyTargetModeAction()
    {
        var modeId = Guid.NewGuid();
        var target = new ResolvedDisplayActionTarget(
            Selector(),
            ResolvedDisplayTopologyIntent.PreserveActiveTopology,
            2560,
            1440,
            60000,
            1001,
            1);

        var plan = ResolvedDisplayActionPlanner.Create(target, 70, modeActionId: modeId);

        Assert.AreEqual(1, plan.Actions.Count);
        Assert.IsNull(plan.TopologyAction);
        Assert.AreEqual(modeId, plan.ModeAction.ActionId);
        Assert.AreEqual(70, plan.ModeAction.SequenceNo);
        var mode = DisplayModeActionContract.DeserializeTargetMode(plan.ModeAction.DesiredStateJson);
        Assert.AreEqual(60000u, mode.RefreshNumerator);
        Assert.AreEqual(1001u, mode.RefreshDenominator);
    }

    [TestMethod]
    public void PlannerPreservesMandatorySeverityOnEveryGeneratedAction()
    {
        var plan = ResolvedDisplayActionPlanner.Create(
            new ResolvedDisplayActionTarget(
                Selector(),
                ResolvedDisplayTopologyIntent.Extend,
                1920,
                1080,
                60,
                1,
                1,
                Mandatory: false),
            1,
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.IsTrue(plan.Actions.All(static action => !action.Mandatory));
    }

    [TestMethod]
    public void PlannerRejectsInvalidResolvedTargetBeforeCreatingAnyAction()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => ResolvedDisplayActionPlanner.Create(
            new ResolvedDisplayActionTarget(Selector(), ResolvedDisplayTopologyIntent.Extend, 0, 1080, 60, 1, 1),
            1));
        AssertThrows<ArgumentOutOfRangeException>(() => ResolvedDisplayActionPlanner.Create(
            new ResolvedDisplayActionTarget(Selector(), ResolvedDisplayTopologyIntent.PreserveActiveTopology, 1920, 1080, 60, 0, 1),
            1));
        AssertThrows<ArgumentException>(() => ResolvedDisplayActionPlanner.Create(
            new ResolvedDisplayActionTarget(Selector(), ResolvedDisplayTopologyIntent.Extend, 1920, 1080, 60, 1, 1),
            1,
            Guid.Empty,
            Guid.NewGuid()));
    }

    [TestMethod]
    public void PlannerRejectsOperationLocalOnlySelectorEvidence()
    {
        var selector = new PersistentDisplaySelector(
            MonitorDevicePath: null,
            EdidManufactureId: null,
            EdidProductCodeId: null,
            ConnectorInstance: null,
            OutputTechnology: null,
            AdapterLuidHint: 123,
            FriendlyMonitorName: null,
            AllowWeakFallback: false,
            PnpDeviceInstanceId: null);

        AssertThrows<ArgumentException>(() => ResolvedDisplayActionPlanner.Create(
            new ResolvedDisplayActionTarget(selector, ResolvedDisplayTopologyIntent.PreserveActiveTopology, 1920, 1080, 60, 1, 1),
            1));
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

    private static T AssertThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(T).Name}, got {exception.GetType().Name}: {exception.Message}");
            throw;
        }

        Assert.Fail($"Expected {typeof(T).Name}.");
        throw new InvalidOperationException();
    }
}
