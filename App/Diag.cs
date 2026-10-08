using System.IO;

namespace FingerprintMacroPad;

/// <summary>Tiny rolling diagnostic log (display + sensor events) to help debug the
/// wake/macro behavior across different machines. Best-effort, size-capped.</summary>
internal static class Diag
{
    private static readonly string Path = System.IO.Path.Combine(AppConfig.Dir, "diag.log");
    private static readonly object Lock = new();

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            lock (Lock)
            {
                var fi = new FileInfo(Path);
                if (fi.Exists && fi.Length > 200_000) File.WriteAllText(Path, "");
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {msg}\n");
            }
        }
        catch { /* logging is best-effort */ }
    }
}
