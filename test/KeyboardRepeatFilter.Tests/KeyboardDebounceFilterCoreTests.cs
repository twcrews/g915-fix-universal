using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeyboardRepeatFilter.Tests
{
    [TestClass]
    public class KeyboardDebounceFilterCoreTests
    {
        private const int VkA = 65;
        private const int VkB = 66;
        private const int VkCapsLock = 0x14;

        [TestMethod]
        public void BlockRepress_FiltersDuplicateKeyDownWithinThreshold()
        {
            var clock = new ManualClock(1000);
            var filtered = new List<string>();
            using (var core = CreateCore(clock, logFiltered: (vk, action) => filtered.Add(vk + ":" + action)))
            {
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1010;
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyUp));
                clock.Ticks = 1020;

                Assert.IsTrue(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
            }

            CollectionAssert.AreEqual(new[] { "65:filtered" }, filtered);
        }

        [TestMethod]
        public void BlockRepress_FiltersKeyUpThatMatchesSuppressedBounceKeyDown()
        {
            var clock = new ManualClock(1000);
            using (var core = CreateCore(clock))
            {
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1010;
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyUp));
                clock.Ticks = 1020;
                Assert.IsTrue(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1021;

                Assert.IsTrue(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyUp));
            }
        }

        [TestMethod]
        public void BlockRelease_DefersKeyUpUntilTimerFires()
        {
            var clock = new ManualClock(1000);
            var timers = new List<ManualReleaseTimer>();
            var injected = new List<string>();
            using (var core = CreateCore(
                clock,
                filterMode: "BlockRelease",
                createTimer: (vk, callback) =>
                {
                    var timer = new ManualReleaseTimer(vk, callback);
                    timers.Add(timer);
                    return timer;
                },
                injectKeyUp: (vk, extended, scanCode) => injected.Add(vk + ":" + extended + ":" + scanCode)))
            {
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1010;

                Assert.IsTrue(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyUp,
                    KeyboardDebounceFilterCore.LlkhfExtended, scanCode: 30));
                Assert.AreEqual(1, timers.Count);
                Assert.AreEqual(28, timers[0].DueTimeMs);
                CollectionAssert.AreEqual(new string[0], injected);

                timers[0].Fire();
            }

            CollectionAssert.AreEqual(new[] { "65:True:30" }, injected);
        }

        [TestMethod]
        public void BlockRelease_CancelsPendingReleaseWhenBounceKeyDownArrives()
        {
            var clock = new ManualClock(1000);
            var timers = new List<ManualReleaseTimer>();
            var injected = new List<int>();
            var filtered = new List<string>();
            using (var core = CreateCore(
                clock,
                filterMode: "BlockRelease",
                createTimer: (vk, callback) =>
                {
                    var timer = new ManualReleaseTimer(vk, callback);
                    timers.Add(timer);
                    return timer;
                },
                injectKeyUp: (vk, _, __) => injected.Add(vk),
                logFiltered: (vk, action) => filtered.Add(vk + ":" + action)))
            {
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1010;
                Assert.IsTrue(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyUp));
                clock.Ticks = 1020;

                Assert.IsTrue(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
                timers[0].Fire();
            }

            Assert.AreEqual(0, injected.Count);
            CollectionAssert.AreEqual(new[] { "65:release-held" }, filtered);
        }

        [TestMethod]
        public void SyntheticEventsPassThroughWithoutChangingDebounceState()
        {
            var clock = new ManualClock(1000);
            using (var core = CreateCore(clock))
            {
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1010;
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyUp));
                clock.Ticks = 1020;

                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown,
                    KeyboardDebounceFilterCore.LlkhfInjected));
                clock.Ticks = 1021;
                Assert.IsTrue(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
            }
        }

        [TestMethod]
        public void CapsLockIsAlwaysExcludedFromFiltering()
        {
            var clock = new ManualClock(1000);
            using (var core = CreateCore(clock))
            {
                Assert.IsFalse(core.ShouldFilter(VkCapsLock, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1010;
                Assert.IsFalse(core.ShouldFilter(VkCapsLock, KeyboardDebounceFilterCore.WmKeyUp));
                clock.Ticks = 1020;

                Assert.IsFalse(core.ShouldFilter(VkCapsLock, KeyboardDebounceFilterCore.WmKeyDown));
            }
        }

        [TestMethod]
        public void PerKeyThresholdsOverrideDefaultThreshold()
        {
            var clock = new ManualClock(1000);
            using (var core = CreateCore(clock, minRepeatIntervalMs: 10, perKeyThresholds: new Dictionary<string, double>
            {
                { "A", 50.0 }
            }))
            {
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1010;
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyUp));
                clock.Ticks = 1040;
                Assert.IsTrue(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));

                clock.Ticks = 2000;
                Assert.IsFalse(core.ShouldFilter(VkB, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 2010;
                Assert.IsFalse(core.ShouldFilter(VkB, KeyboardDebounceFilterCore.WmKeyUp));
                clock.Ticks = 2025;
                Assert.IsFalse(core.ShouldFilter(VkB, KeyboardDebounceFilterCore.WmKeyDown));
            }
        }

        [TestMethod]
        public void BurstBypassAllowsRepeatedCharactersDuringMachineSpeedBursts()
        {
            var clock = new ManualClock(1000);
            using (var core = CreateCore(clock, burstBypass: true))
            {
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1001;
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyUp));

                clock.Ticks = 1010;
                Assert.IsFalse(core.ShouldFilter(VkB, KeyboardDebounceFilterCore.WmKeyDown));
                clock.Ticks = 1011;
                Assert.IsFalse(core.ShouldFilter(VkB, KeyboardDebounceFilterCore.WmKeyUp));

                clock.Ticks = 1020;
                Assert.IsFalse(core.ShouldFilter(VkA, KeyboardDebounceFilterCore.WmKeyDown));
            }
        }

        private static KeyboardDebounceFilterCore CreateCore(
            ManualClock clock,
            string filterMode = "BlockRepress",
            double minRepeatIntervalMs = 28.0,
            bool burstBypass = false,
            Dictionary<string, double> perKeyThresholds = null,
            Action<int, string> logFiltered = null,
            Action<string> logConfigWarning = null,
            Action<int, bool, uint> injectKeyUp = null,
            Func<int, Action<int>, KeyboardDebounceFilterCore.IReleaseTimer> createTimer = null)
        {
            return new KeyboardDebounceFilterCore(
                new FilterConfig
                {
                    FilterMode = filterMode,
                    MinRepeatIntervalMs = minRepeatIntervalMs,
                    BurstBypass = burstBypass,
                    ExcludedKeys = new string[0],
                    PerKeyMinRepeatIntervalMs = perKeyThresholds ?? new Dictionary<string, double>()
                },
                logFiltered,
                logConfigWarning,
                injectKeyUp,
                clock.GetTimestamp,
                1000.0,
                createTimer);
        }

        private sealed class ManualClock
        {
            public ManualClock(long ticks)
            {
                Ticks = ticks;
            }

            public long Ticks { get; set; }

            public long GetTimestamp()
            {
                return Ticks;
            }
        }

        private sealed class ManualReleaseTimer : KeyboardDebounceFilterCore.IReleaseTimer
        {
            private readonly int _vk;
            private readonly Action<int> _callback;
            private bool _cancelled = true;

            public ManualReleaseTimer(int vk, Action<int> callback)
            {
                _vk = vk;
                _callback = callback;
            }

            public int DueTimeMs { get; private set; }

            public void Change(int dueTimeMs)
            {
                DueTimeMs = dueTimeMs;
                _cancelled = false;
            }

            public void Cancel()
            {
                _cancelled = true;
            }

            public void Fire()
            {
                if (!_cancelled)
                {
                    _cancelled = true;
                    _callback(_vk);
                }
            }

            public void Dispose()
            {
                _cancelled = true;
            }
        }
    }
}
