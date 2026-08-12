using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeyboardRepeatFilter.Tests
{
    [TestClass]
    public class MouseDebounceFilterCoreTests
    {
        [TestMethod]
        public void ShouldFilter_FiltersButtonDownWithinThresholdAfterButtonUp()
        {
            var clock = new ManualClock(1000);
            var filtered = new List<string>();
            var core = CreateCore(clock, logFiltered: (button, action) => filtered.Add(button + ":" + action));

            Assert.IsFalse(core.ShouldFilter(0, isDown: true));
            clock.Ticks = 1010;
            Assert.IsFalse(core.ShouldFilter(0, isDown: false));
            clock.Ticks = 1020;

            Assert.IsTrue(core.ShouldFilter(0, isDown: true));
            CollectionAssert.AreEqual(new[] { "0:filtered" }, filtered);
        }

        [TestMethod]
        public void ShouldFilter_PreservesIntentionalClicksOutsideThreshold()
        {
            var clock = new ManualClock(1000);
            var core = CreateCore(clock, mouseMinRepeatIntervalMs: 50);

            Assert.IsFalse(core.ShouldFilter(0, isDown: true));
            clock.Ticks = 1010;
            Assert.IsFalse(core.ShouldFilter(0, isDown: false));
            clock.Ticks = 1100;

            Assert.IsFalse(core.ShouldFilter(0, isDown: true));
        }

        [TestMethod]
        public void ShouldFilter_DoesNotFilterExcludedButtons()
        {
            var clock = new ManualClock(1000);
            var core = CreateCore(clock, excludedButtons: new[] { "Left" });

            Assert.IsFalse(core.ShouldFilter(0, isDown: true));
            clock.Ticks = 1010;
            Assert.IsFalse(core.ShouldFilter(0, isDown: false));
            clock.Ticks = 1020;

            Assert.IsFalse(core.ShouldFilter(0, isDown: true));
        }

        [TestMethod]
        public void ResolveButton_AcceptsDocumentedButtonAliases()
        {
            Assert.AreEqual(0, MouseDebounceFilterCore.ResolveButton("LBUTTON"));
            Assert.AreEqual(1, MouseDebounceFilterCore.ResolveButton("r"));
            Assert.AreEqual(2, MouseDebounceFilterCore.ResolveButton("Middle"));
            Assert.AreEqual(3, MouseDebounceFilterCore.ResolveButton("XBUTTON1"));
            Assert.AreEqual(4, MouseDebounceFilterCore.ResolveButton("x2"));
            Assert.AreEqual(-1, MouseDebounceFilterCore.ResolveButton("unknown"));
        }

        private static MouseDebounceFilterCore CreateCore(
            ManualClock clock,
            double mouseMinRepeatIntervalMs = 50.0,
            string[] excludedButtons = null,
            System.Action<int, string> logFiltered = null)
        {
            return new MouseDebounceFilterCore(
                new FilterConfig
                {
                    MouseMinRepeatIntervalMs = mouseMinRepeatIntervalMs,
                    ExcludedMouseButtons = excludedButtons ?? new string[0]
                },
                logFiltered,
                null,
                clock.GetTimestamp,
                1000.0);
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
    }
}
