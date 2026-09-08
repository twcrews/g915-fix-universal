using System.Diagnostics;
using G915Fix.Core.Heatmap;

namespace G915Fix.MacOS.Services;

/// <summary>Generates the portable HTML report and opens it with the user's default browser.</summary>
internal sealed class MacHeatmapReportService : IHeatmapReportService
{
    private readonly Func<string?> _getDiagnosticPath;
    private readonly Func<string, CancellationToken, Task> _open;

    public MacHeatmapReportService(
        Func<string?> getDiagnosticPath,
        Func<string, CancellationToken, Task>? open = null)
    {
        _getDiagnosticPath = getDiagnosticPath ?? throw new ArgumentNullException(nameof(getDiagnosticPath));
        _open = open ?? OpenAsync;
    }

    public async Task<HeatmapGenerationResult> GenerateAndOpenAsync(CancellationToken cancellationToken = default)
    {
        string? configuredPath = _getDiagnosticPath();
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return new HeatmapGenerationResult(false, Message: "Diagnostics do not have a configured log path.");
        }

        string logPath = Path.GetFullPath(configuredPath);
        if (!File.Exists(logPath))
        {
            return new HeatmapGenerationResult(false, Message: "No diagnostic events have been recorded yet. Enable diagnostics, save, and use the filter before opening a heatmap.");
        }

        try
        {
            HeatmapReport report = await HeatmapAnalyzer.AnalyzeAsync(
                JsonLinesDiagnosticLog.ReadAsync(logPath, cancellationToken),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            string reportPath = Path.ChangeExtension(logPath, ".html");
            string temporaryPath = reportPath + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(temporaryPath, HtmlHeatmapRenderer.Render(report), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, reportPath, overwrite: true);
            await _open(reportPath, cancellationToken).ConfigureAwait(false);
            return new HeatmapGenerationResult(true, reportPath, "Heatmap opened in your default browser.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new HeatmapGenerationResult(false, Message: $"Could not generate the heatmap: {exception.Message}");
        }
    }

    private static Task OpenAsync(string reportPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo("open")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { reportPath }
        })?.Dispose();
        return Task.CompletedTask;
    }
}
