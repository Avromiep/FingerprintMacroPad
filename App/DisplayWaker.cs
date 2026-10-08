using System.Runtime.InteropServices;

namespace FingerprintMacroPad;

/// <summary>Turns the monitor back on. A synthesized no-op key (F15) counts as user
/// input, which wakes a display that has powered down; SetThreadExecutionState also
/// nudges the display idle timer as a belt-and-suspenders.</summary>
internal static class DisplayWaker
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private const byte VK_F15 = 0x7E;             // harmless; almost never bound
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    public static void Wake()
    {
        try { SetThreadExecutionState(ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED); } catch { }
        try
        {
            keybd_event(VK_F15, 0, 0, UIntPtr.Zero);
            keybd_event(VK_F15, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch { }
    }
}
