using System.Drawing;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Controls;

public sealed class StatusChip : Label
{
    public StatusChip(string text, ThemePalette palette)
    {
        AutoSize = true;
        MinimumSize = new Size(0, 28);
        Text = text;
        Padding = new Padding(9, 5, 9, 5);
        Margin = new Padding(0, 0, 8, 0);
        BackColor = palette.AccentSoft;
        ForeColor = palette.Accent;
        TextAlign = ContentAlignment.MiddleCenter;
        AutoEllipsis = true;
        AccessibleRole = AccessibleRole.StatusBar;
    }

    public void SetState(string text, Color foreground, Color background)
    {
        Text = text;
        ForeColor = foreground;
        BackColor = background;
    }
}
