using G915Fix.Core.Diagnostics;
using G915Fix.Core.Input;

namespace G915Fix.Heatmap;

/// <summary>Streams filter diagnostics into layout-independent heatmap counts.</summary>
public static class HeatmapAnalyzer
{
    public static HeatmapReport Analyze(
        IEnumerable<FilterDiagnosticEvent> events,
        HeatmapReportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var accumulator = new Accumulator(options);
        foreach (FilterDiagnosticEvent diagnosticEvent in events)
        {
            accumulator.Add(diagnosticEvent);
        }

        return accumulator.Build();
    }

    public static async Task<HeatmapReport> AnalyzeAsync(
        IAsyncEnumerable<FilterDiagnosticEvent> events,
        HeatmapReportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        var accumulator = new Accumulator(options);
        await foreach (FilterDiagnosticEvent diagnosticEvent in events
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            accumulator.Add(diagnosticEvent);
        }

        return accumulator.Build();
    }

    private sealed class Accumulator(HeatmapReportOptions? options)
    {
        private readonly DateTimeOffset? _fromUtc = options?.FromUtc?.ToUniversalTime();
        private readonly Dictionary<HidKeyboardUsage, int> _keyboardCounts = [];
        private readonly Dictionary<MouseButton, int> _mouseCounts = [];
        private readonly Dictionary<DateOnly, int> _dailyCounts = [];
        private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
        private int _total;
        private int _ignored;
        private DateTimeOffset? _lastTimestamp;

        public void Add(FilterDiagnosticEvent diagnosticEvent)
        {
            if (diagnosticEvent.SchemaVersion != FilterDiagnosticEvent.CurrentSchemaVersion)
            {
                _ignored++;
                return;
            }

            DateTimeOffset timestamp = diagnosticEvent.Timestamp.ToUniversalTime();
            if (_fromUtc is not null && timestamp < _fromUtc)
            {
                return;
            }

            if (diagnosticEvent.Kind == FilterDiagnosticEventKind.ConfigurationWarning)
            {
                if (!string.IsNullOrWhiteSpace(diagnosticEvent.Message))
                {
                    _warnings.Add(diagnosticEvent.Message.Trim());
                }

                return;
            }

            switch (diagnosticEvent.Kind)
            {
                case FilterDiagnosticEventKind.KeyboardFiltered when diagnosticEvent.Key is HidKeyboardUsage key:
                    Increment(_keyboardCounts, key);
                    break;
                case FilterDiagnosticEventKind.MouseFiltered when diagnosticEvent.MouseButton is MouseButton button:
                    Increment(_mouseCounts, button);
                    break;
                default:
                    _ignored++;
                    return;
            }

            _total++;
            DateOnly day = DateOnly.FromDateTime(timestamp.UtcDateTime);
            Increment(_dailyCounts, day);
            if (_lastTimestamp is null || timestamp > _lastTimestamp)
            {
                _lastTimestamp = timestamp;
            }
        }

        public HeatmapReport Build() => new(
            _total,
            new Dictionary<HidKeyboardUsage, int>(_keyboardCounts),
            new Dictionary<MouseButton, int>(_mouseCounts),
            new Dictionary<DateOnly, int>(_dailyCounts),
            _lastTimestamp,
            _ignored,
            _warnings.OrderBy(message => message, StringComparer.Ordinal).ToArray());

        private static void Increment<TKey>(Dictionary<TKey, int> counts, TKey key)
            where TKey : notnull => counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}
