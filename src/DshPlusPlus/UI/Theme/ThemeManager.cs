using System.Drawing;
using System.Windows.Forms;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.UI.Theme;

public sealed class ThemeManager : IDisposable
{
    private readonly List<ThemeFonts> _retiredFonts = [];
    private ThemeFonts _fonts;

    public ThemeManager(ThemeSettings settings)
    {
        Settings = settings;
        Palette = ThemePalettes.Create(settings);
        _fonts = ThemeFonts.Create(settings);
    }

    public ThemeSettings Settings { get; private set; }
    public ThemePalette Palette { get; private set; }

    public void Update(ThemeSettings settings)
    {
        _retiredFonts.Add(_fonts);
        Settings = settings;
        Palette = ThemePalettes.Create(settings);
        _fonts = ThemeFonts.Create(settings);
    }

    public void Apply(Control control)
    {
        control.BackColor = ResolveBackColor(control);
        control.ForeColor = ResolveForeColor(control);
        control.Font = FontFor(control);
        if (control is TextBoxBase textBox)
        {
            textBox.BackColor = Palette.SurfaceRaised;
            textBox.ForeColor = Palette.Text;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
        else if (control is Button button)
        {
            button.BackColor = Palette.SurfaceRaised;
            button.ForeColor = Palette.Text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Palette.Border;
            button.FlatAppearance.MouseOverBackColor = Palette.AccentSoft;
        }
        else if (control is ListView listView)
        {
            listView.BackColor = Palette.SurfaceRaised;
            listView.ForeColor = Palette.Text;
        }
        else if (control is DataGridView grid)
        {
            grid.BackgroundColor = Palette.Surface;
            grid.GridColor = Palette.Border;
            grid.DefaultCellStyle.BackColor = Palette.SurfaceRaised;
            grid.DefaultCellStyle.ForeColor = Palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = Palette.AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = Palette.Text;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.Surface;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.Muted;
            grid.ColumnHeadersDefaultCellStyle.Font = _fonts.Small;
            grid.DefaultCellStyle.Font = _fonts.Body;
        }

        foreach (Control child in control.Controls)
            Apply(child);
        control.Invalidate();
    }

    public void ReleaseRetiredFonts()
    {
        foreach (var fonts in _retiredFonts)
            fonts.Dispose();
        _retiredFonts.Clear();
    }

    public void Dispose()
    {
        ReleaseRetiredFonts();
        _fonts.Dispose();
    }

    private Font FontFor(Control control) => (control.Tag as string) switch
    {
        "title" => _fonts.Title,
        "section" => _fonts.Section,
        "small" => _fonts.Small,
        "mono" => _fonts.Mono,
        _ => _fonts.Body
    };

    private Color ResolveBackColor(Control control) => control switch
    {
        Form => Palette.Background,
        TabPage => Palette.Surface,
        GroupBox => Palette.Surface,
        _ when control.Tag as string == "surface" => Palette.Surface,
        _ when control.Tag as string == "raised" => Palette.SurfaceRaised,
        _ => control.Parent is null ? Palette.Background : control.Parent.BackColor
    };

    private Color ResolveForeColor(Control control) => Palette.Text;

    private sealed class ThemeFonts : IDisposable
    {
        private ThemeFonts(
            Font body,
            Font section,
            Font title,
            Font small,
            Font mono)
        {
            Body = body;
            Section = section;
            Title = title;
            Small = small;
            Mono = mono;
        }

        public Font Body { get; }
        public Font Section { get; }
        public Font Title { get; }
        public Font Small { get; }
        public Font Mono { get; }

        public static ThemeFonts Create(ThemeSettings settings)
        {
            var scale = UiMetrics.ClampFontScale(settings.FontScale) / 100f;
            var uiFamily = UiFontResolver.ResolveUiFamily();
            var monoFamily = UiFontResolver.ResolveMonoFamily();
            return new ThemeFonts(
                new Font(uiFamily, 9f * scale, FontStyle.Regular, GraphicsUnit.Point),
                new Font(uiFamily, 10f * scale, FontStyle.Bold, GraphicsUnit.Point),
                new Font(uiFamily, 20f * scale, FontStyle.Bold, GraphicsUnit.Point),
                new Font(uiFamily, 8.5f * scale, FontStyle.Regular, GraphicsUnit.Point),
                new Font(monoFamily, 9f * scale, FontStyle.Regular, GraphicsUnit.Point));
        }

        public void Dispose()
        {
            Body.Dispose();
            Section.Dispose();
            Title.Dispose();
            Small.Dispose();
            Mono.Dispose();
        }
    }
}
