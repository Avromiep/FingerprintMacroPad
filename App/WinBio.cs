using System.Runtime.InteropServices;

namespace FingerprintMacroPad;

/// <summary>
/// Minimal, strictly READ-ONLY Windows Biometric Framework interop.
/// Only OpenSession / Identify / LocateSensor / Cancel / CloseSession are declared here.
/// There is deliberately NO enroll, delete, or capture-sample function, so this
/// code cannot add, remove, or alter any enrolled fingerprint, nor read a print image.
/// LocateSensor only detects THAT a finger touched (no image, no match) — used for the
/// fast "wake the screen" path when the display is asleep.
/// </summary>
internal static class WinBio
{
    public const uint WINBIO_TYPE_FINGERPRINT = 0x00000008;
    public const uint WINBIO_POOL_SYSTEM      = 0x00000001;
    public const uint WINBIO_FLAG_DEFAULT     = 0x00000000;

    public const int  S_OK           = 0;
    public const uint E_UNKNOWN_ID   = 0x80098003; // WINBIO_E_UNKNOWN_ID  (touch, not the enrolled finger)
    public const uint E_BAD_CAPTURE  = 0x80098008; // WINBIO_E_BAD_CAPTURE (partial/poor read)
    public const uint E_ACCESSDENIED = 0x80070005;

    // 76-byte identity blob; we never decode it (no identity/SID is read).
    [StructLayout(LayoutKind.Sequential, Size = 76)]
    public struct IDENTITY { public uint Type; }

    [DllImport("winbio.dll", EntryPoint = "WinBioOpenSession")]
    public static extern int OpenSession(
        uint factor, uint poolType, uint flags,
        IntPtr unitArray, IntPtr unitCount, IntPtr databaseId,
        out uint sessionHandle);

    [DllImport("winbio.dll", EntryPoint = "WinBioCloseSession")]
    public static extern int CloseSession(uint sessionHandle);

    [DllImport("winbio.dll", EntryPoint = "WinBioCancel")]
    public static extern int Cancel(uint sessionHandle);

    [DllImport("winbio.dll", EntryPoint = "WinBioIdentify")]
    public static extern int Identify(
        uint sessionHandle,
        out uint unitId,
        out IDENTITY identity,
        out byte subFactor,
        out uint rejectDetail);

    // Detects a touch only (no capture image, no matching) — fast.
    [DllImport("winbio.dll", EntryPoint = "WinBioLocateSensor")]
    public static extern int LocateSensor(uint sessionHandle, out uint unitId);
}
