using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FingerprintMacroPad;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public AppConfig Config { get; private set; } = null!;
    public FingerEngine Engine { get; private set; } = null!;

    private Forms.NotifyIcon _tray = null!;
    private MainWindow? _window;
    private System.Threading.Mutex? _mutex;
    private System.Threading.EventWaitHandle? _showEvent;
    private System.Threading.RegisteredWaitHandle? _showReg;
    private DisplayMonitor? _displayMonitor;
    private bool _locked;
    private bool _shuttingDown;

    /// <summary>Fires (on the UI thread) when a completed tap pattern runs a macro.</summary>
    public event Action<int>? PatternRan;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, ev) => LogFatal(ev.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, ev) => LogFatal(ev.Exception);

        _mutex = new System.Threading.Mutex(true, "FingerprintMacroPad_singleton", out bool isNew);
        _showEvent = new System.Threading.EventWaitHandle(
            false, System.Threading.EventResetMode.AutoReset, "FingerprintMacroPad_show");
        if (!isNew)
        {
            // Already running: ask that instance to open Settings, then exit.
            try { _showEvent.Set(); } catch { }
            Shutdown();
            return;
        }
        // First instance: when a later launch signals us, show the Settings window.
        _showReg = System.Threading.ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => Dispatcher.BeginInvoke(new Action(ShowSettings)), null, -1, false);

        Config = AppConfig.Load();
        ThemeManager.Apply(Config.Theme);
        ScriptRunner.CleanupOld();

        Engine = new FingerEngine(Config);
        Engine.PatternFired += OnPatternFired;

        // Track display on/off so a touch while the screen is asleep wakes it
        // instead of firing a macro.
        _displayMonitor = new DisplayMonitor();
        _displayMonitor.DisplayOffChanged += off => Engine.SetDisplayOff(off);

        SystemEvents.SessionSwitch += OnSessionSwitch;

        BuildTray();

        if (Config.Enabled && !_locked)
            Engine.Start();

        bool startHidden = e.Args.Any(a => a.Equals("/tray", StringComparison.OrdinalIgnoreCase));
        if (!startHidden)
            ShowSettings();
    }

    // ── Macro firing ──────────────────────────────────────────────────────────
    private void OnPatternFired(int taps)
    {
        // Engine raises this on a background thread.
        Dispatcher.BeginInvoke(() =>
        {
            if (!Config.Enabled) return;
            if (Config.Patterns.TryGetValue(taps, out var action))
                System.Threading.Tasks.Task.Run(() => MacroRunner.Run(action));
            PatternRan?.Invoke(taps);
        });
    }

    // ── Lock / unlock: pause the sensor so we never fight the Hello unlock screen ─
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect)
        {
            _locked = true;
            Engine.Stop();
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
        {
            _locked = false;
            if (Config.Enabled) Engine.Start();
        }
    }

    // ── Enabled / settings applied from the window or tray ─────────────────────
    public void SetEnabled(bool enabled)
    {
        Config.Enabled = enabled;
        Config.Save();
        if (enabled && !_locked) Engine.Start();
        else Engine.Stop();
        RefreshTray();
    }

    public void SetTheme(AppTheme theme)
    {
        Config.Theme = theme;
        Config.Save();
        ThemeManager.Apply(theme);
    }

    public void SetExcludeUnlockFinger(bool value)
    {
        Config.ExcludeUnlockFinger = value;   // read live by the engine, no restart needed
        Config.Save();
        RefreshTray();
    }

    public void SetWakeScreenOnTap(bool value)
    {
        Config.WakeScreenOnTap = value;       // read live by the engine
        Config.Save();
    }

    public void ApplyImportedConfig(AppConfig imported)
    {
        // Copy fields into the live Config so the engine (which holds this instance) sees them.
        Config.ExcludeUnlockFinger = imported.ExcludeUnlockFinger;
        Config.WakeScreenOnTap = imported.WakeScreenOnTap;
        Config.TapWindowMs = imported.TapWindowMs;
        Config.DebounceMs = imported.DebounceMs;
        Config.Theme = imported.Theme;
        Config.Patterns = imported.Patterns;
        Config.Enabled = imported.Enabled;
        Config.EnsurePatterns();
        Config.Save();

        ThemeManager.Apply(Config.Theme);
        if (Config.Enabled && !_locked) Engine.Start(); else Engine.Stop();
        RefreshTray();
    }

    public bool SetAutoStart(bool value)
    {
        bool ok = value ? AutoStart.Enable() : AutoStart.Disable();
        if (ok) { Config.AutoStart = value; Config.Save(); }
        RefreshTray();
        return ok;
    }

    // ── Tray ──────────────────────────────────────────────────────────────────
    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();

        // Tray is for actions only (open / exit) — all settings live in the window.
        var miOpen = new Forms.ToolStripMenuItem("Open settings", null, (_, _) => ShowSettings())
        { Font = new System.Drawing.Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold) };
        menu.Items.Add(miOpen);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));

        menu.Opening += (_, _) => RefreshTray();

        _tray = new Forms.NotifyIcon
        {
            Icon = IconFactory.CreateTrayIcon(),
            Text = "Fingerprint Macro Pad",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        // reflect autostart reality on launch
        Config.AutoStart = AutoStart.IsEnabled();
        RefreshTray();
    }

    private void RefreshTray()
    {
        if (_tray != null)
            _tray.Text = $"Fingerprint Macro Pad — {(Config.Enabled ? Engine.Status : "Off")}";
    }

    public void ShowSettings()
    {
        if (_window == null)
        {
            _window = new MainWindow();
            _window.Closing += (s, args) =>
            {
                if (_shuttingDown) return;
                args.Cancel = true;   // hide to tray instead of exiting
                _window.Hide();
            };
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true; _window.Topmost = false;
    }

    /// <summary>Swaps in the downloaded exe and relaunches the app.</summary>
    public void RelaunchForUpdate(string newExePath)
    {
        _shuttingDown = true;
        try { UpdateService.StartSwap(newExePath); } catch { }
        try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
        try { _showReg?.Unregister(null); _showEvent?.Dispose(); } catch { }
        try { _displayMonitor?.Dispose(); } catch { }
        try { Engine.Dispose(); } catch { }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    private void ExitApp()
    {
        _shuttingDown = true;
        try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
        try { _showReg?.Unregister(null); _showEvent?.Dispose(); } catch { }
        try { _displayMonitor?.Dispose(); } catch { }
        Engine.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    private static void LogFatal(Exception? ex)
    {
        try
        {
            var dir = AppConfig.Dir;
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "error.log"),
                $"[{DateTime.Now:s}] {ex}\n\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
