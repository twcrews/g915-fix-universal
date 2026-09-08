using G915Fix.Core.Diagnostics;
using G915Fix.MacOS.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.MacOS.Tests;

[TestClass]
public sealed class MacHeatmapReportServiceTests
{
    [TestMethod]
    public async Task GeneratesHtmlAndUsesHostBrowserLauncher()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string logPath = Path.Combine(directory, "filter-diagnostics.jsonl");
        string? openedPath = null;
        try
        {
            using (var sink = new JsonLinesFilterDiagnosticSink(logPath))
            {
                sink.Record(new FilterDiagnosticEvent(
                    FilterDiagnosticEvent.CurrentSchemaVersion,
                    DateTimeOffset.UtcNow,
                    FilterDiagnosticEventKind.KeyboardFiltered,
                    Key: G915Fix.Core.Input.HidKeyboardUsage.A,
                    Action: FilterDiagnosticAction.RepressBlocked));
            }

            var service = new MacHeatmapReportService(
                () => logPath,
                (path, _) =>
                {
                    openedPath = path;
                    return Task.CompletedTask;
                });
            var result = await service.GenerateAndOpenAsync();

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(result.ReportPath);
            Assert.AreEqual(result.ReportPath, openedPath);
            Assert.IsTrue(File.Exists(result.ReportPath));
            StringAssert.Contains(await File.ReadAllTextAsync(result.ReportPath), "Filtered events");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task OpensAnEmptyReportWhenNoDiagnosticLogExists()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string logPath = Path.Combine(directory, "filter-diagnostics.jsonl");
        string? openedPath = null;
        try
        {
            var service = new MacHeatmapReportService(
                () => logPath,
                (path, _) =>
                {
                    openedPath = path;
                    return Task.CompletedTask;
                });

            var result = await service.GenerateAndOpenAsync();

            Assert.IsTrue(result.Succeeded, result.Message);
            string reportPath = result.ReportPath!;
            Assert.AreEqual(reportPath, openedPath);
            StringAssert.Contains(result.Message, "empty heatmap");
            StringAssert.Contains(await File.ReadAllTextAsync(reportPath), "Filtered events");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
