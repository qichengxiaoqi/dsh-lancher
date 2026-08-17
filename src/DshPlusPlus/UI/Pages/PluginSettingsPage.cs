using System.Drawing;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Pages;

public sealed class PluginSettingsPage : PageBase
{
    private readonly LauncherPaths _paths;
    private readonly PluginInventoryService _inventory;
    private readonly ProfilePatchService _patchService;
    private readonly IDshServiceController _serviceController;
    private readonly ServiceStatusProbe _statusProbe;
    private readonly DataGridView _grid = new();
    private readonly Label _status;
    private readonly GlowButton _toggleButton;
    private IReadOnlyList<PluginInfo> _plugins = [];

    public PluginSettingsPage(
        LauncherPaths paths,
        PluginInventoryService inventory,
        ProfilePatchService patchService,
        IDshServiceController serviceController,
        ServiceStatusProbe statusProbe,
        ThemeManager theme)
        : base(theme, "插件设置", "扫描 Profile、第三方插件和运行时 Loader 状态，并安全切换启用状态。")
    {
        _paths = paths;
        _inventory = inventory;
        _patchService = patchService;
        _serviceController = serviceController;
        _statusProbe = statusProbe;
        _status = MutedLabel("尚未扫描");
        _toggleButton = new GlowButton("启用/禁用", theme.Palette, primary: true) { Width = 110, Enabled = false };
        Build();
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _status.Text = "扫描中...";
        _plugins = await _inventory.ScanAsync(cancellationToken);
        _grid.Rows.Clear();
        foreach (var plugin in _plugins)
        {
            var row = _grid.Rows[_grid.Rows.Add()];
            row.Tag = plugin;
            row.Cells[0].Value = plugin.Name;
            row.Cells[1].Value = string.IsNullOrWhiteSpace(plugin.Version) ? "运行时" : plugin.Version;
            row.Cells[2].Value = plugin.Enabled switch { true => "已启用", false => "已禁用", _ => "未知" };
            row.Cells[3].Value = plugin.FiberPhase ?? (plugin.RuntimeAvailable ? "未加载" : "未连接");
            row.Cells[4].Value = plugin.SourceKind.ToString();
            row.Cells[5].Value = plugin.SourcePath;
        }
        _status.Text = $"已发现 {_plugins.Count} 个插件；当前 Profile：{_paths.ProfileName}";
        UpdateToggleState();
    }

    private void Build()
    {
        var layout = CreatePageLayout(3);
        layout.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
        layout.RowStyles[2] = new RowStyle(SizeType.AutoSize);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.Columns.Add("name", "插件");
        _grid.Columns.Add("version", "版本");
        _grid.Columns.Add("enabled", "状态");
        _grid.Columns.Add("phase", "运行阶段");
        _grid.Columns.Add("kind", "来源");
        _grid.Columns.Add("path", "路径");
        var widths = new[] { 0.20f, 0.12f, 0.13f, 0.16f, 0.13f, 0.26f };
        for (var index = 0; index < _grid.Columns.Count; index++)
        {
            _grid.Columns[index].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns[index].FillWeight = widths[index] * 100;
            _grid.Columns[index].MinimumWidth = index == 5 ? 160 : 70;
        }
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                e.ToolTipText = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? string.Empty;
        };
        _grid.SelectionChanged += (_, _) => UpdateToggleState();
        layout.Controls.Add(Card(_grid, "已安装与运行时插件"), 0, 1);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(6, 5, 0, 0) };
        footer.Controls.Add(_status);
        var refresh = new GlowButton("重新扫描", Theme.Palette);
        refresh.Click += async (_, _) => await RefreshAsync(CancellationToken.None);
        _toggleButton.Click += ToggleSelectedAsync;
        footer.Controls.Add(refresh);
        footer.Controls.Add(_toggleButton);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
    }

    private void UpdateToggleState()
    {
        var plugin = GetSelected();
        _toggleButton.Enabled = plugin is not null && plugin.ConfigId is not null;
        _toggleButton.Text = plugin?.Enabled == false ? "启用插件" : "禁用插件";
    }

    private async void ToggleSelectedAsync(object? sender, EventArgs e)
    {
        var plugin = GetSelected();
        if (plugin is null || plugin.ConfigId is null)
            return;
        var patchPath = ResolvePatchPath(plugin.ConfigId);
        var configYaml = ProfilePatchService.FindPluginConfigYaml(plugin.SourcePath, plugin.ConfigId);
        var enable = plugin.Enabled != true;
        var result = await _patchService.SetPluginEnabledAsync(
            patchPath,
            plugin.ConfigId,
            configYaml ?? $"id: {plugin.ConfigId}\nname: {plugin.Name}\n",
            enable,
            CancellationToken.None);
        _status.Text = result.Message;
        _status.ForeColor = result.Succeeded ? Theme.Palette.Success : Theme.Palette.Danger;
        if (!result.Succeeded)
            return;

        var running = (await _statusProbe.ProbeAsync(CancellationToken.None)).State == ServiceState.Running;
        if (running && MessageBox.Show(this, "插件配置已更新，DSH 正在运行。现在重启服务使其生效吗？", "插件状态已修改",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            var restart = await _serviceController.RestartAsync(CancellationToken.None);
            _status.Text = restart.Succeeded ? "插件状态已应用，DSH 已重启" : $"插件已写入，但重启失败：{restart.CombinedOutput}";
        }
        await RefreshAsync(CancellationToken.None);
    }

    private string ResolvePatchPath(string configId)
    {
        var homePatch = Path.Combine(_paths.DshHome, "cordis.patch.yml");
        if (File.Exists(homePatch) && File.ReadAllText(homePatch).Contains($"id: {configId}", StringComparison.Ordinal))
            return homePatch;
        return _paths.ProfilePatchFile;
    }

    private PluginInfo? GetSelected() =>
        _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].Tag as PluginInfo;
}
