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
    public bool DisplayOff { get => _displayOff; set => _displayOff = value; }

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
            hr = WinBio.Identify(_session, out _, out _, out _, out _);
            if (_stopRequested) break;
            uint uhr = (uint)hr;

            bool isTap;
            if (hr == WinBio.S_OK)
                isTap = !_config.ExcludeUnlockFinger;          // the enrolled (unlock) finger
            else if (uhr == WinBio.E_UNKNOWN_ID)
                isTap = true;                                   // some other finger
            else if (uhr == WinBio.E_BAD_CAPTURE)
                isTap = !_config.ExcludeUnlockFinger;           // ambiguous read; count only in any-finger mode
            else if (uhr == WinBio.E_ACCESSDENIED)
            { SetStatus("Needs administrator"); break; }
            else
                isTap = false;

            if (isTap) RegisterTap();
        }

        _flushTimer?.Dispose(); _flushTimer = null;
        try { if (_session != 0) WinBio.CloseSession(_session); } catch { }
        _session = 0;
        _running = false;
        SetStatus("Paused");
    }

    private void RegisterTap()
    {
        int count;
        var now = DateTime.UtcNow;

        // If the display is off (but the PC is unlocked), a touch should just wake
        // the screen — not fire a macro. Swallow that touch and the brief burst after.
        if (_config.WakeScreenOnTap)
        {
            if (_displayOff)
            {
                DisplayWaker.Wake();
                _displayOff = false;                       // optimistic; power event confirms
                _wakeGraceUntil = now.AddMilliseconds(1200);
                return;
            }
            if (now < _wakeGraceUntil) return;             // trailing touches of the wake tap
        }

        lock (_tapLock)
        {
            // Debounce: ignore sensor re-fires from a lingering press so a slightly
            // long touch doesn't inflate into a double/triple.
            if ((now - _lastTapUtc).TotalMilliseconds < _config.DebounceMs) return;
            _lastTapUtc = now;
            count = ++_tapCount;
            _flushTimer?.Change(_config.TapWindowMs, Timeout.Infinite);
        }
        TapProgress?.Invoke(count);
    }

    private void Flush()
    {
        int count;
        lock (_tapLock) { count = _tapCount; _tapCount = 0; }
        if (count > 0) PatternFired?.Invoke(count);
    }

    public void Dispose() => Stop();
}
