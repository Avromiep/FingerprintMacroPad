using System.Diagnostics;

namespace FingerprintMacroPad;

/// <summary>
/// Registers a logon Scheduled Task that launches the app elevated (highest
/// privileges) at sign-in — so it starts silently with no UAC prompt each boot.
/// A plain Startup shortcut can't do that because the app requires admin.
/// </summary>
internal static class AutoStart
{
    private const string TaskName = "FingerprintMacroPad";

    public static bool IsEnabled()
    {
        try { return Run("/Query", "/TN", TaskName) == 0; }
        catch { return false; }
    }

    public static bool Enable()
    {
        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
        // /RL HIGHEST => elevated (no UAC prompt); /SC ONLOGON => at sign-in.
        // The task launches the app with /tray so it starts silently in the notification area.
        return Run("/Create", "/TN", TaskName, "/TR", $"\"{exe}\" /tray",
                   "/SC", "ONLOGON", "/RL", "HIGHEST", "/F") == 0;
    }

    public static bool Disable() => Run("/Delete", "/TN", TaskName, "/F") == 0;

    private static int Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit(10000);
        return p.HasExited ? p.ExitCode : -1;
    }
}
