using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace KeyboardRepeatFilter
{
    /// <summary>Platform-neutral mouse-button debounce state machine.</summary>
    internal sealed class MouseDebounceFilterCore
    {
        private const int ButtonCount = 5;
        private static readonly string[] ButtonNames = { "Left", "Right", "Middle", "X1", "X2" };

        private readonly long[] _lastUpTicks = new long[ButtonCount];
        private readonly bool[] _isPressed = new bool[ButtonCount];
        private readonly bool[] _excluded = new bool[ButtonCount];
        private readonly Func<long> _getTimestamp;
        private readonly Action<int, string> _logFiltered;
        private readonly Action<string> _logConfigWarning;
        private readonly long _thresholdTicks;

        public MouseDebounceFilterCore(
            FilterConfig config,
            Action<int, string> logFiltered,
            Action<string> logConfigWarning,
            Func<long> getTimestamp = null,
            double? timestampFrequency = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
            _logFiltered = logFiltered ?? ((_, __) => { });
            _logConfigWarning = logConfigWarning ?? (_ => { });
            double frequency = timestampFrequency ?? Stopwatch.Frequency;
            _thresholdTicks = (long)(frequency * config.MouseMinRepeatIntervalMs / 1000.0);

            var unresolved = new List<string>();
            if (config.ExcludedMouseButtons != null)
            {
                foreach (var token in config.ExcludedMouseButtons)
                {
                    var index = ResolveButton(token);
                    if (index < 0)
                    {
                        unresolved.Add(token);
                        continue;
                    }

                    _excluded[index] = true;
                }
            }

            if (unresolved.Count > 0)
            {
                _logConfigWarning("Unrecognized mouse button name(s) in config (ignored): " + string.Join(", ", unresolved));
            }
        }

        public bool ShouldFilter(int button, bool isDown)
        {
            if (button < 0 || button >= ButtonCount || _excluded[button])
            {
                return false;
            }

            var now = _getTimestamp();

            if (isDown)
            {
                if (!_isPressed[button] && (now - _lastUpTicks[button]) < _thresholdTicks)
                {
                    _logFiltered(button, "filtered");
                    return true;
                }

                _isPressed[button] = true;
            }
            else
            {
                _lastUpTicks[button] = now;
                _isPressed[button] = false;
            }

            return false;
        }

        internal static int ResolveButton(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return -1;
            }

            switch (token.Trim().ToUpperInvariant())
            {
                case "LEFT":
                case "L":
                case "LBUTTON":
                    return 0;
                case "RIGHT":
                case "R":
                case "RBUTTON":
                    return 1;
                case "MIDDLE":
                case "M":
                case "MBUTTON":
                    return 2;
                case "X1":
                case "XBUTTON1":
                    return 3;
                case "X2":
                case "XBUTTON2":
                    return 4;
                default:
                    return -1;
            }
        }

        internal static string ButtonName(int button)
        {
            return button >= 0 && button < ButtonNames.Length ? ButtonNames[button] : "Unknown";
        }
    }
}
