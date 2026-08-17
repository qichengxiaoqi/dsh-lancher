using System.Drawing;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Pages;

public abstract class PageBase : UserControl
{
    protected PageBase(ThemeManager theme, string title, string subtitle)
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(28, 24, 28, 20);
        Theme = theme;
        Title = title;
        Subtitle = subtitle;
        BackColor = theme.Palette.Background;
    }

    protected ThemeManager Theme { get; }
    protected string Title { get; }
    protected string Subtitle { get; }

    public virtual bool SupportsAutoRefresh => false;

    protected TableLayoutPanel CreatePageLayout(int rows = 3)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows,
            BackColor = Theme.Palette.Background
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var index = 1; index < rows; index++)
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / (rows - 1)));
        var heading = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Palette.Background };
        var title = new Label
        {
            Text = Title,
            Dock = DockStyle.Top,
            AutoSize = true,
            Tag = "title",
            ForeColor = Theme.Palette.Text
        };
        var subtitle = new Label
        {
            Text = Subtitle,
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Theme.Palette.Muted,
            AutoEllipsis = true,
            Tag = "small"
        };
        heading.Controls.Add(subtitle);
        heading.Controls.Add(title);
        layout.Controls.Add(heading, 0, 0);
        return layout;
    }

    protected Panel Card(Control content, string? title = null)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            Margin = new Padding(6),
            BackColor = Theme.Palette.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Tag = "surface"
        };
        if (title is not null)
        {
            var label = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                ForeColor = Theme.Palette.Text,
                Tag = "section"
            };
            card.Controls.Add(content);
            card.Controls.Add(label);
        }
        else
        {
            card.Controls.Add(content);
        }
        return card;
    }

    protected Label MutedLabel(string text) => new()
    {
        Text = text,
        AutoEllipsis = true,
        ForeColor = Theme.Palette.Muted,
        Margin = new Padding(0, 3, 0, 3)
    };

    public virtual Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void ApplyCurrentTheme() => Theme.Apply(this);
}
