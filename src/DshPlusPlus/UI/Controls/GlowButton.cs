using System.Drawing;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Controls;

public sealed class GlowButton : Button
{
    public GlowButton(string text, ThemePalette palette, bool primary = false)
    {
        Text = text;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(82, 34);
        Padding = new Padding(12, 6, 12, 6);
        Margin = new Padding(0, 0, 8, 8);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.BorderColor = primary ? palette.Accent : palette.Border;
        BackColor = primary ? palette.AccentSoft : palette.SurfaceRaised;
        ForeColor = primary ? palette.Accent : palette.Text;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = base.GetPreferredSize(proposedSize);
        var dpi = Math.Max(1, DeviceDpi);
        var safeHeight = UiMetrics.SafeHeight(Font.GetHeight(dpi), Padding.Vertical, 34, dpi);
        return new Size(Math.Max(preferred.Width, MinimumSize.Width), Math.Max(preferred.Height, safeHeight));
    }
}
