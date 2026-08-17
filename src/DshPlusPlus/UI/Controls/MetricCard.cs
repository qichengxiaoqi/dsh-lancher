using System.Drawing;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Controls;

public sealed class MetricCard : Panel
{
    private readonly Label _value = new();
    private readonly Label _detail = new();
    private readonly ToolTip _toolTip = new();

    public MetricCard(string caption, string value, string detail, ThemePalette palette)
    {
        Tag = "raised";
        Dock = DockStyle.Fill;
        Margin = new Padding(6);
        Padding = new Padding(14, 10, 14, 8);
        BackColor = palette.SurfaceRaised;
        BorderStyle = BorderStyle.FixedSingle;
        var captionLabel = new Label
        {
            Text = caption,
            ForeColor = palette.Muted,
            Dock = DockStyle.Top,
            AutoSize = true,
            Tag = "small"
        };
        _value.Text = value;
        _value.ForeColor = palette.Text;
        _value.Dock = DockStyle.Top;
        _value.AutoSize = true;
        _value.Tag = "section";
        _detail.Text = detail;
        _detail.ForeColor = palette.Muted;
        _detail.Dock = DockStyle.Fill;
        _detail.AutoEllipsis = true;
        _detail.Tag = "small";
        Controls.Add(_detail);
        Controls.Add(_value);
        Controls.Add(captionLabel);
    }

    public void SetValue(string value, string? detail = null)
    {
        _value.Text = value;
        _toolTip.SetToolTip(_value, value);
        if (detail is not null)
        {
            _detail.Text = detail;
            _toolTip.SetToolTip(_detail, detail);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
