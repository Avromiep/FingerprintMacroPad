using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace FingerprintMacroPad;

/// <summary>Synthesizes hotkeys and media keys. As an elevated process we can send
/// input to normal-integrity foreground windows (that direction is allowed).</summary>
internal static class KeySender
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT   = 0x10;
    private const byte VK_MENU    = 0x12; // Alt
    private const byte VK_LWIN     = 0x5B;

    public static void SendMedia(MediaKeyKind kind)
    {
        byte vk = kind switch
        {
            MediaKeyKind.PlayPause  => 0xB3,
            MediaKeyKind.Next       => 0xB0,
            MediaKeyKind.Previous   => 0xB1,
            MediaKeyKind.Stop       => 0xB2,
            MediaKeyKind.VolumeUp   => 0xAF,
            MediaKeyKind.VolumeDown => 0xAE,
            MediaKeyKind.Mute       => 0xAD,
            _ => 0xB3
        };
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Sends a combo like "Ctrl+Shift+M". Returns false if it couldn't be parsed.</summary>
    public static bool SendHotkey(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return false;
        var mods = new List<byte>();
        byte mainVk = 0;

        foreach (var raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods.Add(VK_CONTROL); break;
                case "shift": mods.Add(VK_SHIFT); break;
                case "alt": case "menu": mods.Add(VK_MENU); break;
                case "win": case "windows": case "cmd": case "meta": mods.Add(VK_LWIN); break;
                default:
                    if (!TryResolveKey(raw, out mainVk)) return false;
                    break;
            }
        }
        if (mainVk == 0) return false;

        foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
        keybd_event(mainVk, 0, 0, UIntPtr.Zero);
        keybd_event(mainVk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        for (int i = mods.Count - 1; i >= 0; i--) keybd_event(mods[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return true;
    }

    private static bool TryResolveKey(string token, out byte vk)
    {
        vk = 0;
        token = token.Trim();
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { vk = (byte)c; return true; }
        }

        string t = token.ToLowerInvariant();
        byte mapped = t switch
        {
            "enter" or "return" => 0x0D,
            "space" or "spacebar" => 0x20,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "printscreen" or "prtsc" => 0x2C,
            _ => 0
        };
        if (mapped != 0) { vk = mapped; return true; }

        // F1..F24 and the rest via WinForms Keys enum (its values equal VK codes).
        if (Enum.TryParse<Forms.Keys>(token, true, out var k) && (int)k is > 0 and < 256)
        {
            vk = (byte)(int)k;
            return true;
        }
        return false;
    }
}
