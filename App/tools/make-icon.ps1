# Generates a crisp multi-resolution app.ico (fingerprint-on-a-button), each size
# rendered natively, and a preview strip for inspection.
# Run under Windows PowerShell 5.1:
#   powershell.exe -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class IcoGen
{
    static readonly Color Glyph = Color.FromArgb(0x2F,0x80,0xFF);  // vivid electric blue

    public static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        // Solid fingerprint glyph on transparent background (no button) — bold blue
        // ridges filling the icon, 2-3 widely-spaced loops with staggered breaks + curl core.
        int loops = size <= 40 ? 2 : 3;
        float stroke = Math.Max(2.6f, size * 0.10f);
        float cx = size * 0.5f, cy = size * 0.5f;
        float baseR = size * 0.155f, outerR = size * 0.42f;
        float stepR = loops > 1 ? (outerR - baseR) / (loops - 1) : 0f;
        using (var pen = new Pen(Glyph, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            for (int i = 0; i < loops; i++)
            {
                float rx = baseR + i * stepR;
                float ry = rx * 1.12f;                        // slightly taller = fingertip
                float gap = 50f;
                float gapCenter = 90f + (i % 2 == 0 ? -20f : 20f);  // stagger, no wedge
                g.DrawArc(pen, cx - rx, cy - ry, rx*2, ry*2, gapCenter + gap/2f, 360f - gap);
            }
            // small curl core (open loop), nudged up like a real whorl
            float r0 = size * 0.062f;
            g.DrawArc(pen, cx - r0, cy - r0*1.1f - size*0.01f, r0*2, r0*2.2f, 120f, 300f);
        }

        g.Dispose();
        return bmp;
    }

    public static byte[] Png(int size)
    {
        using (var bmp = Draw(size))
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }
}
"@

$root = Split-Path $PSScriptRoot -Parent

# --- write app.ico (multi-size) ---
$sizes = 16,20,24,32,40,48,64,128,256
$pngs  = foreach ($s in $sizes) { ,([IcoGen]::Png($s)) }
$out = Join-Path $root 'app.ico'
$fs = [System.IO.File]::Open($out, 'Create'); $bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
    $s=$sizes[$i]; $len=$pngs[$i].Length; $dim=[Byte]($(if($s -ge 256){0}else{$s}))
    $bw.Write($dim); $bw.Write($dim); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($d in $pngs) { $bw.Write($d) }
$bw.Flush(); $fs.Close()
Write-Output "Wrote $out ($($sizes.Count) sizes)"

# --- preview: TRUE size (1x) row + zoomed row, on a taskbar-like ground ---
$pv = 16,20,24,32,40,48; $zoom = 6; $padp = 24
$W = 1100; $H = 60 + 48*$zoom + 3*$padp
$canvas = New-Object System.Drawing.Bitmap $W, $H
$cg = [System.Drawing.Graphics]::FromImage($canvas)
$cg.Clear([System.Drawing.Color]::FromArgb(28,32,40))
# true-size row (what you actually see)
$cg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$cg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$x = $padp; $rowY = $padp
foreach($s in $pv){
  $bmp = [IcoGen]::Draw($s)
  $cg.DrawImage($bmp, (New-Object System.Drawing.Rectangle $x, ($rowY + (48-$s)), $s, $s), (New-Object System.Drawing.Rectangle 0,0,$s,$s), [System.Drawing.GraphicsUnit]::Pixel)
  $x += $s + $padp; $bmp.Dispose()
}
# zoomed row (nearest-neighbor, exaggerates edges)
$cg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$x = $padp; $rowY = 60 + 2*$padp
foreach($s in 24,32,48){
  $bmp = [IcoGen]::Draw($s)
  $cg.DrawImage($bmp, (New-Object System.Drawing.Rectangle $x, $rowY, ($s*$zoom), ($s*$zoom)), (New-Object System.Drawing.Rectangle 0,0,$s,$s), [System.Drawing.GraphicsUnit]::Pixel)
  $x += $s*$zoom + $padp; $bmp.Dispose()
}
$prev = "C:\Users\avrom\AppData\Local\Temp\claude\C--Users-avrom-My-Claude-Code\5ab33dcb-f2ae-42a3-bb46-e03ee07d0c67\scratchpad\ico_preview.png"
$canvas.Save($prev, [System.Drawing.Imaging.ImageFormat]::Png); $cg.Dispose(); $canvas.Dispose()
Write-Output "Preview: $prev"
