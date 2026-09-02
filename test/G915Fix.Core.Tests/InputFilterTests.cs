using G915Fix.Core.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.Core.Tests;

[TestClass]
public sealed class InputFilterTests
{
    [TestMethod]
    public void HidKeyboardTokenResolver_ResolvesNamesAndModifierGroups()
    {
        var resolver = new HidKeyboardTokenResolver();

        CollectionAssert.AreEqual(
            new[] { HidKeyboardUsage.Enter },
            resolver.Resolve("VK_RETURN").ToArray());
        CollectionAssert.AreEqual(
            new[] { HidKeyboardUsage.LeftControl, HidKeyboardUsage.RightControl },
            resolver.Resolve("Ctrl").ToArray());
        Assert.AreEqual(0, resolver.Resolve("8").Count);
    }

    [TestMethod]
    public void KeyboardDebounceFilter_BlockRepressSuppressesBounceAndItsRelease()
    {
        using var filter = new KeyboardDebounceFilter(
            new KeyboardDebounceOptions { MinimumRepeatInterval = TimeSpan.FromMilliseconds(28) },
            new RecordingInjector(),
            timestampFrequency: 1000);

        Assert.IsFalse(filter.ShouldSuppress(new KeyboardInputEvent(HidKeyboardUsage.A, KeyboardInputKind.KeyDown, Timestamp: 100)));
        Assert.IsFalse(filter.ShouldSuppress(new KeyboardInputEvent(HidKeyboardUsage.A, KeyboardInputKind.KeyUp, Timestamp: 110)));
        Assert.IsTrue(filter.ShouldSuppress(new KeyboardInputEvent(HidKeyboardUsage.A, KeyboardInputKind.KeyDown, Timestamp: 120)));
        Assert.IsTrue(filter.ShouldSuppress(new KeyboardInputEvent(HidKeyboardUsage.A, KeyboardInputKind.KeyUp, Timestamp: 121)));
    }

    [TestMethod]
    public void KeyboardDebounceFilter_BlockReleaseReinjectsOnlyUncontestedRelease()
    {
        var injector = new RecordingInjector();
        var schedulerFactory = new RecordingSchedulerFactory();
        using var filter = new KeyboardDebounceFilter(
            new KeyboardDebounceOptions
            {
                Mode = KeyboardDebounceMode.BlockRelease,
                MinimumRepeatInterval = TimeSpan.FromMilliseconds(28)
            },
            injector,
            schedulerFactory,
            timestampFrequency: 1000);

        Assert.IsFalse(filter.ShouldSuppress(new KeyboardInputEvent(HidKeyboardUsage.A, KeyboardInputKind.KeyDown, Timestamp: 100)));
        Assert.IsTrue(filter.ShouldSuppress(new KeyboardInputEvent(HidKeyboardUsage.A, KeyboardInputKind.KeyUp, Timestamp: 110)));
        schedulerFactory.Scheduler.Fire();
        CollectionAssert.AreEqual(new[] { HidKeyboardUsage.A }, injector.Released);

        Assert.IsFalse(filter.ShouldSuppress(new KeyboardInputEvent(HidKeyboardUsage.A, KeyboardInputKind.KeyDown, Timestamp: 200)));
        Assert.IsTrue(filter.ShouldSuppress(new KeyboardInputEvent(HidKeyboardUsage.A, KeyboardInputKind.KeyUp, Timestamp: 210)));
        Assert.IsTrue(filter.ShouldSuppress(new KeyboardInputEvent(HidKeyboardUsage.A, KeyboardInputKind.KeyDown, Timestamp: 220)));
        schedulerFactory.Scheduler.Fire();
        CollectionAssert.AreEqual(new[] { HidKeyboardUsage.A }, injector.Released);
    }

    [TestMethod]
    public void MouseDebounceFilter_SuppressesRapidSecondPress()
    {
        var filter = new MouseDebounceFilter(
            new MouseDebounceOptions { MinimumRepeatInterval = TimeSpan.FromMilliseconds(50) },
            timestampFrequency: 1000);

        Assert.IsFalse(filter.ShouldSuppress(new MouseInputEvent(MouseButton.Left, MouseInputKind.ButtonDown, 100)));
        Assert.IsFalse(filter.ShouldSuppress(new MouseInputEvent(MouseButton.Left, MouseInputKind.ButtonUp, 110)));
        Assert.IsTrue(filter.ShouldSuppress(new MouseInputEvent(MouseButton.Left, MouseInputKind.ButtonDown, 120)));
    }

    private sealed class RecordingInjector : IKeyboardInputInjector
    {
        public List<HidKeyboardUsage> Released { get; } = [];

        public void InjectKeyUp(HidKeyboardUsage key) => Released.Add(key);
    }

    private sealed class RecordingSchedulerFactory : IReleaseSchedulerFactory
    {
        public RecordingScheduler Scheduler { get; } = new();

        public IReleaseScheduler Create() => Scheduler;
    }

    private sealed class RecordingScheduler : IReleaseScheduler
    {
        private Action<HidKeyboardUsage>? _callback;
        private HidKeyboardUsage _key;

        public void Schedule(HidKeyboardUsage key, TimeSpan delay, Action<HidKeyboardUsage> callback)
        {
            _key = key;
            _callback = callback;
        }

        public void Cancel() => _callback = null;

        public void Fire() => _callback?.Invoke(_key);

        public void Dispose() => Cancel();
    }
}
