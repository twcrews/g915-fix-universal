namespace G915Fix.Heatmap;

/// <summary>Controls which diagnostic events contribute to a report.</summary>
public sealed class HeatmapReportOptions
{
    /// <summary>Events before this UTC instant are excluded.</summary>
    public DateTimeOffset? FromUtc { get; init; }
}
