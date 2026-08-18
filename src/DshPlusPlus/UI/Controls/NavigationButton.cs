using System.ComponentModel;
using System.Drawing;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Controls;

public enum NavigationIconKind
{
    Dashboard,
    Maintenance,
    Api,
    System,
    Plugins,
    Settings
}

public sealed record NavigationItem(
    string Title,
    string Index,
    NavigationIconKind Icon)
{
    public string AccessibleName => Title;

    public static NavigationItem Create(string title, string index) => new(
        title,
        index,
        index switch
        {
            "01" => NavigationIconKind.Dashboard,
            "02" => NavigationIconKind.Maintenance,
            "03" => NavigationIconKind.Api,
            "04" => NavigationIconKind.System,
            "05" => NavigationIconKind.Plugins,
            _ => NavigationIconKind.Settings
        });
}

public sealed class NavigationButton : Button
{
    private readonly ThemePalette _palette;
    private bool _hovered;

    public NavigationButton(NavigationItem item, ThemePalette palette)
    {
        Item = item;
        _palette = palette;
        Text = string.Empty;
        AccessibleName = item.AccessibleName;
        AccessibleDescription = $"{item.Index} {item.Title}";
        AccessibleRole = AccessibleRole.PushButton;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = false;
        AutoSize = false;
        MinimumSize = new Size(56, 34);
        Height = 42;
        Margin = new Padding(0, 3, 0, 3);
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
        SetStyle(ControlStyles.Selectable, false);
    }

    public NavigationItem Item { get; private set; }

    public bool IsCollapsed { get; private set; }

    public void UpdateTitle(string title)
    {
        Item = Item with { Title = title };
        AccessibleName = Item.AccessibleName;
        AccessibleDescription = $"{Item.Index} {Item.Title}";
        Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsActive { get; set; }

    public void SetCollapsed(bool collapsed)
    {
        if (IsCollapsed == collapsed)
            return;
        IsCollapsed = collapsed;
        AccessibleName = Item.AccessibleName;
        Invalidate();
    }

    public void ApplyLayout(bool collapsed, int width, int dpi)
    {
        SetCollapsed(collapsed);
        Width = Math.Max(MinimumSize.Width, width);
        Height = UiMetrics.SafeHeight(Font.GetHeight(Math.Max(1, dpi)), 12, 42, dpi);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = ClientRectangle;
        using var background = new SolidBrush(_hovered ? _palette.AccentSoft : BackColor);
        e.Graphics.FillRectangle(background, bounds);
        if (IsActive)
        {
            using var active = new SolidBrush(_palette.Accent);
            e.Graphics.FillRectangle(active, bounds.Left, bounds.Top, 3, bounds.Height);
        }

        var iconSize = Math.Min(22, Math.Max(16, bounds.Height - 16));
        var iconLeft = IsCollapsed ? bounds.Left + (bounds.Width - iconSize) / 2 : bounds.Left + 12;
        DrawIcon(e.Graphics, new Rectangle(iconLeft, bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize));

        if (!IsCollapsed)
        {
            var textBounds = new Rectangle(iconLeft + iconSize + 10, bounds.Top, Math.Max(1, bounds.Width - iconSize - 28), bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                Item.Title,
                Font,
                textBounds,
                ForeColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var dpi = Math.Max(1, DeviceDpi);
        var height = UiMetrics.SafeHeight(Font.GetHeight(dpi), 12, 42, dpi);
        var width = IsCollapsed ? UiMetrics.NavigationWidth(true) : UiMetrics.NavigationWidth(false) - 28;
        return new Size(Math.Max(width, MinimumSize.Width), height);
    }

    private void DrawIcon(Graphics graphics, Rectangle bounds)
    {
        using var pen = new Pen(ForeColor, Math.Max(1f, bounds.Width / 8f))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        using var brush = new SolidBrush(ForeColor);
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        switch (Item.Icon)
        {
            case NavigationIconKind.Dashboard:
                graphics.DrawRectangle(pen, bounds.Left + 3, bounds.Top + 3, bounds.Width - 6, bounds.Height - 6);
                graphics.DrawLine(pen, center.X, bounds.Top + 4, center.X, bounds.Bottom - 4);
                graphics.DrawLine(pen, bounds.Left + 4, center.Y, bounds.Right - 4, center.Y);
                break;
            case NavigationIconKind.Maintenance:
                graphics.DrawLine(pen, bounds.Left + 5, bounds.Bottom - 5, bounds.Right - 6, bounds.Top + 6);
                graphics.DrawEllipse(pen, bounds.Left + 2, bounds.Top + 2, 7, 7);
                graphics.DrawLine(pen, bounds.Right - 8, bounds.Top + 3, bounds.Right - 3, bounds.Top + 8);
                break;
            case NavigationIconKind.Api:
                graphics.DrawEllipse(pen, bounds.Left + 3, bounds.Top + 3, bounds.Width - 6, bounds.Height - 6);
                graphics.DrawLine(pen, bounds.Left + 6, center.Y, bounds.Right - 6, center.Y);
                graphics.FillEllipse(brush, center.X - 2, center.Y - 2, 4, 4);
                break;
            case NavigationIconKind.System:
                graphics.DrawEllipse(pen, bounds.Left + 5, bounds.Top + 5, bounds.Width - 10, bounds.Height - 10);
                for (var index = 0; index < 4; index++)
                {
                    var angle = index * Math.PI / 2;
                    var x1 = center.X + (int)(Math.Cos(angle) * (bounds.Width / 2 - 3));
                    var y1 = center.Y + (int)(Math.Sin(angle) * (bounds.Height / 2 - 3));
                    var x2 = center.X + (int)(Math.Cos(angle) * (bounds.Width / 2));
                    var y2 = center.Y + (int)(Math.Sin(angle) * (bounds.Height / 2));
                    graphics.DrawLine(pen, x1, y1, x2, y2);
                }
                break;
            case NavigationIconKind.Plugins:
                graphics.DrawRectangle(pen, bounds.Left + 3, bounds.Top + 3, 7, 7);
                graphics.DrawRectangle(pen, bounds.Right - 10, bounds.Bottom - 10, 7, 7);
                graphics.DrawLine(pen, bounds.Left + 10, bounds.Top + 10, bounds.Right - 10, bounds.Bottom - 10);
                break;
            default:
                graphics.DrawEllipse(pen, bounds.Left + 4, bounds.Top + 4, bounds.Width - 8, bounds.Height - 8);
                graphics.DrawLine(pen, center.X, bounds.Top + 2, center.X, bounds.Bottom - 2);
                graphics.DrawLine(pen, bounds.Left + 2, center.Y, bounds.Right - 2, center.Y);
                break;
        }
    }
}
