using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FingerprintMacroPad;

public enum ActionType { None, LaunchApp, OpenPath, Hotkey, MediaKey, RunScript, InlineScript }

public enum MediaKeyKind { PlayPause, Next, Previous, Stop, VolumeUp, VolumeDown, Mute }

public enum ScriptLanguage { PowerShell, Batch }

public enum AppTheme { Dark, Light }

/// <summary>One configurable action bound to a tap pattern.</summary>
public sealed class MacroAction
{
    public ActionType Type { get; set; } = ActionType.None;
    public string Target { get; set; } = "";     // app path / file / url / script path
    public string Arguments { get; set; } = "";
    public string Hotkey { get; set; } = "";      // e.g. "Ctrl+Shift+M"
    public MediaKeyKind Media { get; set; } = MediaKeyKind.PlayPause;
    public string ScriptBody { get; set; } = "";  // inline pasted / dropped script
    public ScriptLanguage ScriptLang { get; set; } = ScriptLanguage.PowerShell;

    public MacroAction Clone() => new()
    {
        Type = Type, Target = Target, Arguments = Arguments, Hotkey = Hotkey, Media = Media,
        ScriptBody = ScriptBody, ScriptLang = ScriptLang
    };

    public string Describe() => Type switch
    {
        ActionType.None => "Not set",
        ActionType.LaunchApp => $"Launch  {ShortTarget()}",
        ActionType.OpenPath => $"Open  {ShortTarget()}",
        ActionType.Hotkey => string.IsNullOrWhiteSpace(Hotkey) ? "Hotkey (unset)" : $"Hotkey  {Hotkey}",
        ActionType.MediaKey => $"Media  {Media}",
        ActionType.RunScript => $"Run  {ShortTarget()}",
        ActionType.InlineScript => string.IsNullOrWhiteSpace(ScriptBody)
            ? $"{ScriptLang} script (empty)" : $"Run {ScriptLang} script",
        _ => "—"
    };

    private string ShortTarget()
    {
        if (string.IsNullOrWhiteSpace(Target)) return "(unset)";
        try { var n = Path.GetFileName(Target.TrimEnd('\\', '/')); return string.IsNullOrEmpty(n) ? Target : n; }
        catch { return Target; }
    }
}

public sealed class AppConfig
{
    public bool Enabled { get; set; } = true;
    public bool ExcludeUnlockFinger { get; set; } = false;
    public int TapWindowMs { get; set; } = 500;
    public int DebounceMs { get; set; } = 120;
    public bool AutoStart { get; set; } = false;
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Keyed by tap count (1..4).</summary>
    public Dictionary<int, MacroAction> Patterns { get; set; } = new()
    {
        [1] = new(), [2] = new(), [3] = new(), [4] = new()
    };

    [JsonIgnore] public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FingerprintMacroPad");
    [JsonIgnore] public static string FilePath => Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options);
                if (cfg != null) { cfg.EnsurePatterns(); return cfg; }
            }
        }
        catch { /* fall through to defaults */ }
        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Import a config from a file, keeping it valid.</summary>
    public static AppConfig ImportFrom(string path)
    {
        var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options)
                  ?? throw new InvalidDataException("File did not contain a valid configuration.");
        cfg.EnsurePatterns();
        return cfg;
    }

    public void ExportTo(string path) => File.WriteAllText(path, ToJson());

    public void EnsurePatterns()
    {
        Patterns ??= new();
        for (int i = 1; i <= 4; i++)
            if (!Patterns.ContainsKey(i) || Patterns[i] == null)
                Patterns[i] = new MacroAction();
        if (TapWindowMs < 150) TapWindowMs = 150;
        if (TapWindowMs > 1500) TapWindowMs = 1500;
        if (DebounceMs < 0) DebounceMs = 0;
        if (DebounceMs > 400) DebounceMs = 400;
    }
}
