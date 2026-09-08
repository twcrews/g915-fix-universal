namespace G915Fix.Core.Heatmap;

/// <summary>
/// Host-owned report generation and presentation for the portable diagnostic heatmap.
/// Core provides the JSON Lines reader and renderer; hosts choose paths and browser APIs.
/// </summary>
public interface IHeatmapReportService
{
    Task<HeatmapGenerationResult> GenerateAndOpenAsync(CancellationToken cancellationToken = default);
}

public sealed record HeatmapGenerationResult(
    bool Succeeded,
    string? ReportPath = null,
    string? Message = null);
