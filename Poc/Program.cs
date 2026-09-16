using System.Runtime.InteropServices;

// ─────────────────────────────────────────────────────────────────────────────
//  Fingerprint Macro Pad — Proof of Concept
//
//  PURPOSE: prove that the Windows Biometric Framework reports WHICH finger
//  touched your ELAN sensor (VID_04F3 / PID_0C3D). That per-finger signal is
//  what a macro pad would map to actions.
//
//  SAFETY — READ THIS:
//  This program uses ONLY read-only biometric calls:
//      WinBioOpenSession, WinBioIdentify, WinBioCancel, WinBioCloseSession.
//  There is deliberately NO call to WinBioEnrollBegin, WinBioDeleteTemplate,
//  WinBioEnrollCommit, or any other function that could add, change, or REMOVE
//  a fingerprint. Running this — with any finger — cannot delete or alter your
//  Windows Hello unlock finger or any other enrolled finger. It only looks.
// ─────────────────────────────────────────────────────────────────────────────

internal static class Program
{
    // WinBio constants
    private const uint WINBIO_TYPE_FINGERPRINT = 0x00000008;
    private const uint WINBIO_POOL_SYSTEM      = 0x00000001;
    private const uint WINBIO_FLAG_DEFAULT     = 0x00000000;

    private const int  S_OK                = 0;
    private const uint WINBIO_E_UNKNOWN_ID = 0x80098005; // touch detected, no matching enrolled finger
    private const uint E_ACCESSDENIED      = 0x80070005;

    // WINBIO_IDENTITY is a 76-byte struct (type + a union up to a SID). We never
    // decode it in the POC, so we just reserve the space and ignore the contents.
    [StructLayout(LayoutKind.Sequential, Size = 76)]
    private struct WINBIO_IDENTITY
    {
        public uint Type;
    }

    [DllImport("winbio.dll", EntryPoint = "WinBioOpenSession")]
    private static extern int WinBioOpenSession(
        uint factor, uint poolType, uint flags,
        IntPtr unitArray, IntPtr unitCount, IntPtr databaseId,
        out uint sessionHandle);

    [DllImport("winbio.dll", EntryPoint = "WinBioCloseSession")]
    private static extern int WinBioCloseSession(uint sessionHandle);

    [DllImport("winbio.dll", EntryPoint = "WinBioCancel")]
    private static extern int WinBioCancel(uint sessionHandle);

    [DllImport("winbio.dll", EntryPoint = "WinBioIdentify")]
    private static extern int WinBioIdentify(
        uint sessionHandle,
        out uint unitId,
        out WINBIO_IDENTITY identity,
        out byte subFactor,
        out uint rejectDetail);

    // Diagnostic-only, still read-only: LocateSensor just detects THAT a finger
    // touched and returns the unit id. No image, no template, no match, no storage.
    [DllImport("winbio.dll", EntryPoint = "WinBioLocateSensor")]
    private static extern int WinBioLocateSensor(uint sessionHandle, out uint unitId);

    // Focus arbitration: ask the biometric service to route sensor input to us.
    [DllImport("winbio.dll", EntryPoint = "WinBioAcquireFocus")]
    private static extern int WinBioAcquireFocus();

    [DllImport("winbio.dll", EntryPoint = "WinBioReleaseFocus")]
    private static extern int WinBioReleaseFocus();

    private static uint _session;
    private static volatile bool _stop;
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "fingerscan_diag.log");
    private static readonly object _logLock = new();

    private static void Log(string line)
    {
        Console.WriteLine(line);
        try
        {
            lock (_logLock)
                File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { /* logging is best-effort */ }
    }

    private static string FingerName(byte sub) => sub switch
    {
        0x00 => "(no finger info)",
        0x01 => "Right THUMB",
        0x02 => "Right INDEX",
        0x03 => "Right MIDDLE",
        0x04 => "Right RING",
        0x05 => "Right LITTLE",
        0x06 => "Left THUMB",
        0x07 => "Left INDEX",
        0x08 => "Left MIDDLE",
        0x09 => "Left RING",
        0x0A => "Left LITTLE",
        0xFF => "(any)",
        _    => $"(subtype 0x{sub:X2})"
    };

    private static int Main(string[] args)
    {
        // Modes: "taps" (default, tap-pattern detector), "locate" (any touch),
        // "identify" (which finger, diagnostic).
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "taps";
        bool identifyMode = mode == "identify";
        bool tapsMode     = mode == "taps";
        bool holdMode     = mode == "hold";
        int  tapWindowMs  = (args.Length > 1 && int.TryParse(args[1], out int w)) ? w : 500;
        int  runSeconds   = tapsMode ? 90 : (holdMode ? 60 : 30);

        try { File.WriteAllText(LogPath, ""); } catch { }

        bool elevated = IsElevated();
        Log("Fingerprint Macro Pad - Proof of Concept  (READ-ONLY)");
        Log($"Mode: {mode}   Elevated: {elevated}" + (tapsMode ? $"   Tap window: {tapWindowMs} ms" : ""));
        Log("Never enrolls, deletes, or stores anything.");
        Log("");

        int hr = WinBioOpenSession(
            WINBIO_TYPE_FINGERPRINT, WINBIO_POOL_SYSTEM, WINBIO_FLAG_DEFAULT,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _session);
        Log($"WinBioOpenSession -> 0x{(uint)hr:X8} (session {_session})");

        if (hr != S_OK)
        {
            if ((uint)hr == E_ACCESSDENIED)
                Log("-> Access denied. Run this from an ELEVATED (Administrator) terminal.");
            return hr;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;                          // don't hard-kill
            _stop = true;
            if (_session != 0) WinBioCancel(_session); // unblock a pending call
        };

        // Watchdog: auto-exit so an elevated instance cleans itself up.
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(runSeconds * 1000);
            if (!_stop) { _stop = true; if (_session != 0) WinBioCancel(_session); }
        }) { IsBackground = true };
        watchdog.Start();

        if (tapsMode)
        {
            RunTapDetector(tapWindowMs, runSeconds);
            WinBioCloseSession(_session);
            Log("");
            Log("Session closed. No fingerprints were stored, added, or removed.");
            return 0;
        }

        if (holdMode)
        {
            RunHoldProbe();
            WinBioCloseSession(_session);
            Log("");
            Log("Session closed. No fingerprints were stored, added, or removed.");
            return 0;
        }

        Log("");
        Log($"Ready. Touch the sensor. Auto-exits in {runSeconds}s (or Ctrl+C).");
        Log("");

        while (!_stop)
        {
            uint uhr;
            string stamp;

            if (identifyMode)
            {
                hr = WinBioIdentify(_session, out uint unitId, out _, out byte subFactor, out uint reject);
                uhr = (uint)hr;
                if (_stop) break;
                stamp = DateTime.Now.ToString("HH:mm:ss");

                if (hr == S_OK)
                    Log($"[{stamp}]  {FingerName(subFactor)}   (unit {unitId}, subtype 0x{subFactor:X2})");
                else if (uhr == WINBIO_E_UNKNOWN_ID)
                    Log($"[{stamp}]  Touch detected - finger NOT recognized (not enrolled).");
                else
                    Log($"[{stamp}]  Identify -> 0x{uhr:X8}  (reject {reject})");
            }
            else
            {
                hr = WinBioLocateSensor(_session, out uint unitId);
                uhr = (uint)hr;
                if (_stop) break;
                stamp = DateTime.Now.ToString("HH:mm:ss");

                if (hr == S_OK)
                    Log($"[{stamp}]  TOUCH detected on unit {unitId}.");
                else
                    Log($"[{stamp}]  LocateSensor -> 0x{uhr:X8}");
            }

            if (uhr == E_ACCESSDENIED) { Log("-> Access denied. Try running as Administrator."); break; }
        }

        if (_session != 0) WinBioCloseSession(_session);
        Log("");
        Log("Session closed. No fingerprints were stored, added, or removed.");
        return 0;
    }

    // Tap-pattern detector: groups quick touches into single/double/triple taps.
    // Uses LocateSensor (any finger, fast). Read-only: no identity, no storage.
    private static void RunTapDetector(int windowMs, int runSeconds)
    {
        var gate = new object();
        int tapCount = 0;

        // When taps stop for windowMs, emit the pattern (what a macro would fire on).
        var flushTimer = new Timer(_ =>
        {
            int c;
            lock (gate) { c = tapCount; tapCount = 0; }
            if (c <= 0) return;
            string label = c switch
            {
                1 => "SINGLE TAP",
                2 => "DOUBLE TAP",
                3 => "TRIPLE TAP",
                _ => $"{c}x TAP"
            };
            Log($"[{DateTime.Now:HH:mm:ss}]  >>> {label}  ->  would run macro #{c}");
        }, null, Timeout.Infinite, Timeout.Infinite);

        Log("");
        Log($"Ready. Tap with ANY finger.  1/2/3 taps = 3 different macros.");
        Log($"(taps grouped within {windowMs} ms; auto-exits in {runSeconds}s or Ctrl+C)");
        Log("");

        while (!_stop)
        {
            int hr = WinBioLocateSensor(_session, out uint unitId);
            if (_stop) break;
            uint uhr = (uint)hr;

            if (hr == S_OK)
            {
                int n;
                lock (gate) { n = ++tapCount; }
                flushTimer.Change(windowMs, Timeout.Infinite); // (re)arm the gap timer
                Log($"[{DateTime.Now:HH:mm:ss}]  tap #{n} (unit {unitId})");
            }
            else if (uhr == E_ACCESSDENIED)
            {
                Log("-> Access denied. Run as Administrator."); break;
            }
            // other transient results (bad capture, etc.) are ignored
        }

        flushTimer.Dispose();
    }

    // Hold probe: logs each touch event with the millisecond gap since the last,
    // to reveal whether the sensor STREAMS events while a finger is held (=> hold
    // is detectable) or fires just once per touch (=> use quad-tap instead).
    private static void RunHoldProbe()
    {
        Log("");
        Log("HOLD PROBE. Do these in order, watching the gaps:");
        Log("  1) PRESS AND HOLD one finger flat for ~4 seconds, then lift.");
        Log("  2) Wait a moment, then do 3 quick separate taps.");
        Log("Small repeating gaps during the hold = hold IS detectable.");
        Log("");

        DateTime? last = null;
        int n = 0;
        while (!_stop)
        {
            int hr = WinBioLocateSensor(_session, out uint unitId);
            if (_stop) break;
            uint uhr = (uint)hr;
            DateTime now = DateTime.Now;

            if (hr == S_OK)
            {
                n++;
                string gap = last is null ? "  (first)" : $"gap={(now - last.Value).TotalMilliseconds,7:F0} ms";
                Log($"event #{n,-3} {now:HH:mm:ss.fff}  {gap}");
                last = now;
            }
            else if (uhr == E_ACCESSDENIED)
            {
                Log("-> Access denied. Run as Administrator."); break;
            }
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var p = new System.Security.Principal.WindowsPrincipal(id);
            return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
