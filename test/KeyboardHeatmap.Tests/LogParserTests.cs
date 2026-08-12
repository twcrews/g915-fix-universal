using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeyboardHeatmap.Tests
{
    [TestClass]
    public class LogParserTests
    {
        [TestMethod]
        public void ParseFile_ParsesKnownKeyboardMouseAndLifecycleEntries()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "2026-04-15 13:52:17.725 - Startup: version=1.1.0.0, pid=5136, minRepeatIntervalMs=28",
                    "2026-04-15 16:53:41.439 - I=73 filtered",
                    "2026-05-15 09:01:22.010 - LSHIFT=160 release-held",
                    "2026-06-17 12:56:36.884 - Mouse_Left filtered",
                    "2026-06-13 18:55:01.123 - ConfigWarning: Unrecognized key name(s) in config (ignored): foo",
                    "2026-04-16 15:28:01.784 - Shutdown: reason=UserExit, uptimeSec=30787.1, pid=21300",
                    "this line is not recognized"
                });

                var entries = LogParser.ParseFile(path);

                Assert.AreEqual(7, entries.Count);

                Assert.AreEqual(LogEntryKind.Startup, entries[0].Kind);
                Assert.AreEqual(new DateTime(2026, 4, 15, 13, 52, 17, 725), entries[0].Timestamp);
                Assert.AreEqual("1.1.0.0", entries[0].Version);
                Assert.AreEqual(5136, entries[0].Pid);
                Assert.AreEqual(28, entries[0].MinRepeatIntervalMs);

                Assert.AreEqual(LogEntryKind.Filtered, entries[1].Kind);
                Assert.AreEqual("I", entries[1].KeyName);
                Assert.AreEqual(73, entries[1].KeyCode);
                Assert.AreEqual("filtered", entries[1].Action);

                Assert.AreEqual(LogEntryKind.Filtered, entries[2].Kind);
                Assert.AreEqual("LSHIFT", entries[2].KeyName);
                Assert.AreEqual(160, entries[2].KeyCode);
                Assert.AreEqual("release-held", entries[2].Action);

                Assert.AreEqual(LogEntryKind.Filtered, entries[3].Kind);
                Assert.AreEqual("Mouse_Left", entries[3].KeyName);
                Assert.IsFalse(entries[3].KeyCode.HasValue);
                Assert.AreEqual("filtered", entries[3].Action);

                Assert.AreEqual(LogEntryKind.ConfigWarning, entries[4].Kind);
                Assert.AreEqual("Unrecognized key name(s) in config (ignored): foo", entries[4].Message);

                Assert.AreEqual(LogEntryKind.Shutdown, entries[5].Kind);
                Assert.AreEqual("UserExit", entries[5].ShutdownReason);
                Assert.AreEqual(30787.1, entries[5].UptimeSec.Value, 0.0001);
                Assert.AreEqual(21300, entries[5].Pid);

                Assert.AreEqual(LogEntryKind.Unknown, entries[6].Kind);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
