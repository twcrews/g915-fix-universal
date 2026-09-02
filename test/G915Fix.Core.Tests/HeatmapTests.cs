using G915Fix.Core.Diagnostics;
using G915Fix.Core.Input;
using G915Fix.Heatmap;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.Core.Tests;

[TestClass]
public sealed class HeatmapTests
{
    [TestMethod]
    public void Analyze_AggregatesFilteredEventsAndWarnings()
    {
        DateTimeOffset timestamp = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        FilterDiagnosticEvent[] events =
        [
            new(FilterDiagnosticEvent.CurrentSchemaVersion, timestamp, FilterDiagnosticEventKind.KeyboardFiltered,
                Key: HidKeyboardUsage.A, Action: FilterDiagnosticAction.RepressBlocked),
            new(FilterDiagnosticEvent.CurrentSchemaVersion, timestamp.AddMinutes(1), FilterDiagnosticEventKind.KeyboardFiltered,
                Key: HidKeyboardUsage.A, Action: FilterDiagnosticAction.ReleaseHeld),
            new(FilterDiagnosticEvent.CurrentSchemaVersion, timestamp.AddDays(1), FilterDiagnosticEventKind.MouseFiltered,
                MouseButton: MouseButton.Left, Action: FilterDiagnosticAction.MousePressBlocked),
            new(FilterDiagnosticEvent.CurrentSchemaVersion, timestamp, FilterDiagnosticEventKind.ConfigurationWarning,
                Message: "Invalid key <example>"),
            new(99, timestamp, FilterDiagnosticEventKind.KeyboardFiltered, Key: HidKeyboardUsage.B)
        ];

        HeatmapReport report = HeatmapAnalyzer.Analyze(events);

        Assert.AreEqual(3, report.TotalFilteredEvents);
        Assert.AreEqual(2, report.KeyboardCounts[HidKeyboardUsage.A]);
        Assert.AreEqual(1, report.MouseCounts[MouseButton.Left]);
        Assert.AreEqual(1, report.IgnoredEventCount);
        CollectionAssert.AreEqual(new[] { "Invalid key <example>" }, report.ConfigurationWarnings.ToArray());
        StringAssert.Contains(HtmlHeatmapRenderer.Render(report), "Invalid key &lt;example&gt;");
    }

    [TestMethod]
    public async Task JsonLinesDiagnosticLog_RoundTripsEvents()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var sink = new JsonLinesDiagnosticSink(path, append: false))
            {
                sink.Record(new FilterDiagnosticEvent(
                    FilterDiagnosticEvent.CurrentSchemaVersion,
                    DateTimeOffset.UtcNow,
                    FilterDiagnosticEventKind.KeyboardFiltered,
                    Key: HidKeyboardUsage.Enter,
                    Action: FilterDiagnosticAction.RepressBlocked));
            }

            HeatmapReport report = await HeatmapAnalyzer.AnalyzeAsync(JsonLinesDiagnosticLog.ReadAsync(path));
            Assert.AreEqual(1, report.TotalFilteredEvents);
            Assert.AreEqual(1, report.KeyboardCounts[HidKeyboardUsage.Enter]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LegacyWindowsLogImporter_MapsVirtualKeysAndMouseButtons()
    {
        FilterDiagnosticEvent[] events = LegacyWindowsLogImporter.ParseLines(
        [
            "2026-04-15 16:53:41.439 - I=73 filtered",
            "2026-06-17 12:56:36.884 - Mouse_Left filtered",
            "2026-06-13 18:55:01.123 - ConfigWarning: old config warning"
        ]).ToArray();

        Assert.AreEqual(3, events.Length);
        Assert.AreEqual(HidKeyboardUsage.I, events[0].Key);
        Assert.AreEqual(MouseButton.Left, events[1].MouseButton);
        Assert.AreEqual(FilterDiagnosticEventKind.ConfigurationWarning, events[2].Kind);
    }
}
