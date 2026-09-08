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
        string reportPath = Path.ChangeExtension(logPath, ".html");
        string temporaryPath = reportPath + ".tmp-" + Guid.NewGuid().ToString("N");
        bool hasDiagnosticLog = File.Exists(logPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            HeatmapReport report = hasDiagnosticLog
                ? await HeatmapAnalyzer.AnalyzeAsync(
                    JsonLinesDiagnosticLog.ReadAsync(logPath, cancellationToken),
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                : HeatmapAnalyzer.Analyze([]);
            await File.WriteAllTextAsync(temporaryPath, HtmlHeatmapRenderer.Render(report), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, reportPath, overwrite: true);
            await _open(reportPath, cancellationToken).ConfigureAwait(false);
            return new HeatmapGenerationResult(
                true,
                reportPath,
                hasDiagnosticLog
                    ? "Heatmap opened in your default browser."
                    : "No diagnostic events have been recorded yet; an empty heatmap was opened.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            return new HeatmapGenerationResult(false, Message: $"Could not generate the heatmap: {exception.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
                // Temporary cleanup must not hide a report result.
            }
        }
    }

    private static async Task OpenAsync(string reportPath, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/open")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
            ArgumentList = { reportPath }
        }) ?? throw new InvalidOperationException("macOS could not start the browser launcher.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "macOS could not open the heatmap in a browser."
                : error.Trim());
        }
    }
}
