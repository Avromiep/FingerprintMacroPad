using System.IO;

namespace FingerprintMacroPad;

/// <summary>Runs pasted / dropped PowerShell or Batch script text by writing it to a
/// temp file and launching it at the user's normal integrity level.</summary>
internal static class ScriptRunner
{
    private static string TempDir => Path.Combine(Path.GetTempPath(), "FingerprintMacroPad", "scripts");

    public static void RunInline(string body, ScriptLanguage lang)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        Directory.CreateDirectory(TempDir);
        string ext = lang == ScriptLanguage.Batch ? "bat" : "ps1";
        string file = Path.Combine(TempDir, $"macro_{Guid.NewGuid():N}.{ext}");
        // BOM helps cmd.exe/powershell with non-ASCII; harmless otherwise.
        File.WriteAllText(file, body, new System.Text.UTF8Encoding(true));
        ProcessLauncher.RunScript(file, "");
    }

    /// <summary>Deletes leftover temp scripts older than a day.</summary>
    public static void CleanupOld()
    {
        try
        {
            if (!Directory.Exists(TempDir)) return;
            foreach (var f in Directory.EnumerateFiles(TempDir))
            {
                try { if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1)) File.Delete(f); }
                catch { }
            }
        }
        catch { }
    }
}
