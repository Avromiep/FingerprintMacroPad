using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace FingerprintMacroPad;

public sealed record UpdateInfo(Version Version, string Tag, string ExeUrl, string Notes);

/// <summary>Checks GitHub Releases for a newer build, downloads the single-file exe,
/// and swaps it in on relaunch.</summary>
public static class UpdateService
{
    private const string Owner = "Avromiep";
    private const string Repo = "FingerprintMacroPad";
    private const string AssetName = "FingerprintMacroPad.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("FingerprintMacroPad-Updater");
        return h;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public static string CurrentVersionText
    {
        get { var v = CurrentVersion; return $"{v.Major}.{v.Minor}.{v.Build}"; }
    }

    /// <summary>Returns update info if a newer release exists, else null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        using var resp = await Http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        Version? ver = ParseVersion(root.GetProperty("tag_name").GetString() ?? "");
        if (ver == null) return null;
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

        string exeUrl = "";
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
                if ((a.GetProperty("name").GetString() ?? "").Equals(AssetName, StringComparison.OrdinalIgnoreCase))
                { exeUrl = a.GetProperty("browser_download_url").GetString() ?? ""; break; }

        if (string.IsNullOrEmpty(exeUrl) || ver <= CurrentVersion) return null;
        return new UpdateInfo(ver, root.GetProperty("tag_name").GetString() ?? "", exeUrl, notes);
    }

    private static Version? ParseVersion(string tag)
    {
        tag = tag.TrimStart('v', 'V').Trim();
        return Version.TryParse(tag, out var v) ? v : null;
    }

    /// <summary>Downloads the new exe to a temp folder and returns its path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "FingerprintMacroPad", "update");
        Directory.CreateDirectory(dir);
        string outPath = Path.Combine(dir, $"FingerprintMacroPad-{info.Version}.exe");

        using var resp = await Http.GetAsync(info.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(outPath))
        {
            var buf = new byte[81920];
            long read = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                read += n;
                if (total is > 0) progress?.Report((double)read / total.Value);
            }
        }
        return outPath;
    }

    /// <summary>Writes a helper script that waits for this app to exit, swaps in the new
    /// exe, and relaunches it. The caller should then shut the app down.</summary>
    public static void StartSwap(string newExePath)
    {
        string current = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
        string exeName = Path.GetFileName(current);
        string dir = Path.Combine(Path.GetTempPath(), "FingerprintMacroPad", "update");
        Directory.CreateDirectory(dir);
        string bat = Path.Combine(dir, "swap.bat");

        string script = $"""
            @echo off
            :wait
            tasklist /fi "imagename eq {exeName}" 2>nul | find /i "{exeName}" >nul
            if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )
            move /y "{newExePath}" "{current}" >nul
            start "" "{current}"
            del "%~f0"
            """;
        File.WriteAllText(bat, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = bat,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
