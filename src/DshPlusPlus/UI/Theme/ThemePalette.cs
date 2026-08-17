using System.Drawing;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.UI.Theme;

public sealed record ThemePalette(
    Color Background,
    Color Surface,
    Color SurfaceRaised,
    Color Border,
    Color Text,
    Color Muted,
    Color Accent,
    Color AccentSoft,
    Color Success,
    Color Warning,
    Color Danger);

public static class ThemePalettes
{
    public static ThemePalette Create(ThemeSettings settings)
    {
        var accent = ParseColor(settings.Accent, Color.FromArgb(57, 217, 255));
        return settings.Name switch
        {
            "Light" => new ThemePalette(
                Color.FromArgb(244, 247, 250), Color.White, Color.FromArgb(235, 241, 246),
                Color.FromArgb(211, 221, 230), Color.FromArgb(22, 34, 48), Color.FromArgb(92, 108, 122),
                Color.FromArgb(0, 128, 170), Color.FromArgb(218, 244, 252), Color.FromArgb(24, 151, 94),
                Color.FromArgb(190, 112, 24), Color.FromArgb(190, 58, 66)),
            "High Contrast" => new ThemePalette(
                Color.Black, Color.FromArgb(12, 12, 12), Color.FromArgb(30, 30, 30), Color.White,
                Color.White, Color.FromArgb(210, 210, 210), Color.Yellow, Color.FromArgb(80, 80, 0),
                Color.Lime, Color.Orange, Color.Red),
            _ => new ThemePalette(
                Color.FromArgb(12, 18, 27), Color.FromArgb(20, 29, 41), Color.FromArgb(28, 40, 55),
                Color.FromArgb(47, 66, 84), Color.FromArgb(230, 241, 247), Color.FromArgb(139, 160, 175),
                accent, Color.FromArgb(25, 70, 88), Color.FromArgb(70, 214, 151), Color.FromArgb(248, 177, 76),
                Color.FromArgb(246, 102, 114))
        };
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return ColorTranslator.FromHtml(value);
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
