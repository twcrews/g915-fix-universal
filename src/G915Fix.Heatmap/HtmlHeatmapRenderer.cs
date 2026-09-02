using System.Globalization;
using System.Net;
using System.Text;
using G915Fix.Core.Input;

namespace G915Fix.Heatmap;

/// <summary>Renders a self-contained, generic HID keyboard and mouse heatmap.</summary>
public static class HtmlHeatmapRenderer
{
    private static readonly IReadOnlyList<IReadOnlyList<HidKeyboardUsage>> KeyboardRows =
    [
        [HidKeyboardUsage.Escape, HidKeyboardUsage.F1, HidKeyboardUsage.F2, HidKeyboardUsage.F3,
         HidKeyboardUsage.F4, HidKeyboardUsage.F5, HidKeyboardUsage.F6, HidKeyboardUsage.F7,
         HidKeyboardUsage.F8, HidKeyboardUsage.F9, HidKeyboardUsage.F10, HidKeyboardUsage.F11,
         HidKeyboardUsage.F12],
        [HidKeyboardUsage.Grave, HidKeyboardUsage.Number1, HidKeyboardUsage.Number2, HidKeyboardUsage.Number3,
         HidKeyboardUsage.Number4, HidKeyboardUsage.Number5, HidKeyboardUsage.Number6, HidKeyboardUsage.Number7,
         HidKeyboardUsage.Number8, HidKeyboardUsage.Number9, HidKeyboardUsage.Number0, HidKeyboardUsage.Minus,
         HidKeyboardUsage.Equal, HidKeyboardUsage.Backspace],
        [HidKeyboardUsage.Tab, HidKeyboardUsage.Q, HidKeyboardUsage.W, HidKeyboardUsage.E, HidKeyboardUsage.R,
         HidKeyboardUsage.T, HidKeyboardUsage.Y, HidKeyboardUsage.U, HidKeyboardUsage.I, HidKeyboardUsage.O,
         HidKeyboardUsage.P, HidKeyboardUsage.LeftBracket, HidKeyboardUsage.RightBracket, HidKeyboardUsage.Backslash],
        [HidKeyboardUsage.CapsLock, HidKeyboardUsage.A, HidKeyboardUsage.S, HidKeyboardUsage.D, HidKeyboardUsage.F,
         HidKeyboardUsage.G, HidKeyboardUsage.H, HidKeyboardUsage.J, HidKeyboardUsage.K, HidKeyboardUsage.L,
         HidKeyboardUsage.Semicolon, HidKeyboardUsage.Apostrophe, HidKeyboardUsage.Enter],
        [HidKeyboardUsage.LeftShift, HidKeyboardUsage.Z, HidKeyboardUsage.X, HidKeyboardUsage.C, HidKeyboardUsage.V,
         HidKeyboardUsage.B, HidKeyboardUsage.N, HidKeyboardUsage.M, HidKeyboardUsage.Comma, HidKeyboardUsage.Period,
         HidKeyboardUsage.Slash, HidKeyboardUsage.RightShift],
        [HidKeyboardUsage.LeftControl, HidKeyboardUsage.LeftGui, HidKeyboardUsage.LeftAlt, HidKeyboardUsage.Space,
         HidKeyboardUsage.RightAlt, HidKeyboardUsage.Application, HidKeyboardUsage.RightControl]
    ];

    public static string Render(HeatmapReport report, string title = "G915 Fix heatmap")
    {
        ArgumentNullException.ThrowIfNull(report);
        string encodedTitle = WebUtility.HtmlEncode(title);
        int maxKeyboard = Math.Max(1, report.KeyboardCounts.Values.DefaultIfEmpty().Max());
        int maxMouse = Math.Max(1, report.MouseCounts.Values.DefaultIfEmpty().Max());
        var html = new StringBuilder();

        html.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.AppendLine($"<title>{encodedTitle}</title><style>{Css}</style></head><body><main>");
        html.AppendLine($"<h1>{encodedTitle}</h1><p class=\"subtitle\">Offline diagnostic report</p>");
        html.AppendLine("<section class=\"stats\">");
        Stat(html, report.TotalFilteredEvents.ToString(CultureInfo.InvariantCulture), "Filtered events");
        Stat(html, report.MostFilteredKey is { } topKey
            ? $"{Label(topKey.Key)} ({topKey.Value})"
            : "None", "Top keyboard key");
        Stat(html, report.MouseCounts.Values.Sum().ToString(CultureInfo.InvariantCulture), "Mouse events");
        Stat(html, report.LastEventTimestamp?.ToLocalTime().ToString("u") ?? "None", "Last event");
        html.AppendLine("</section>");

        if (report.ConfigurationWarnings.Count > 0)
        {
            html.AppendLine("<section class=\"warning\"><strong>Configuration warnings</strong><ul>");
            foreach (string warning in report.ConfigurationWarnings)
            {
                html.AppendLine($"<li>{WebUtility.HtmlEncode(warning)}</li>");
            }

            html.AppendLine("</ul></section>");
        }

        html.AppendLine("<h2>Keyboard</h2><section class=\"keyboard\">");
        foreach (IReadOnlyList<HidKeyboardUsage> row in KeyboardRows)
        {
            html.AppendLine("<div class=\"key-row\">");
            foreach (HidKeyboardUsage key in row)
            {
                int count = report.KeyboardCounts.GetValueOrDefault(key);
                string color = Color(count, maxKeyboard);
                html.AppendLine($"<div class=\"key\" style=\"--heat:{color}\" title=\"{WebUtility.HtmlEncode(Label(key))}: {count} filtered events\"><span>{WebUtility.HtmlEncode(Label(key))}</span><b>{count}</b></div>");
            }

            html.AppendLine("</div>");
        }

        html.AppendLine("</section>");
        var layoutKeys = KeyboardRows.SelectMany(row => row).ToHashSet();
        var otherKeys = report.KeyboardCounts
            .Where(pair => !layoutKeys.Contains(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .ToArray();
        if (otherKeys.Length > 0)
        {
            html.AppendLine("<h2>Other keyboard keys</h2><section class=\"mouse\">");
            foreach ((HidKeyboardUsage key, int count) in otherKeys)
            {
                html.AppendLine($"<div class=\"mouse-button\" style=\"--heat:{Color(count, maxKeyboard)}\"><span>{WebUtility.HtmlEncode(Label(key))}</span><b>{count}</b></div>");
            }

            html.AppendLine("</section>");
        }

        html.AppendLine("<h2>Mouse buttons</h2><section class=\"mouse\">");
        foreach (MouseButton button in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.X1, MouseButton.X2 })
        {
            int count = report.MouseCounts.GetValueOrDefault(button);
            html.AppendLine($"<div class=\"mouse-button\" style=\"--heat:{Color(count, maxMouse)}\"><span>{WebUtility.HtmlEncode(MouseLabel(button))}</span><b>{count}</b></div>");
        }

        html.AppendLine("</section>");
        if (report.DailyCounts.Count > 0)
        {
            int maxDaily = Math.Max(1, report.DailyCounts.Values.Max());
            html.AppendLine("<h2>Daily filtered event count</h2><section class=\"daily\">");
            foreach ((DateOnly day, int count) in report.DailyCounts.OrderByDescending(pair => pair.Key))
            {
                double width = count * 100d / maxDaily;
                html.AppendLine($"<div class=\"day\"><time>{day:yyyy-MM-dd}</time><div class=\"bar\"><i style=\"width:{width:F1}%\"></i></div><b>{count}</b></div>");
            }

            html.AppendLine("</section>");
        }

        if (report.IgnoredEventCount > 0)
        {
            html.AppendLine($"<p class=\"ignored\">Ignored {report.IgnoredEventCount} unsupported diagnostic event(s).</p>");
        }

        html.AppendLine("</main></body></html>");
        return html.ToString();
    }

    private static void Stat(StringBuilder html, string value, string label) =>
        html.AppendLine($"<div class=\"stat\"><b>{WebUtility.HtmlEncode(value)}</b><span>{WebUtility.HtmlEncode(label)}</span></div>");

    private static string Label(HidKeyboardUsage usage) => usage switch
    {
        HidKeyboardUsage.Space => "Space",
        HidKeyboardUsage.Grave => "`",
        HidKeyboardUsage.LeftControl => "Left Ctrl",
        HidKeyboardUsage.RightControl => "Right Ctrl",
        HidKeyboardUsage.LeftAlt => "Left Alt",
        HidKeyboardUsage.RightAlt => "Right Alt",
        HidKeyboardUsage.LeftGui => "Left GUI",
        HidKeyboardUsage.RightGui => "Right GUI",
        _ when usage.ToString().StartsWith("Number", StringComparison.Ordinal) => usage.ToString()[6..],
        _ => usage.ToString()
    };

    private static string MouseLabel(MouseButton button) => button.Code switch
    {
        0 => "Left", 1 => "Right", 2 => "Middle", 3 => "X1", 4 => "X2", _ => $"Button {button.Code}"
    };

    private static string Color(int count, int maximum)
    {
        if (count == 0) return "#e6e8ee";
        double ratio = Math.Clamp((double)count / maximum, 0, 1);
        int red = (int)Math.Round(46 + (200 - 46) * ratio);
        int green = (int)Math.Round(168 + (24 - 168) * ratio);
        int blue = (int)Math.Round(79 + (73 - 79) * ratio);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private const string Css = """
        :root { color-scheme: light dark; font-family: system-ui,sans-serif; }
        body { margin: 0; padding: 2rem; background: #f5f6fa; color: #1d2330; }
        main { max-width: 1000px; margin: auto; } h1 { margin-bottom: 0; } .subtitle { color: #667085; }
        h2 { margin-top: 2rem; font-size: 1rem; } .stats { display:grid; grid-template-columns:repeat(4,1fr); gap:.75rem; }
        .stat,.warning,.key,.mouse-button { border:1px solid #d9dce5; border-radius:.5rem; background:#fff; }
        .stat { padding:1rem; }.stat b,.stat span { display:block; }.stat b { font-size:1.25rem; }.stat span { color:#667085; font-size:.8rem; }
        .warning { margin-top:1rem; padding:1rem; border-color:#ba7517; }.warning ul { margin-bottom:0; }
        .keyboard { overflow:auto; padding:.5rem; }.key-row { display:flex; gap:.35rem; margin-bottom:.35rem; min-width:max-content; }
        .key { width:3.4rem; min-height:3.25rem; padding:.3rem; background:var(--heat); text-align:center; }.key span,.key b { display:block; font-size:.75rem; }.key b { margin-top:.25rem; }
        .mouse { display:flex; gap:.5rem; flex-wrap:wrap; }.mouse-button { min-width:7rem; padding:1rem; background:var(--heat); }.mouse-button span,.mouse-button b { display:block; }
        .daily { display:flex; flex-direction:column; gap:.35rem; }.day { display:grid; grid-template-columns:6rem 1fr 3rem; gap:.5rem; align-items:center; }.bar { height:.8rem; background:#e6e8ee; border-radius:.25rem; overflow:hidden; }.bar i { display:block; height:100%; background:linear-gradient(90deg,#2ea84f,#c81849); }.ignored { color:#667085; font-size:.8rem; }
        @media (prefers-color-scheme:dark) { body { background:#131722;color:#eef1f7; }.stat,.warning,.key,.mouse-button { border-color:#394050; }.stat { background:#1c2230; }.key,.mouse-button { color:#18202b; }.warning { background:#2a2418; }.bar { background:#394050; } }
        @media (max-width:700px) { body { padding:1rem; }.stats { grid-template-columns:repeat(2,1fr); } }
        """;
}
