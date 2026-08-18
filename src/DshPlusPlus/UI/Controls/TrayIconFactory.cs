using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Controls;

public static class TrayIconFactory
{
    public static Icon Create(ThemePalette palette, TrayStatusKind status = TrayStatusKind.Checking)
    {
        var statusColor = status switch
        {
            TrayStatusKind.Connected => palette.Success,
            TrayStatusKind.Disconnected => palette.Danger,
            TrayStatusKind.Attention => palette.Warning,
            _ => palette.Warning
        };

        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var source = LoadWhaleImage())
        using (var halo = new SolidBrush(palette.Background))
        using (var accent = new SolidBrush(statusColor))
        using (var outline = new Pen(palette.Text, 1.2F))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, new Rectangle(0, 0, 32, 32));

            // Keep the whale silhouette visible while making DSH connectivity
            // unambiguous in the taskbar and notification area.
            graphics.FillEllipse(halo, 22, 22, 9, 9);
            graphics.DrawEllipse(outline, 22, 22, 9, 9);
            graphics.FillEllipse(accent, 24, 24, 5, 5);
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

    private static Bitmap LoadWhaleImage()
    {
        var assembly = typeof(TrayIconFactory).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("Assets.dsh-whale.png", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            throw new InvalidOperationException("The dsh++ whale icon resource is missing.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The dsh++ whale icon resource cannot be opened.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
