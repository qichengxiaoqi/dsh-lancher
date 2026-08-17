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
    private readonly ListView _files = new();
    private readonly RichTextBox _preview = new();
    private readonly Label _status;
    private readonly ToolTip _fileToolTip = new();

    public SystemSettingsPage(SystemInstructionScanner scanner, ThemeManager theme)
        : base(theme, "系统级设置", "查看 DSH 的 AGENTS.md、CLAUDE.md 兼容文件和结构化配置层。")
    {
        _scanner = scanner;
        _status = MutedLabel("正在准备扫描");
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
            item.SubItems.Add(info.IsActive ? "生效" : "兼容/重复");
            item.SubItems.Add(info.Size.ToString());
            item.Tag = info;
            _files.Items.Add(item);
        }
        _files.EndUpdate();
        _status.Text = $"已发现 {infos.Count} 个配置文件；AGENTS.md 为主要指令文件，CLAUDE.md 用于兼容。";
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
        _files.Columns.Add("文件");
        _files.Columns.Add("作用域");
        _files.Columns.Add("类型");
        _files.Columns.Add("状态");
        _files.Columns.Add("字节");
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
        split.Panel1.Controls.Add(Card(_files, "配置层级"));
        split.Panel2.Controls.Add(Card(_preview, "只读预览"));
        layout.Controls.Add(split, 0, 1);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(6, 5, 0, 0) };
        footer.Controls.Add(_status);
        var refresh = new GlowButton("重新扫描", Theme.Palette, primary: true);
        refresh.Click += async (_, _) =>
        {
            _scanner.ClearCache();
            await RefreshAsync(CancellationToken.None);
        };
        var open = new GlowButton("打开编辑器", Theme.Palette);
        open.Click += (_, _) => OpenSelected();
        var reveal = new GlowButton("定位文件", Theme.Palette);
        reveal.Click += (_, _) => RevealSelected();
        footer.Controls.AddRange([refresh, open, reveal]);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
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
                : "该项目是链接/目录，无法直接预览。";
        }
        catch (Exception ex)
        {
            _preview.Text = $"读取失败：{ex.Message}";
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
