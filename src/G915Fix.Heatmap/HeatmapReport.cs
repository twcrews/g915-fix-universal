using G915Fix.Core.Diagnostics;
using G915Fix.Core.Input;

namespace G915Fix.Heatmap;

/// <summary>Aggregated, layout-independent data for a heatmap report.</summary>
public sealed record HeatmapReport(
    int TotalFilteredEvents,
    IReadOnlyDictionary<HidKeyboardUsage, int> KeyboardCounts,
    IReadOnlyDictionary<MouseButton, int> MouseCounts,
    IReadOnlyDictionary<DateOnly, int> DailyCounts,
    DateTimeOffset? LastEventTimestamp,
    int IgnoredEventCount,
    IReadOnlyList<string> ConfigurationWarnings)
{
    public KeyValuePair<HidKeyboardUsage, int>? MostFilteredKey => KeyboardCounts.Count == 0
        ? null
        : KeyboardCounts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First();
}
