using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FingerprintMacroPad;

public partial class MainWindow : Window
{
    private AppConfig Cfg => App.Current.Config;
    private FingerEngine Engine => App.Current.Engine;

    private bool _loading;
    private readonly DispatcherTimer _resetTimer;

    public MainWindow()
    {
        InitializeComponent();
        AppIconImage.Source = IconFactory.LoadFrame(32);   // crisp native 32px frame
        Icon = IconFactory.LoadWindowIcon();               // multi-frame → crisp taskbar/chrome

        _resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _resetTimer.Tick += (_, _) => { _resetTimer.Stop(); IndicatorNumber.Text = "0"; };

        _loading = true;
        SwEnabled.IsChecked = Cfg.Enabled;
        SwExclude.IsChecked = Cfg.ExcludeUnlockFinger;
        SwAutoStart.IsChecked = Cfg.AutoStart;
        UpdateTapLabel();
        UpdateDebLabel();
        UpdateThemeGlyph();

        Pc1.Init(1, "Single tap", Cfg.Patterns[1]);
        Pc2.Init(2, "Double tap", Cfg.Patterns[2]);
        Pc3.Init(3, "Triple tap", Cfg.Patterns[3]);
        Pc4.Init(4, "Quadruple tap", Cfg.Patterns[4]);
        Pc1.Changed += SavePatterns;
        Pc2.Changed += SavePatterns;
        Pc3.Changed += SavePatterns;
        Pc4.Changed += SavePatterns;

        TxtVersion.Text = $"Version {UpdateService.CurrentVersionText}";

        _loading = false;

        Engine.TapProgress += OnTapProgress;
        Engine.StatusChanged += OnStatusChanged;
        App.Current.PatternRan += OnPatternRan;

        UpdateStatus();
    }

    // ── Engine events (marshal to UI) ──────────────────────────────────────────
    private void OnTapProgress(int count) => Dispatcher.BeginInvoke(() =>
    {
        IndicatorNumber.Text = count.ToString();
        Pulse();
        _resetTimer.Stop(); _resetTimer.Start();
    });

    private void OnStatusChanged(string status) => Dispatcher.BeginInvoke(UpdateStatus);

    private void OnPatternRan(int taps)
    {
        string name = taps switch { 1 => "Single tap", 2 => "Double tap", 3 => "Triple tap", 4 => "Quadruple tap", _ => $"{taps} taps" };
        var desc = Cfg.Patterns.TryGetValue(taps, out var a) ? a.Describe() : "Not set";
        TxtLastFired.Text = $"Fired: {name}  ·  {desc}";
    }

    private void UpdateStatus()
    {
        string s = !Cfg.Enabled ? "Off" : Engine.Status;
        TxtStatus.Text = s;
        TxtAdminWarn.Visibility = s.Contains("administrator", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Pulse()
    {
        var fade = new DoubleAnimation(0.55, 0, TimeSpan.FromMilliseconds(480));
        var grow = new DoubleAnimation(0.5, 1.7, TimeSpan.FromMilliseconds(480));
        IndicatorPulse.BeginAnimation(OpacityProperty, fade);
        PulseScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
        PulseScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
    }

    // ── Toggles ─────────────────────────────────────────────────────────────
    private void SwEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Current.SetEnabled(SwEnabled.IsChecked == true);
        UpdateStatus();
    }

    private void SwExclude_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Current.SetExcludeUnlockFinger(SwExclude.IsChecked == true);
    }

    private void SwAutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool want = SwAutoStart.IsChecked == true;
        bool ok = App.Current.SetAutoStart(want);
        if (!ok)
        {
            _loading = true;
            SwAutoStart.IsChecked = !want;   // revert on failure
            _loading = false;
            MessageBox.Show(this, "Couldn't update the Windows startup task.", "Fingerprint Macro Pad");
        }
    }

    private void SavePatterns(object? sender, EventArgs e)
    {
        if (_loading) return;
        Cfg.Save();
    }

    // ── Steppers ────────────────────────────────────────────────────────────
    private void TapMinus_Click(object sender, RoutedEventArgs e) => AdjustTap(-50);
    private void TapPlus_Click(object sender, RoutedEventArgs e) => AdjustTap(+50);
    private void AdjustTap(int delta)
    {
        Cfg.TapWindowMs = Math.Clamp(Cfg.TapWindowMs + delta, 150, 1500);
        Cfg.Save(); UpdateTapLabel();
    }
    private void UpdateTapLabel() => TxtTapWindow.Text = $"{Cfg.TapWindowMs} ms";

    private void DebMinus_Click(object sender, RoutedEventArgs e) => AdjustDeb(-20);
    private void DebPlus_Click(object sender, RoutedEventArgs e) => AdjustDeb(+20);
    private void AdjustDeb(int delta)
    {
        Cfg.DebounceMs = Math.Clamp(Cfg.DebounceMs + delta, 0, 400);
        Cfg.Save(); UpdateDebLabel();
    }
    private void UpdateDebLabel() => TxtDebounce.Text = $"{Cfg.DebounceMs} ms";

    // ── Import / Export ───────────────────────────────────────────────────────
    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export macros",
            Filter = "Macro config (*.json)|*.json",
            FileName = "fingerprint-macros.json"
        };
        if (dlg.ShowDialog(this) == true)
        {
            try { Cfg.ExportTo(dlg.FileName); }
            catch (Exception ex) { MessageBox.Show(this, "Export failed:\n" + ex.Message, "Fingerprint Macro Pad"); }
        }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import macros",
            Filter = "Macro config (*.json)|*.json|All files|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var imported = AppConfig.ImportFrom(dlg.FileName);
            App.Current.ApplyImportedConfig(imported);
            ReloadFromConfig();
            MessageBox.Show(this, "Macros imported.", "Fingerprint Macro Pad");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed:\n" + ex.Message, "Fingerprint Macro Pad");
        }
    }

    private void ReloadFromConfig()
    {
        _loading = true;
        SwEnabled.IsChecked = Cfg.Enabled;
        SwExclude.IsChecked = Cfg.ExcludeUnlockFinger;
        UpdateTapLabel(); UpdateDebLabel(); UpdateThemeGlyph();
        Pc1.Init(1, "Single tap", Cfg.Patterns[1]);
        Pc2.Init(2, "Double tap", Cfg.Patterns[2]);
        Pc3.Init(3, "Triple tap", Cfg.Patterns[3]);
        Pc4.Init(4, "Quadruple tap", Cfg.Patterns[4]);
        _loading = false;
        UpdateStatus();
    }

    // ── Updates ───────────────────────────────────────────────────────────────
    private string? _downloadedExe;

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        TxtUpdateStatus.Text = "Checking for updates…";
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info == null)
            {
                TxtUpdateStatus.Text = $"You're on the latest version ({UpdateService.CurrentVersionText}).";
                return;
            }
            string v = $"{info.Version.Major}.{info.Version.Minor}.{info.Version.Build}";
            var progress = new Progress<double>(p => TxtUpdateStatus.Text = $"Downloading update {v}… {p:P0}");
            _downloadedExe = await UpdateService.DownloadAsync(info, progress);
            TxtUpdateStatus.Text = $"Update {v} downloaded — relaunch to finish installing.";
            BtnCheckUpdate.Visibility = Visibility.Collapsed;
            BtnRelaunch.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = "Update check failed: " + ex.Message;
        }
        finally { BtnCheckUpdate.IsEnabled = true; }
    }

    private void BtnRelaunch_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_downloadedExe) || !System.IO.File.Exists(_downloadedExe))
        {
            TxtUpdateStatus.Text = "Update file missing — check again.";
            BtnRelaunch.Visibility = Visibility.Collapsed;
            BtnCheckUpdate.Visibility = Visibility.Visible;
            return;
        }
        App.Current.RelaunchForUpdate(_downloadedExe);
    }

    // ── Title bar ───────────────────────────────────────────────────────────
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        var next = Cfg.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        App.Current.SetTheme(next);
        UpdateThemeGlyph();
    }

    private void UpdateThemeGlyph() => BtnTheme.Content = Cfg.Theme == AppTheme.Dark ? "" : "";

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close(); // App intercepts → hide to tray
}
