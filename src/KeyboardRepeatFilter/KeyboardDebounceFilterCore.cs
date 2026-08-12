using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace KeyboardRepeatFilter
{
    /// <summary>
    /// Platform-neutral keyboard debounce state machine used by the Windows hook.
    /// It contains no hook registration or desktop interaction so the existing
    /// filtering semantics can be covered by unit tests.
    /// </summary>
    internal sealed class KeyboardDebounceFilterCore : IDisposable
    {
        internal const int WmKeyDown = 0x0100;
        internal const int WmKeyUp = 0x0101;
        internal const int WmSysKeyDown = 0x0104;
        internal const int WmSysKeyUp = 0x0105;

        internal const uint LlkhfExtended = 0x01;
        internal const uint LlkhfInjected = 0x10;

        private const int VkCapital = 0x14;
        private const double BurstGapMs = 25.0;
        private const int BurstMinStreak = 2;
        private const long InjectedMarkerValue = 0x52464C54;
        internal static readonly UIntPtr InjectedMarker = (UIntPtr)InjectedMarkerValue;

        private readonly long[] _lastUpTicks = new long[256];
        private readonly bool[] _isPressed = new bool[256];
        private readonly bool[] _swallowNextUp = new bool[256];
        private readonly bool[] _excludedKeys = new bool[256];
        private readonly double[] _thresholdTicksByVk = new double[256];
        private readonly int[] _thresholdMsByVk = new int[256];
        private readonly bool[] _pendingUp = new bool[256];
        private readonly bool[] _pendingExtended = new bool[256];
        private readonly uint[] _pendingScan = new uint[256];
        private readonly IReleaseTimer[] _releaseTimers = new IReleaseTimer[256];
        private readonly object _sync = new object();

        private readonly Func<long> _getTimestamp;
        private readonly double _timestampFrequency;
        private readonly Func<int, Action<int>, IReleaseTimer> _createTimer;
        private readonly Action<int, bool, uint> _injectKeyUp;
        private readonly Action<int, string> _logFiltered;
        private readonly Action<string> _logConfigWarning;

        private readonly bool _blockRelease;
        private readonly bool _burstBypass;
        private readonly long _burstGapTicks;
        private long _lastDownTicks;
        private int _rapidStreak;
        private bool _inBurst;
        private bool _disposed;

        public KeyboardDebounceFilterCore(
            FilterConfig config,
            Action<int, string> logFiltered,
            Action<string> logConfigWarning,
            Action<int, bool, uint> injectKeyUp,
            Func<long> getTimestamp = null,
            double? timestampFrequency = null,
            Func<int, Action<int>, IReleaseTimer> createTimer = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
            _timestampFrequency = timestampFrequency ?? Stopwatch.Frequency;
            _createTimer = createTimer ?? ((vk, callback) => new ThreadingReleaseTimer(vk, callback));
            _injectKeyUp = injectKeyUp ?? ((_, __, ___) => { });
            _logFiltered = logFiltered ?? ((_, __) => { });
            _logConfigWarning = logConfigWarning ?? (_ => { });

            _blockRelease = string.Equals(config.FilterMode, "BlockRelease", StringComparison.OrdinalIgnoreCase);
            _burstBypass = config.BurstBypass;
            _burstGapTicks = (long)(_timestampFrequency * BurstGapMs / 1000.0);

            var defaultThresholdTicks = _timestampFrequency * config.MinRepeatIntervalMs / 1000.0;
            var defaultThresholdMs = Math.Max(1, (int)Math.Ceiling(config.MinRepeatIntervalMs));
            for (var i = 0; i < _thresholdTicksByVk.Length; i++)
            {
                _thresholdTicksByVk[i] = defaultThresholdTicks;
                _thresholdMsByVk[i] = defaultThresholdMs;
            }

            var unresolved = new List<string>();

            if (config.PerKeyMinRepeatIntervalMs != null)
            {
                foreach (var kvp in config.PerKeyMinRepeatIntervalMs)
                {
                    if (kvp.Value < 0)
                    {
                        continue;
                    }

                    var codes = KeyTokenResolver.Resolve(kvp.Key);
                    if (codes.Count == 0)
                    {
                        unresolved.Add(kvp.Key);
                        continue;
                    }

                    foreach (var vk in codes)
                    {
                        if (vk >= 0 && vk < _thresholdTicksByVk.Length)
                        {
                            _thresholdTicksByVk[vk] = _timestampFrequency * kvp.Value / 1000.0;
                            _thresholdMsByVk[vk] = Math.Max(1, (int)Math.Ceiling(kvp.Value));
                        }
                    }
                }
            }

            if (config.ExcludedVkCodes != null)
            {
                foreach (var vkCode in config.ExcludedVkCodes)
                {
                    if (vkCode >= 0 && vkCode < _excludedKeys.Length)
                    {
                        _excludedKeys[vkCode] = true;
                    }
                }
            }

            if (config.ExcludedKeys != null)
            {
                foreach (var token in config.ExcludedKeys)
                {
                    var codes = KeyTokenResolver.Resolve(token);
                    if (codes.Count == 0)
                    {
                        unresolved.Add(token);
                        continue;
                    }

                    foreach (var vk in codes)
                    {
                        if (vk >= 0 && vk < _excludedKeys.Length)
                        {
                            _excludedKeys[vk] = true;
                        }
                    }
                }
            }

            _excludedKeys[VkCapital] = true;

            if (unresolved.Count > 0)
            {
                _logConfigWarning("Unrecognized key name(s) in config (ignored): " + string.Join(", ", unresolved));
            }
        }

        public bool IsBlockRelease => _blockRelease;

        public bool ShouldFilter(int vk, int message, uint flags = 0, uint scanCode = 0, UIntPtr extraInfo = default(UIntPtr))
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KeyboardDebounceFilterCore));

            if (extraInfo == InjectedMarker || (flags & LlkhfInjected) != 0)
            {
                return false;
            }

            if (vk < 0 || vk >= 256)
            {
                return false;
            }

            if (_burstBypass)
            {
                UpdateBurstState(message);
            }

            if (_excludedKeys[vk])
            {
                return false;
            }

            return _blockRelease
                ? HandleBlockRelease(vk, message, flags, scanCode)
                : HandleBlockRepress(vk, message);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_sync)
            {
                for (var i = 0; i < _releaseTimers.Length; i++)
                {
                    _releaseTimers[i]?.Dispose();
                    _releaseTimers[i] = null;
                    _pendingUp[i] = false;
                }
            }
        }

        private void UpdateBurstState(int message)
        {
            if (message != WmKeyDown && message != WmSysKeyDown)
            {
                return;
            }

            long now = _getTimestamp();
            long gap = now - _lastDownTicks;

            if (_lastDownTicks != 0 && gap < _burstGapTicks)
            {
                if (_rapidStreak < BurstMinStreak)
                {
                    _rapidStreak++;
                }
            }
            else
            {
                _rapidStreak = 0;
            }

            _lastDownTicks = now;
            _inBurst = _rapidStreak >= BurstMinStreak;
        }

        private bool BurstActive() => _burstBypass && _inBurst;

        private bool HandleBlockRepress(int vk, int message)
        {
            var now = _getTimestamp();

            if (message == WmKeyDown || message == WmSysKeyDown)
            {
                if (!_isPressed[vk] && (now - _lastUpTicks[vk]) < _thresholdTicksByVk[vk] && !BurstActive())
                {
                    _logFiltered(vk, "filtered");
                    _swallowNextUp[vk] = true;
                    return true;
                }

                _isPressed[vk] = true;
                _swallowNextUp[vk] = false;
            }
            else if (message == WmKeyUp || message == WmSysKeyUp)
            {
                if (_swallowNextUp[vk])
                {
                    _swallowNextUp[vk] = false;
                    return true;
                }

                _lastUpTicks[vk] = now;
                _isPressed[vk] = false;
            }

            return false;
        }

        private bool HandleBlockRelease(int vk, int message, uint flags, uint scanCode)
        {
            lock (_sync)
            {
                if (BurstActive())
                {
                    if (message == WmKeyDown || message == WmSysKeyDown)
                    {
                        if (_pendingUp[vk]) CancelPendingUp(vk);
                        _isPressed[vk] = true;
                    }
                    else if (message == WmKeyUp || message == WmSysKeyUp)
                    {
                        if (_pendingUp[vk]) CancelPendingUp(vk);
                        _isPressed[vk] = false;
                    }

                    return false;
                }

                if (message == WmKeyDown || message == WmSysKeyDown)
                {
                    if (_pendingUp[vk])
                    {
                        CancelPendingUp(vk);
                        _logFiltered(vk, "release-held");
                        return true;
                    }

                    _isPressed[vk] = true;
                    return false;
                }

                if (message == WmKeyUp || message == WmSysKeyUp)
                {
                    if (_pendingUp[vk])
                    {
                        return true;
                    }

                    if (!_isPressed[vk])
                    {
                        return false;
                    }

                    _pendingUp[vk] = true;
                    _pendingExtended[vk] = (flags & LlkhfExtended) != 0;
                    _pendingScan[vk] = scanCode;
                    EnsureTimer(vk).Change(_thresholdMsByVk[vk]);
                    return true;
                }
            }

            return false;
        }

        private IReleaseTimer EnsureTimer(int vk)
        {
            if (_releaseTimers[vk] == null)
            {
                _releaseTimers[vk] = _createTimer(vk, OnReleaseTimer);
            }

            return _releaseTimers[vk];
        }

        private void CancelPendingUp(int vk)
        {
            _pendingUp[vk] = false;
            _releaseTimers[vk]?.Cancel();
        }

        private void OnReleaseTimer(int vk)
        {
            bool extended;
            uint scanCode;

            lock (_sync)
            {
                if (!_pendingUp[vk])
                {
                    return;
                }

                _pendingUp[vk] = false;
                _isPressed[vk] = false;
                extended = _pendingExtended[vk];
                scanCode = _pendingScan[vk];
            }

            _injectKeyUp(vk, extended, scanCode);
        }

        internal interface IReleaseTimer : IDisposable
        {
            void Change(int dueTimeMs);
            void Cancel();
        }

        private sealed class ThreadingReleaseTimer : IReleaseTimer
        {
            private readonly int _vk;
            private readonly Action<int> _callback;
            private readonly System.Threading.Timer _timer;

            public ThreadingReleaseTimer(int vk, Action<int> callback)
            {
                _vk = vk;
                _callback = callback;
                _timer = new System.Threading.Timer(_ => _callback(_vk), null,
                    System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }

            public void Change(int dueTimeMs)
            {
                _timer.Change(dueTimeMs, System.Threading.Timeout.Infinite);
            }

            public void Cancel()
            {
                _timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }

            public void Dispose()
            {
                _timer.Dispose();
            }
        }
    }
}
