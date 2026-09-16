using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FingerprintMacroPad;

public partial class PatternCard : UserControl
{
    private bool _loading = true;   // stays true until Init() finishes, so combo
                                    // SelectionChanged during XAML parse is ignored
    public MacroAction Action { get; private set; } = new();
    public event EventHandler? Changed;

    public PatternCard() => InitializeComponent();

    public void Init(int number, string label, MacroAction action)
    {
        _loading = true;
        Action = action;
        LblPattern.Text = label;

        DotsPanel.Children.Clear();
        for (int i = 0; i < number; i++)
        {
            DotsPanel.Children.Add(new Ellipse
            {
                Width = 11, Height = 11, Margin = new Thickness(0, 0, 5, 0),
                Fill = (Brush)FindResource("Accent")
            });
        }

        CmbAction.SelectedIndex = (int)action.Type;
        TxtTarget.Text = action.Target;
        TxtArgs.Text = action.Arguments;
        TxtHotkey.Text = string.IsNullOrWhiteSpace(action.Hotkey) ? "Click and press keys…" : action.Hotkey;
        CmbMedia.SelectedIndex = (int)action.Media;
        TxtScript.Text = action.ScriptBody;
        CmbLang.SelectedIndex = (int)action.ScriptLang;

        UpdateVisibility();
        UpdateSummary();
        _loading = false;

        CmbAction.SelectionChanged -= CmbAction_Changed;
        CmbAction.SelectionChanged += CmbAction_Changed;
    }

    private void CmbAction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        Action.Type = (ActionType)Math.Max(0, CmbAction.SelectedIndex);
        UpdateVisibility();
        Commit();
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Action.Target = TxtTarget.Text.Trim();
        Action.Arguments = TxtArgs.Text.Trim();
        Action.Media = (MediaKeyKind)Math.Max(0, CmbMedia.SelectedIndex);
        Action.ScriptBody = TxtScript.Text;
        Action.ScriptLang = (ScriptLanguage)Math.Max(0, CmbLang.SelectedIndex);
        Commit();
    }

    private void TxtHotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifier(key)) return; // wait for the real key

        var parts = new List<string>();
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyToken(key));

        string combo = string.Join("+", parts);
        Action.Hotkey = combo;
        TxtHotkey.Text = combo;
        Commit();
    }

    private void BtnClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        Action.Hotkey = "";
        TxtHotkey.Text = "Click and press keys…";
        Commit();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a target",
            Filter = "Programs & scripts|*.exe;*.lnk;*.bat;*.cmd;*.ps1|All files|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true)
            TxtTarget.Text = dlg.FileName;   // triggers Field_Changed
    }

    private void UpdateVisibility()
    {
        PanTarget.Visibility = Visibility.Collapsed;
        PanArgs.Visibility = Visibility.Collapsed;
        PanHotkey.Visibility = Visibility.Collapsed;
        PanMedia.Visibility = Visibility.Collapsed;

        switch (Action.Type)
        {
            case ActionType.LaunchApp:
                LblTargetHint.Text = "APPLICATION";
                PanTarget.Visibility = Visibility.Visible;
                PanArgs.Visibility = Visibility.Visible;
                break;
            case ActionType.OpenPath:
                LblTargetHint.Text = "FILE, FOLDER OR URL";
                PanTarget.Visibility = Visibility.Visible;
                break;
            case ActionType.RunScript:
                LblTargetHint.Text = "SCRIPT  (.PS1 / .BAT / .CMD / .EXE)";
                PanTarget.Visibility = Visibility.Visible;
                PanArgs.Visibility = Visibility.Visible;
                break;
            case ActionType.Hotkey:
                PanHotkey.Visibility = Visibility.Visible;
                break;
            case ActionType.MediaKey:
                PanMedia.Visibility = Visibility.Visible;
                break;
            case ActionType.InlineScript:
                PanScript.Visibility = Visibility.Visible;
                break;
        }
    }

    private void TxtScript_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TxtScript_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        string path = files[0];
        try
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".bat" or ".cmd") CmbLang.SelectedIndex = (int)ScriptLanguage.Batch;
            else if (ext == ".ps1") CmbLang.SelectedIndex = (int)ScriptLanguage.PowerShell;
            TxtScript.Text = File.ReadAllText(path);   // triggers Field_Changed
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't read that file:\n" + ex.Message, "Fingerprint Macro Pad");
        }
        e.Handled = true;
    }

    private void UpdateSummary() => TxtSummary.Text = Action.Describe();

    private void Commit()
    {
        if (_loading) return;
        UpdateSummary();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift
        or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    private static string KeyToken(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return k.ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return ((int)(k - Key.NumPad0)).ToString();
        if (k >= Key.F1 && k <= Key.F24) return k.ToString();
        return k switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Escape => "Esc",
            Key.Left => "Left", Key.Right => "Right", Key.Up => "Up", Key.Down => "Down",
            Key.Home => "Home", Key.End => "End",
            Key.PageUp => "PageUp", Key.PageDown => "PageDown",
            Key.Insert => "Insert", Key.Delete => "Delete", Key.Back => "Backspace",
            _ => k.ToString()
        };
    }
}
