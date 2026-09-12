using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class PnpHardwareGenerationTests
{
    [TestMethod]
    public void TrackerInvalidationIsMonotonicAndRetainsNativeAction()
    {
        var observed = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var tracker = new HardwareGenerationTracker(new FixedTimeProvider(observed));

        var first = tracker.Invalidate(7);
        var second = tracker.Invalidate(9);

        Assert.AreEqual(2L, first.Generation);
        Assert.AreEqual(7u, first.NativeAction);
        Assert.AreEqual(observed, first.ObservedUtc);
        Assert.AreEqual(3L, second.Generation);
        Assert.AreEqual(9u, second.NativeAction);
        Assert.AreEqual(3L, tracker.CurrentGeneration);
        Assert.AreEqual(second, tracker.LastChange);
    }

    [TestMethod]
    public void NativeCallbackOnlyInvalidatesGenerationAndRegistrationIsIdempotent()
    {
        var observed = new DateTimeOffset(2026, 9, 12, 9, 5, 0, TimeSpan.Zero);
        var tracker = new HardwareGenerationTracker(new FixedTimeProvider(observed));
        var fakeInterop = new FakeConfigurationManagerPnpInterop(
            new PnpNotificationRegistrationAttempt(0, new IntPtr(1234)));
        using var source = new WindowsPnpHardwareNotificationSource(
            fakeInterop,
            new PnpHardwareGenerationInvalidator(tracker));

        source.Start();
        source.Start();
        var callbackResult = fakeInterop.Raise(8);

        Assert.AreEqual(0u, callbackResult);
        Assert.IsTrue(source.IsRegistered);
        Assert.AreEqual(1, fakeInterop.RegisterCalls);
        Assert.AreEqual(2L, tracker.CurrentGeneration);
        Assert.IsNotNull(tracker.LastChange);
        Assert.AreEqual(8u, tracker.LastChange.NativeAction);
        Assert.AreEqual(observed, tracker.LastChange.ObservedUtc);

        source.Stop();
        source.Stop();

        Assert.IsFalse(source.IsRegistered);
        Assert.AreEqual(1, fakeInterop.UnregisterCalls);
        Assert.AreEqual(new IntPtr(1234), fakeInterop.LastUnregisteredHandle);
    }

    [TestMethod]
    public void RegistrationFailurePreservesConfigRetAndDoesNotPublishRegisteredState()
    {
        var fakeInterop = new FakeConfigurationManagerPnpInterop(
            new PnpNotificationRegistrationAttempt(0x0000000Du, IntPtr.Zero));
        using var source = new WindowsPnpHardwareNotificationSource(
            fakeInterop,
            new PnpHardwareGenerationInvalidator(new HardwareGenerationTracker()));

        var exception = AssertThrows<ConfigurationManagerNotificationException>(() => source.Start());

        Assert.AreEqual(0x0000000Du, exception.ConfigRet);
        Assert.IsFalse(source.IsRegistered);
    }

    [TestMethod]
    public void SuccessfulRegistrationWithoutHandleFailsClosed()
    {
        var fakeInterop = new FakeConfigurationManagerPnpInterop(
            new PnpNotificationRegistrationAttempt(0, IntPtr.Zero));
        using var source = new WindowsPnpHardwareNotificationSource(
            fakeInterop,
            new PnpHardwareGenerationInvalidator(new HardwareGenerationTracker()));

        AssertThrows<InvalidDataException>(() => source.Start());
        Assert.IsFalse(source.IsRegistered);
    }

    [TestMethod]
    public void NativeConfigurationManagerRegistrationSmoke()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("PnP notification registration smoke requires Windows.");

        var interop = new ConfigurationManagerPnpInterop();
        PnpHardwareNotificationCallback callback = (_, _, _, _, _) => 0;
        var attempt = interop.RegisterAllDeviceInstances(callback);

        try
        {
            Assert.AreEqual(0u, attempt.ConfigRet);
            Assert.AreNotEqual(IntPtr.Zero, attempt.NotificationHandle);
        }
        finally
        {
            if (attempt.ConfigRet == 0 && attempt.NotificationHandle != IntPtr.Zero)
                Assert.AreEqual(0u, interop.Unregister(attempt.NotificationHandle));

            GC.KeepAlive(callback);
        }
    }

    private sealed class FakeConfigurationManagerPnpInterop(
        PnpNotificationRegistrationAttempt registrationAttempt) : IConfigurationManagerPnpInterop
    {
        private PnpHardwareNotificationCallback? _callback;

        public int RegisterCalls { get; private set; }
        public int UnregisterCalls { get; private set; }
        public IntPtr LastUnregisteredHandle { get; private set; }

        public PnpNotificationRegistrationAttempt RegisterAllDeviceInstances(PnpHardwareNotificationCallback callback)
        {
            RegisterCalls++;
            _callback = callback;
            return registrationAttempt;
        }

        public uint Unregister(IntPtr notificationHandle)
        {
            UnregisterCalls++;
            LastUnregisteredHandle = notificationHandle;
            return 0;
        }

        public uint Raise(uint action)
            => _callback is null
                ? throw new InvalidOperationException("PnP callback has not been registered.")
                : _callback(new IntPtr(1234), IntPtr.Zero, action, IntPtr.Zero, 0);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

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
