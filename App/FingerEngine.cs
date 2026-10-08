namespace FingerprintMacroPad;

/// <summary>
/// Watches the fingerprint sensor and turns quick taps into 1/2/3/4-tap patterns.
/// Read-only: it identifies "your enrolled finger vs. any other finger" and never
/// stores, adds, or removes anything. Runs on a background thread; events are raised
/// off the UI thread, so subscribers must marshal to the UI as needed.
/// </summary>
public sealed class FingerEngine : IDisposable
{
    private readonly AppConfig _config;

    private Thread? _thread;
    private uint _session;
    private volatile bool _running;
    private volatile bool _stopRequested;

    private readonly object _tapLock = new();
    private int _tapCount;
    private DateTime _lastTapUtc = DateTime.MinValue;
    private System.Threading.Timer? _flushTimer;

    // Display power state (set by the app from power-broadcast events). When the
    // screen is off, a touch should wake it rather than fire a macro.
    private volatile bool _displayOff;
    private DateTime _wakeGraceUntil = DateTime.MinValue;
    public bool DisplayOff => _displayOff;

    /// <summary>Called when the monitor turns on/off. Cancels the in-flight sensor
    /// operation so the loop immediately switches between the fast wake path (screen
    /// off) and the normal Identify path (screen on) instead of waiting for the next
    /// touch to return.</summary>
    public void SetDisplayOff(bool off)
    {
        if (_displayOff == off) return;
        _displayOff = off;
        Reevaluate();
    }

    /// <summary>Cancels the in-flight sensor op so the loop re-picks its fast/match path
    /// immediately after a relevant setting (exclude-unlock, wake-on-tap) changes.</summary>
    public void Reevaluate()
    {
        try { if (_running && _session != 0) WinBio.Cancel(_session); } catch { }
    }

    /// <summary>Fired as each tap lands, with the running count in the current burst.</summary>
    public event Action<int>? TapProgress;
    /// <summary>Fired once a burst completes, with the final tap count (the pattern).</summary>
    public event Action<int>? PatternFired;
    /// <summary>Human-readable status: "Listening", "Paused", "Needs administrator", etc.</summary>
    public event Action<string>? StatusChanged;

    public bool IsRunning => _running;
    public string Status { get; private set; } = "Stopped";

    public FingerEngine(AppConfig config) => _config = config;

    public void Start()
    {
        if (_running || (_thread?.IsAlive ?? false)) return;
        _stopRequested = false;
        _thread = new Thread(Loop) { IsBackground = true, Name = "FingerEngine" };
        _thread.Start();
    }

    public void Stop()
    {
        _stopRequested = true;
        try { if (_session != 0) WinBio.Cancel(_session); } catch { }
        _thread = null;
    }

    private void SetStatus(string s)
    {
        Status = s;
        StatusChanged?.Invoke(s);
    }

    private void Loop()
    {
        int hr = WinBio.OpenSession(
            WinBio.WINBIO_TYPE_FINGERPRINT, WinBio.WINBIO_POOL_SYSTEM, WinBio.WINBIO_FLAG_DEFAULT,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _session);

        if (hr != WinBio.S_OK)
        {
            _running = false;
            SetStatus((uint)hr == WinBio.E_ACCESSDENIED ? "Needs administrator" : $"Sensor error 0x{(uint)hr:X8}");
            return;
        }

        _running = true;
        _flushTimer = new System.Threading.Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        SetStatus("Listening");

        while (!_stopRequested)
        {
            bool wakeMode = _displayOff && _config.WakeScreenOnTap;

            // FAST path (touch-only, no fingerprint matching) whenever we don't need to
            // tell the unlock finger apart: i.e. waking the screen, or any-finger macro
            // mode. Identify's matching step is the lag, so we skip it here.
            if (wakeMode || !_config.ExcludeUnlockFinger)
            {
                int lr = WinBio.LocateSensor(_session, out _);
                if (_stopRequested) break;
                uint ulr = (uint)lr;
                if (ulr == WinBio.E_ACCESSDENIED) { SetStatus("Needs administrator"); break; }
                if (lr != WinBio.S_OK) continue;    // no touch, or cancelled by a mode change

                if (wakeMode)                        // screen asleep -> wake, not a macro
                {
                    DisplayWaker.Wake();
                    _displayOff = false;
                    _wakeGraceUntil = DateTime.UtcNow.AddMilliseconds(1200);
                    continue;
                }
                if (DateTime.UtcNow < _wakeGraceUntil) continue;  // trailing touches of a wake
                RegisterTap();
                continue;
            }

            // MATCH path: "ignore my unlock finger" is on, so identify the finger.
            hr = WinBio.Identify(_session, out _, out _, out _, out _);
            if (_stopRequested) break;
            uint uhr = (uint)hr;
            if (uhr == WinBio.E_ACCESSDENIED) { SetStatus("Needs administrator"); break; }

            bool touched = hr == WinBio.S_OK || uhr == WinBio.E_UNKNOWN_ID || uhr == WinBio.E_BAD_CAPTURE;
            if (!touched) continue;

            if (HandleWakeIfDisplayOff()) continue;   // display slept mid-identify, or grace

            if (uhr == WinBio.E_UNKNOWN_ID) RegisterTap();   // only non-enrolled fingers fire
        }

        _flushTimer?.Dispose(); _flushTimer = null;
        try { if (_session != 0) WinBio.CloseSession(_session); } catch { }
        _session = 0;
        _running = false;
        SetStatus("Paused");
    }

    /// <summary>If the display is off (PC unlocked), wake it and swallow this touch
    /// plus the brief burst after. Returns true if the touch was consumed for waking.
    /// Runs for ANY finger — independent of the exclude-unlock-finger macro filter.</summary>
    private bool HandleWakeIfDisplayOff()
    {
        if (!_config.WakeScreenOnTap) return false;
        var now = DateTime.UtcNow;
        if (_displayOff)
        {
            DisplayWaker.Wake();
            _displayOff = false;                           // optimistic; power event confirms
            _wakeGraceUntil = now.AddMilliseconds(1200);
            return true;
        }
        return now < _wakeGraceUntil;                      // trailing touches of the wake
    }

    /// <summary>Highest tap count that has an action assigned (0 if none). Once a burst
    /// reaches it there's nothing higher to wait for, so we can fire immediately.</summary>
    private int MaxActivePattern()
    {
        int max = 0;
        var patterns = _config.Patterns;
        if (patterns != null)
            for (int n = 1; n <= 4; n++)
                if (patterns.TryGetValue(n, out var a) && a != null && a.Type != ActionType.None)
                    max = n;
        return max;
    }

    private void RegisterTap()
    {
        int count;
        bool fireNow = false;
        var now = DateTime.UtcNow;
        lock (_tapLock)
        {
            // Debounce: ignore sensor re-fires from a lingering press so a slightly
            // long touch doesn't inflate into a double/triple.
            if ((now - _lastTapUtc).TotalMilliseconds < _config.DebounceMs) return;
            _lastTapUtc = now;
            count = ++_tapCount;

            // If we've reached the highest macro the user actually assigned, there's
            // nothing longer to wait for — fire right away instead of the full window.
            int max = MaxActivePattern();
            if (max > 0 && count >= max)
            {
                _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                fireNow = true;
            }
            else
            {
                _flushTimer?.Change(_config.TapWindowMs, Timeout.Infinite);
            }
        }
        TapProgress?.Invoke(count);
        if (fireNow) Flush();
    }

    private void Flush()
    {
        int count;
        lock (_tapLock) { count = _tapCount; _tapCount = 0; }
        if (count > 0) PatternFired?.Invoke(count);
    }

    public void Dispose() => Stop();
}
