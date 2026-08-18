using System.Diagnostics;
using System.Drawing;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Pages;

public sealed class SystemSettingsPage : PageBase
{
    private readonly SystemInstructionScanner _scanner;
    private LauncherText _text;
    private readonly ListView _files = new();
    private readonly RichTextBox _preview = new();
    private readonly Label _status;
    private readonly ToolTip _fileToolTip = new();
    private Label? _configCardTitle;
    private Label? _previewCardTitle;
    private GlowButton? _refreshButton;
    private GlowButton? _openButton;
    private GlowButton? _revealButton;

    public SystemSettingsPage(SystemInstructionScanner scanner, ThemeManager theme, LauncherText? text = null)
        : base(
            theme,
            LauncherTextCatalog.Get(LauncherLanguage.System).SystemSettings,
            LauncherTextCatalog.Get(LauncherLanguage.System).Pick(
                "查看 DSH 的 AGENTS.md、CLAUDE.md 兼容文件和结构化配置层。",
                "Inspect DSH AGENTS.md, CLAUDE.md compatibility files and structured configuration layers."))
    {
        _scanner = scanner;
        _text = text ?? LauncherTextCatalog.Get(LauncherLanguage.System);
        _status = MutedLabel(_text.Pick("正在准备扫描", "Preparing scan"));
        Build();
    }

    public override bool SupportsAutoRefresh => true;

    public void UpdatePaths(LauncherPaths paths) => _scanner.UpdatePaths(paths);

    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _scanner.ClearCache();
        var infos = await _scanner.ScanAsync(cancellationToken);
        _files.BeginUpdate();
        _files.Items.Clear();
        foreach (var info in infos)
        {
            var item = new ListViewItem(Path.GetFileName(info.Path));
            item.SubItems.Add(info.Scope);
            item.SubItems.Add(info.Kind.ToString());
            item.SubItems.Add(info.IsActive ? _text.Pick("生效", "Active") : _text.Pick("兼容/重复", "Compatible/duplicate"));
            item.SubItems.Add(info.Size.ToString());
            item.Tag = info;
            _files.Items.Add(item);
        }
        _files.EndUpdate();
        _status.Text = _text.Pick(
            $"已发现 {infos.Count} 个配置文件；AGENTS.md 为主要指令文件，CLAUDE.md 用于兼容。",
            $"Found {infos.Count} configuration files. AGENTS.md is the primary instruction file; CLAUDE.md is supported for compatibility.");
    }

    private void Build()
    {
        var layout = CreatePageLayout(3);
        layout.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
        layout.RowStyles[2] = new RowStyle(SizeType.AutoSize);

        _files.Dock = DockStyle.Fill;
        _files.View = View.Details;
        _files.FullRowSelect = true;
        _files.HideSelection = false;
        _files.MultiSelect = false;
        _files.Columns.Add(_text.Pick("文件", "File"));
        _files.Columns.Add(_text.Pick("作用域", "Scope"));
        _files.Columns.Add(_text.Pick("类型", "Kind"));
        _files.Columns.Add(_text.Pick("状态", "Status"));
        _files.Columns.Add(_text.Pick("字节", "Bytes"));
        _files.Resize += (_, _) => ResizeFileColumns();
        _files.ItemMouseHover += (_, e) =>
        {
            if (e.Item?.Tag is SystemInstructionFileInfo info)
                _fileToolTip.SetToolTip(_files, info.Path);
        };
        _files.SelectedIndexChanged += PreviewSelected;
        _preview.Dock = DockStyle.Fill;
        _preview.ReadOnly = true;
        _preview.Tag = "mono";
        _preview.BorderStyle = BorderStyle.FixedSingle;
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 360,
            BackColor = Theme.Palette.Background
        };
        split.SizeChanged += (_, _) =>
        {
            if (split.ClientSize.Width > 0)
                split.SplitterDistance = Math.Clamp(split.ClientSize.Width / 2, 260, Math.Max(260, split.ClientSize.Width - 260));
        };
        split.Panel1.Padding = new Padding(6);
        split.Panel2.Padding = new Padding(6);
        var configCard = Card(_files, _text.Pick("配置层级", "Configuration layers"));
        var previewCard = Card(_preview, _text.Pick("只读预览", "Read-only preview"));
        _configCardTitle = configCard.Controls.OfType<Label>().FirstOrDefault();
        _previewCardTitle = previewCard.Controls.OfType<Label>().FirstOrDefault();
        split.Panel1.Controls.Add(configCard);
        split.Panel2.Controls.Add(previewCard);
        layout.Controls.Add(split, 0, 1);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(6, 5, 0, 0) };
        footer.Controls.Add(_status);
        _refreshButton = new GlowButton(_text.Pick("重新扫描", "Rescan"), Theme.Palette, primary: true);
        _refreshButton.Click += async (_, _) =>
        {
            _scanner.ClearCache();
            await RefreshAsync(CancellationToken.None);
        };
        _openButton = new GlowButton(_text.Pick("打开编辑器", "Open editor"), Theme.Palette);
        _openButton.Click += (_, _) => OpenSelected();
        _revealButton = new GlowButton(_text.Pick("定位文件", "Reveal file"), Theme.Palette);
        _revealButton.Click += (_, _) => RevealSelected();
        footer.Controls.AddRange([_refreshButton, _openButton, _revealButton]);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
    }

    public override void ApplyLanguage(LauncherText text)
    {
        _text = text;
        ApplyHeader(
            text.SystemSettings,
            text.Pick(
                "查看 DSH 的 AGENTS.md、CLAUDE.md 兼容文件和结构化配置层。",
                "Inspect DSH AGENTS.md, CLAUDE.md compatibility files and structured configuration layers."));
        if (_configCardTitle is not null)
            _configCardTitle.Text = text.Pick("配置层级", "Configuration layers");
        if (_previewCardTitle is not null)
            _previewCardTitle.Text = text.Pick("只读预览", "Read-only preview");
        if (_refreshButton is not null)
            _refreshButton.Text = text.Pick("重新扫描", "Rescan");
        if (_openButton is not null)
            _openButton.Text = text.Pick("打开编辑器", "Open editor");
        if (_revealButton is not null)
            _revealButton.Text = text.Pick("定位文件", "Reveal file");
        if (_files.Columns.Count >= 5)
        {
            _files.Columns[0].Text = text.Pick("文件", "File");
            _files.Columns[1].Text = text.Pick("作用域", "Scope");
            _files.Columns[2].Text = text.Pick("类型", "Kind");
            _files.Columns[3].Text = text.Pick("状态", "Status");
            _files.Columns[4].Text = text.Pick("字节", "Bytes");
        }
        _status.Text = text.Pick("正在准备扫描", "Preparing scan");
        foreach (ListViewItem item in _files.Items)
        {
            if (item.Tag is SystemInstructionFileInfo info && item.SubItems.Count >= 4)
                item.SubItems[3].Text = info.IsActive ? text.Pick("生效", "Active") : text.Pick("兼容/重复", "Compatible/duplicate");
        }
    }

    private void ResizeFileColumns()
    {
        if (_files.Columns.Count < 5)
            return;
        var width = Math.Max(260, _files.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        _files.Columns[0].Width = (int)(width * 0.30);
        _files.Columns[1].Width = (int)(width * 0.15);
        _files.Columns[2].Width = (int)(width * 0.23);
        _files.Columns[3].Width = (int)(width * 0.18);
        _files.Columns[4].Width = Math.Max(48, width - _files.Columns.Cast<ColumnHeader>().Take(4).Sum(column => column.Width));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _fileToolTip.Dispose();
        base.Dispose(disposing);
    }

    private void PreviewSelected(object? sender, EventArgs e)
    {
        if (_files.SelectedItems.Count == 0 || _files.SelectedItems[0].Tag is not SystemInstructionFileInfo info)
            return;
        try
        {
            _preview.Text = File.Exists(info.Path)
                ? File.ReadAllText(info.Path)
                : _text.Pick("该项目是链接/目录，无法直接预览。", "This item is a link or directory and cannot be previewed directly.");
        }
        catch (Exception ex)
        {
            _preview.Text = _text.Pick($"读取失败：{ex.Message}", $"Read failed: {ex.Message}");
        }
    }

    private void OpenSelected()
    {
        if (GetSelected() is not { } info || !File.Exists(info.Path))
            return;
        Process.Start(new ProcessStartInfo { FileName = info.Path, UseShellExecute = true });
    }

    private void RevealSelected()
    {
        if (GetSelected() is not { } info)
            return;
        if (File.Exists(info.Path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{info.Path}\"") { UseShellExecute = true });
        else if (Directory.Exists(info.Path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{info.Path}\"") { UseShellExecute = true });
    }

    private SystemInstructionFileInfo? GetSelected() =>
        _files.SelectedItems.Count == 0 ? null : _files.SelectedItems[0].Tag as SystemInstructionFileInfo;
}
