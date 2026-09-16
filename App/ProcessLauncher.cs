using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FingerprintMacroPad;

/// <summary>
/// Launches processes at the interactive user's normal (medium) integrity level.
/// Because this app runs elevated, a plain Process.Start would spawn ELEVATED
/// children (an admin browser, etc.). We instead borrow the shell's (explorer.exe)
/// token so launched apps behave exactly as if the user double-clicked them.
/// Falls back to a shell-execute launch if the token can't be obtained.
/// </summary>
internal static class ProcessLauncher
{
    public static void Open(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        // explorer opens files/URLs/folders with the default handler, at medium IL.
        if (!LaunchWithShellToken(explorer, $"\"{target}\"", WorkingDirOf(target)))
            FallbackShellExecute(target, "");
    }

    public static void Launch(string exe, string args)
    {
        if (string.IsNullOrWhiteSpace(exe)) return;
        if (string.IsNullOrWhiteSpace(args)) { Open(exe); return; }
        string cmd = $"\"{exe}\" {args}";
        if (!LaunchWithShellToken(exe, cmd, WorkingDirOf(exe)))
            FallbackShellExecute(exe, args);
    }

    public static void RunScript(string target, string args)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        string ext = Path.GetExtension(target).ToLowerInvariant();
        string app, cmd;
        switch (ext)
        {
            case ".ps1":
                app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                   "WindowsPowerShell\\v1.0\\powershell.exe");
                cmd = $"\"{app}\" -ExecutionPolicy Bypass -File \"{target}\" {args}";
                break;
            case ".bat":
            case ".cmd":
                app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                cmd = $"\"{app}\" /c \"{target}\" {args}";
                break;
            default: // .exe or anything else
                Launch(target, args);
                return;
        }
        if (!LaunchWithShellToken(app, cmd, WorkingDirOf(target)))
            FallbackShellExecute(target, args);
    }

    private static string WorkingDirOf(string path)
    {
        try { var d = Path.GetDirectoryName(path); return string.IsNullOrEmpty(d) ? Environment.CurrentDirectory : d; }
        catch { return Environment.CurrentDirectory; }
    }

    private static void FallbackShellExecute(string target, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = args ?? "",
                UseShellExecute = true
            });
        }
        catch { /* give up quietly */ }
    }

    // ── Shell-token launch ────────────────────────────────────────────────────
    private static bool LaunchWithShellToken(string app, string commandLine, string workingDir)
    {
        IntPtr hShellProc = IntPtr.Zero, hShellTok = IntPtr.Zero, hPrimary = IntPtr.Zero;
        try
        {
            IntPtr hwnd = GetShellWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;

            hShellProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hShellProc == IntPtr.Zero) return false;

            if (!OpenProcessToken(hShellProc,
                    TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID,
                    out hShellTok))
                return false;

            if (!DuplicateTokenEx(hShellTok, MAXIMUM_ALLOWED, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out hPrimary))
                return false;

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            var pi = new PROCESS_INFORMATION();
            bool ok = CreateProcessWithTokenW(
                hPrimary, 0, null, commandLine,
                CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, workingDir, ref si, out pi);

            if (ok)
            {
                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            }
            return ok;
        }
        catch { return false; }
        finally
        {
            if (hPrimary != IntPtr.Zero) CloseHandle(hPrimary);
            if (hShellTok != IntPtr.Zero) CloseHandle(hShellTok);
            if (hShellProc != IntPtr.Zero) CloseHandle(hShellProc);
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_DUPLICATE = 0x0002, TOKEN_QUERY = 0x0008, TOKEN_ASSIGN_PRIMARY = 0x0001,
                       TOKEN_ADJUST_DEFAULT = 0x0080, TOKEN_ADJUST_SESSIONID = 0x0100;
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    private enum SECURITY_IMPERSONATION_LEVEL { SecurityAnonymous, SecurityIdentification, SecurityImpersonation, SecurityDelegation }
    private enum TOKEN_TYPE { TokenPrimary = 1, TokenImpersonation }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr proc, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attrs,
        SECURITY_IMPERSONATION_LEVEL level, TOKEN_TYPE type, out IntPtr newToken);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string? appName,
        string? cmdLine, uint creationFlags, IntPtr env, string? cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
}
