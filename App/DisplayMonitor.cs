using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FingerprintMacroPad;

/// <summary>Tracks whether the monitor is powered off via the Windows
/// GUID_CONSOLE_DISPLAY_STATE power-setting broadcast, using a hidden
/// message-only window. Must be created on the UI thread.</summary>
public sealed class DisplayMonitor : IDisposable
{
    /// <summary>Raised with true when the display turns off, false when it turns on.</summary>
    public event Action<bool>? DisplayOffChanged;

    private readonly HwndSource _src;
    private IntPtr _registration;

    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x0;
    private static readonly Guid GUID_CONSOLE_DISPLAY_STATE =
        new("6fe69556-704a-47a0-8f24-c28d936fda47");

    public DisplayMonitor()
    {
        var p = new HwndSourceParameters("FMP_DisplayMonitor")
        {
            ParentWindow = (IntPtr)(-3),   // HWND_MESSAGE -> message-only window
            Width = 0, Height = 0
        };
        _src = new HwndSource(p);
        _src.AddHook(Hook);
        var guid = GUID_CONSOLE_DISPLAY_STATE;   // can't pass a static readonly by ref
        _registration = RegisterPowerSettingNotification(
            _src.Handle, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_POWERBROADCAST && (int)wParam == PBT_POWERSETTINGCHANGE)
        {
            var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
            if (setting.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
            {
                Diag.Log($"display power state = {setting.Data} (0=off,1=on,2=dim)");
                DisplayOffChanged?.Invoke(setting.Data == 0);   // 0 = off, 1 = on, 2 = dimmed
            }
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;   // first byte of the DWORD state value
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr Handle);

    public void Dispose()
    {
        try { if (_registration != IntPtr.Zero) { UnregisterPowerSettingNotification(_registration); _registration = IntPtr.Zero; } } catch { }
        try { _src.RemoveHook(Hook); _src.Dispose(); } catch { }
    }
}
