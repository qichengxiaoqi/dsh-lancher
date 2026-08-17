using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Controls;

public sealed class LogDrawer : UserControl
{
    private readonly TextBox _textBox = new();

    public LogDrawer(ThemePalette palette)
    {
        Dock = DockStyle.Fill;
        Tag = "mono";
        Padding = new Padding(0, 8, 0, 0);
        _textBox.Dock = DockStyle.Fill;
        _textBox.Multiline = true;
        _textBox.ReadOnly = true;
        _textBox.ScrollBars = ScrollBars.Vertical;
        _textBox.Tag = "mono";
        _textBox.BackColor = palette.Background;
        _textBox.ForeColor = palette.Muted;
        _textBox.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_textBox);
    }

    public void Append(string message)
    {
        _textBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        if (_textBox.TextLength > 16000)
            _textBox.Text = _textBox.Text[^16000..];
        _textBox.SelectionStart = _textBox.TextLength;
        _textBox.ScrollToCaret();
    }
}
