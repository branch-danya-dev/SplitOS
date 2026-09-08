using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class RuntimeStateRefreshSignalTests
{
    [TestMethod]
    public async Task ExplicitRefreshSignalWakesWaitingCoordinator()
    {
        var signal = new RuntimeStateRefreshSignal();
        var wait = signal.WaitAsync(TimeSpan.FromSeconds(5));

        signal.RequestRefresh();
        await wait.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(wait.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task RefreshRequestsAreCoalescedWithoutBlockingWriters()
    {
        var signal = new RuntimeStateRefreshSignal();

        for (var index = 0; index < 100; index++)
        {
            signal.RequestRefresh();
        }

        await signal.WaitAsync(TimeSpan.FromSeconds(1));
        var secondWait = signal.WaitAsync(TimeSpan.FromMilliseconds(30));
        await secondWait.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(secondWait.IsCompletedSuccessfully);
    }
}
