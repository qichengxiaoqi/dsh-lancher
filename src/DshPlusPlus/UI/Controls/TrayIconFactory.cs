using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Controls;

public static class TrayIconFactory
{
    public static Icon Create(ThemePalette palette)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var background = new SolidBrush(palette.Background))
        using (var accent = new SolidBrush(palette.Accent))
        using (var highlight = new SolidBrush(palette.Text))
        using (var outline = new Pen(palette.Accent, 2F))
        using (var font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            graphics.FillEllipse(background, 1, 1, 30, 30);
            graphics.DrawEllipse(outline, 2, 2, 28, 28);
            graphics.FillEllipse(accent, 7, 7, 18, 18);
            graphics.DrawString("d", font, highlight, 10, 6);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
