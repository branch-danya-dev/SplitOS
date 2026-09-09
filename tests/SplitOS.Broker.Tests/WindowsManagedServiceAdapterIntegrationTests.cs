using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class WindowsManagedServiceAdapterIntegrationTests
{
    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public async Task RunningCiServiceCanStopVerifyReplayAndRestoreRunning()
    {
        var serviceName = Environment.GetEnvironmentVariable("SPLITOS_CI_MANAGED_SERVICE_NAME");
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            Assert.Inconclusive("SPLITOS_CI_MANAGED_SERVICE_NAME is not configured for this test run.");
            return;
        }

        var entry = new ManagedServiceCatalogEntry(
            "CI_MANAGED_SERVICE",
            serviceName,
            new HashSet<ManagedServiceDesiredState>
            {
                ManagedServiceDesiredState.Running,
                ManagedServiceDesiredState.Stopped
            },
            TimeSpan.FromSeconds(20),
            0);
        var adapter = new WindowsManagedServiceAdapter();

        var initial = await adapter.QueryAsync(entry);
        Assert.AreEqual(
            ManagedServiceObservedState.Running,
            initial.State,
            $"CI fixture service {serviceName} must begin RUNNING for deterministic restore semantics.");

        try
        {
            var stopped = await adapter.ApplyAsync(entry, ManagedServiceDesiredState.Stopped);
            Assert.IsTrue(stopped.Verified, $"Stop failed: {stopped.ErrorCode}/{stopped.NativeErrorCode}");
            Assert.AreEqual(ManagedServiceObservedState.Stopped, stopped.ActualState);

            var observedStopped = await adapter.QueryAsync(entry);
            Assert.AreEqual(ManagedServiceObservedState.Stopped, observedStopped.State);

            var stoppedReplay = await adapter.ApplyAsync(entry, ManagedServiceDesiredState.Stopped);
            Assert.AreEqual(ManagedServiceApplyDisposition.AlreadySatisfied, stoppedReplay.Disposition);
            Assert.IsTrue(stoppedReplay.Verified);
            Assert.IsFalse(stoppedReplay.OperationAttempted);
            Assert.AreEqual(ManagedServiceObservedState.Stopped, stoppedReplay.ActualState);

            var running = await adapter.ApplyAsync(entry, ManagedServiceDesiredState.Running);
            Assert.IsTrue(running.Verified, $"Start failed: {running.ErrorCode}/{running.NativeErrorCode}");
            Assert.AreEqual(ManagedServiceObservedState.Running, running.ActualState);

            var observedRunning = await adapter.QueryAsync(entry);
            Assert.AreEqual(ManagedServiceObservedState.Running, observedRunning.State);

            var runningReplay = await adapter.ApplyAsync(entry, ManagedServiceDesiredState.Running);
            Assert.AreEqual(ManagedServiceApplyDisposition.AlreadySatisfied, runningReplay.Disposition);
            Assert.IsTrue(runningReplay.Verified);
            Assert.IsFalse(runningReplay.OperationAttempted);
            Assert.AreEqual(ManagedServiceObservedState.Running, runningReplay.ActualState);
        }
        finally
        {
            var final = await adapter.QueryAsync(entry);
            if (final.State != ManagedServiceObservedState.Running)
            {
                _ = await adapter.ApplyAsync(entry, ManagedServiceDesiredState.Running);
            }
        }
    }
}
