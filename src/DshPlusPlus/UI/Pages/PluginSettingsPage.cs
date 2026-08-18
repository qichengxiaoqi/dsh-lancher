using System.Drawing;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Pages;

public sealed class PluginSettingsPage : PageBase
{
    private LauncherPaths _paths;
    private SkillPathSet _skillPaths;
    private LauncherText _text;
    private readonly PluginInventoryService _inventory;
    private readonly SkillInventoryService _skillInventory;
    private readonly SkillImportService _skillImporter;
    private readonly ProfilePatchService _patchService;
    private readonly IDshServiceController _serviceController;
    private readonly ServiceStatusProbe _statusProbe;
    private readonly DataGridView _grid = new();
    private readonly DataGridView _skillGrid = new();
    private readonly Label _status;
    private readonly Label _skillStatus;
    private readonly Label _skillPathLabel;
    private readonly GlowButton _toggleButton;
    private readonly GlowButton _scanSkillsButton;
    private readonly GlowButton _importSkillsButton;
    private Label? _skillCardTitle;
    private Label? _pluginCardTitle;
    private GlowButton? _refreshButton;
    private IReadOnlyList<PluginInfo> _plugins = [];
    private IReadOnlyList<SkillInfo> _skills = [];
    private bool _skillsLoaded;

    public PluginSettingsPage(
        LauncherPaths paths,
        PluginInventoryService inventory,
        SkillPathSet skillPaths,
        SkillInventoryService skillInventory,
        SkillImportService skillImporter,
        ProfilePatchService patchService,
        IDshServiceController serviceController,
        ServiceStatusProbe statusProbe,
        ThemeManager theme,
        LauncherText? text = null)
        : base(
            theme,
            LauncherTextCatalog.Get(LauncherLanguage.System).PluginSettings,
            LauncherTextCatalog.Get(LauncherLanguage.System).Pick(
                "扫描 Profile、第三方插件和运行时 Loader 状态，并安全切换启用状态。",
                "Scan the Profile, third-party plugins and runtime Loader state, then safely toggle plugins."))
    {
        _paths = paths;
        _skillPaths = skillPaths;
        _text = text ?? LauncherTextCatalog.Get(LauncherLanguage.System);
        _inventory = inventory;
        _skillInventory = skillInventory;
        _skillImporter = skillImporter;
        _patchService = patchService;
        _serviceController = serviceController;
        _statusProbe = statusProbe;
        _status = MutedLabel(_text.Pick("尚未扫描", "Not scanned"));
        _toggleButton = new GlowButton(_text.Pick("启用/禁用", "Enable/disable"), theme.Palette, primary: true) { Width = 110, Enabled = false };
        _skillStatus = MutedLabel(_text.SkillNotScanned);
        _skillPathLabel = MutedLabel(string.Empty);
        _skillPathLabel.Dock = DockStyle.Fill;
        _scanSkillsButton = new GlowButton(_text.SkillScan, theme.Palette);
        _importSkillsButton = new GlowButton(_text.SkillImportSelected, theme.Palette, primary: true) { Enabled = false };
        Build();
    }

    public override bool SupportsAutoRefresh => true;

    public void UpdatePaths(LauncherPaths paths)
    {
        _paths = paths;
        _inventory.UpdatePaths(paths);
    }

    public void UpdateSkillPaths(SkillPathSet paths)
    {
        _skillPaths = paths;
        _skillInventory.UpdateSettings(new SkillImportSettings
        {
            CodexSkillsDirectory = paths.Codex,
            ClaudeSkillsDirectory = paths.ClaudeCode,
            DshSkillsDirectory = paths.DshTarget
        });
        _skillImporter.UpdatePaths(paths);
        _skillsLoaded = false;
        UpdateSkillPathLabel();
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _status.Text = _text.Pick("扫描中...", "Scanning...");
        _plugins = await _inventory.ScanAsync(cancellationToken);
        _grid.Rows.Clear();
        foreach (var plugin in _plugins)
        {
            var row = _grid.Rows[_grid.Rows.Add()];
            row.Tag = plugin;
            row.Cells[0].Value = plugin.Name;
            row.Cells[1].Value = string.IsNullOrWhiteSpace(plugin.Version) ? "运行时" : plugin.Version;
            row.Cells[2].Value = plugin.Enabled switch
            {
                true => _text.Pick("已启用", "Enabled"),
                false => _text.Pick("已禁用", "Disabled"),
                _ => _text.Pick("未知", "Unknown")
            };
            row.Cells[3].Value = plugin.FiberPhase ?? (plugin.RuntimeAvailable ? _text.Pick("未加载", "Not loaded") : _text.Pick("未连接", "Disconnected"));
            row.Cells[4].Value = plugin.SourceKind.ToString();
            row.Cells[5].Value = plugin.SourcePath;
        }
        _status.Text = _text.Pick(
            $"已发现 {_plugins.Count} 个插件；当前 Profile：{_paths.ProfileName}",
            $"Found {_plugins.Count} plugins. Current Profile: {_paths.ProfileName}");
        UpdateToggleState();
        if (!_skillsLoaded)
            await ScanSkillsAsync(cancellationToken);
    }

    private void Build()
    {
        var layout = CreatePageLayout(4);
        layout.RowStyles[1] = new RowStyle(SizeType.Percent, 46);
        layout.RowStyles[2] = new RowStyle(SizeType.Percent, 54);
        layout.RowStyles[3] = new RowStyle(SizeType.AutoSize);

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
        BuildSkillGrid();
        var pluginCard = Card(_grid, _text.Pick("已安装与运行时插件", "Installed and runtime plugins"));
        _pluginCardTitle = pluginCard.Controls.OfType<Label>().FirstOrDefault();
        layout.Controls.Add(pluginCard, 0, 1);

        layout.Controls.Add(BuildSkillCard(), 0, 2);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(6, 5, 0, 0) };
        footer.Controls.Add(_status);
        _refreshButton = new GlowButton(_text.Pick("重新扫描", "Rescan"), Theme.Palette);
        _refreshButton.Click += async (_, _) => await RefreshAsync(CancellationToken.None);
        _toggleButton.Click += ToggleSelectedAsync;
        _scanSkillsButton.Click += async (_, _) => await ScanSkillsAsync(CancellationToken.None);
        _importSkillsButton.Click += ImportSelectedSkillsAsync;
        footer.Controls.Add(_refreshButton);
        footer.Controls.Add(_toggleButton);
        footer.Controls.Add(_skillStatus);
        footer.Controls.Add(_scanSkillsButton);
        footer.Controls.Add(_importSkillsButton);
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
    }

    private void BuildSkillGrid()
    {
        _skillGrid.Dock = DockStyle.Fill;
        _skillGrid.AllowUserToAddRows = false;
        _skillGrid.AllowUserToDeleteRows = false;
        _skillGrid.ReadOnly = false;
        _skillGrid.RowHeadersVisible = false;
        _skillGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _skillGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _skillGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _skillGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

        _skillGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "select",
            HeaderText = _text.SkillSelectColumn,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 60,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _skillGrid.Columns.Add("name", _text.SkillNameColumn);
        _skillGrid.Columns.Add("description", _text.SkillDescriptionColumn);
        _skillGrid.Columns.Add("source", _text.SkillSourceColumn);
        _skillGrid.Columns.Add("state", _text.SkillStateColumn);
        _skillGrid.Columns.Add("target", _text.SkillTargetColumn);
        _skillGrid.Columns.Add("warning", _text.SkillNoteColumn);
        foreach (DataGridViewColumn column in _skillGrid.Columns)
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.FillWeight = column.Index switch
            {
                0 => 7,
                1 => 14,
                2 => 25,
                3 => 13,
                4 => 13,
                5 => 20,
                _ => 18
            };
            column.MinimumWidth = column.Index == 0 ? 60 : 80;
            column.ReadOnly = column.Index != 0;
        }
        _skillGrid.CellValueChanged += (_, _) => UpdateSkillImportState();
        _skillGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_skillGrid.IsCurrentCellDirty)
                _skillGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _skillGrid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                e.ToolTipText = _skillGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? string.Empty;
        };
    }

    private Panel BuildSkillCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Palette.Surface
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(_skillPathLabel, 0, 0);
        content.Controls.Add(_skillGrid, 0, 1);
        UpdateSkillPathLabel();
        var card = Card(content, _text.SkillCardTitle);
        _skillCardTitle = card.Controls.OfType<Label>().FirstOrDefault();
        return card;
    }

    private void UpdateSkillPathLabel()
    {
        if (_skillPathLabel is null)
            return;
        _skillPathLabel.Text = string.Format(
            _text.SkillPathFormat,
            _skillPaths.Codex,
            _skillPaths.ClaudeCode,
            _skillPaths.DshTarget);
    }

    private async Task ScanSkillsAsync(CancellationToken cancellationToken)
    {
        _scanSkillsButton.Enabled = false;
        _importSkillsButton.Enabled = false;
        _skillStatus.Text = _text.SkillScanning;
        try
        {
            _skills = await _skillInventory.ScanAsync(cancellationToken);
            _skillGrid.Rows.Clear();
            foreach (var skill in _skills)
            {
                var row = _skillGrid.Rows[_skillGrid.Rows.Add()];
                row.Tag = skill;
                row.Cells[0].Value = false;
                row.Cells[0].ReadOnly = !SkillImportService.IsSelectable(skill);
                row.Cells[1].Value = skill.Name;
                row.Cells[2].Value = skill.Description;
                row.Cells[3].Value = skill.SourceKind.ToString();
                row.Cells[4].Value = DescribeSkillState(skill.State);
                row.Cells[5].Value = skill.TargetPath;
                row.Cells[6].Value = skill.Warning;
            }
            _skillsLoaded = true;
            _skillStatus.Text = _text.SkillFound(_skills.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _skillStatus.Text = _text.SkillScanCanceled;
        }
        catch (Exception exception)
        {
            _skillStatus.Text = _text.SkillScanFailed(exception.Message);
            _skillStatus.ForeColor = Theme.Palette.Danger;
        }
        finally
        {
            _scanSkillsButton.Enabled = true;
            UpdateSkillImportState();
        }
    }

    private async void ImportSelectedSkillsAsync(object? sender, EventArgs e)
    {
        var selected = _skillGrid.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Cells[0].Value is true)
            .Select(row => row.Tag as SkillInfo)
            .Where(skill => skill is not null && SkillImportService.IsSelectable(skill))
            .Cast<SkillInfo>()
            .ToArray();
        if (selected.Length == 0)
        {
            _skillStatus.Text = _text.SkillSelectAtLeastOne;
            return;
        }

        if (selected.Any(skill => skill.State == SkillImportState.Conflict)
            && MessageBox.Show(
                this,
                _text.SkillConflictPrompt,
                _text.SkillConflictTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _importSkillsButton.Enabled = false;
        _scanSkillsButton.Enabled = false;
        var succeeded = 0;
        var failures = 0;
        try
        {
            foreach (var skill in selected)
            {
                var result = await _skillImporter.ImportAsync(skill, CancellationToken.None);
                if (result.Succeeded) succeeded++; else failures++;
            }
            _skillStatus.ForeColor = failures == 0 ? Theme.Palette.Success : Theme.Palette.Danger;
            _skillStatus.Text = _text.SkillImportResult(succeeded, failures);
            _skillsLoaded = false;
            await ScanSkillsAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _skillStatus.ForeColor = Theme.Palette.Danger;
            _skillStatus.Text = _text.SkillImportFailed(exception.Message);
        }
        finally
        {
            _scanSkillsButton.Enabled = true;
            UpdateSkillImportState();
        }
    }

    private void UpdateSkillImportState()
    {
        _importSkillsButton.Enabled = _skillGrid.Rows.Cast<DataGridViewRow>()
            .Any(row => row.Cells[0].Value is true
                && row.Tag is SkillInfo skill
                && SkillImportService.IsSelectable(skill));
    }

    private string DescribeSkillState(SkillImportState state) => state switch
    {
        SkillImportState.New => _text == LauncherTextCatalog.English ? "New" : "新增",
        SkillImportState.SameContent => _text == LauncherTextCatalog.English ? "Same content" : "内容相同",
        SkillImportState.Conflict => _text == LauncherTextCatalog.English ? "Conflict (backup)" : "冲突（将备份）",
        SkillImportState.Invalid => _text == LauncherTextCatalog.English ? "Invalid" : "无效",
        SkillImportState.Unsupported => _text == LauncherTextCatalog.English ? "Unsupported" : "不支持",
        _ => _text == LauncherTextCatalog.English ? "Error" : "错误"
    };

    public override void ApplyLanguage(LauncherText text)
    {
        _text = text;
        ApplyHeader(
            text.PluginSettings,
            text.Pick(
                "扫描 Profile、第三方插件和运行时 Loader 状态，并安全切换启用状态。",
                "Scan the Profile, third-party plugins and runtime Loader state, then safely toggle plugins."));
        if (_pluginCardTitle is not null)
            _pluginCardTitle.Text = text.Pick("已安装与运行时插件", "Installed and runtime plugins");
        if (_refreshButton is not null)
            _refreshButton.Text = text.Pick("重新扫描", "Rescan");
        _toggleButton.Text = GetSelected()?.Enabled == false
            ? text.Pick("启用插件", "Enable plugin")
            : text.Pick("禁用插件", "Disable plugin");
        if (_grid.Columns.Count >= 6)
        {
            _grid.Columns[0].HeaderText = text.Pick("插件", "Plugin");
            _grid.Columns[1].HeaderText = text.Pick("版本", "Version");
            _grid.Columns[2].HeaderText = text.Pick("状态", "Status");
            _grid.Columns[3].HeaderText = text.Pick("运行阶段", "Runtime phase");
            _grid.Columns[4].HeaderText = text.Pick("来源", "Source");
            _grid.Columns[5].HeaderText = text.Pick("路径", "Path");
        }
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not PluginInfo plugin)
                continue;
            row.Cells[2].Value = plugin.Enabled switch
            {
                true => text.Pick("已启用", "Enabled"),
                false => text.Pick("已禁用", "Disabled"),
                _ => text.Pick("未知", "Unknown")
            };
            if (plugin.FiberPhase is null)
                row.Cells[3].Value = plugin.RuntimeAvailable ? text.Pick("未加载", "Not loaded") : text.Pick("未连接", "Disconnected");
        }
        _skillStatus.Text = _skillsLoaded ? text.SkillFound(_skills.Count) : text.SkillNotScanned;
        _skillPathLabel.Text = string.Format(
            text.SkillPathFormat,
            _skillPaths.Codex,
            _skillPaths.ClaudeCode,
            _skillPaths.DshTarget);
        _scanSkillsButton.Text = text.SkillScan;
        _importSkillsButton.Text = text.SkillImportSelected;
        if (_skillCardTitle is not null)
            _skillCardTitle.Text = text.SkillCardTitle;
        if (_skillGrid.Columns.Count >= 7)
        {
            _skillGrid.Columns[0].HeaderText = text.SkillSelectColumn;
            _skillGrid.Columns[1].HeaderText = text.SkillNameColumn;
            _skillGrid.Columns[2].HeaderText = text.SkillDescriptionColumn;
            _skillGrid.Columns[3].HeaderText = text.SkillSourceColumn;
            _skillGrid.Columns[4].HeaderText = text.SkillStateColumn;
            _skillGrid.Columns[5].HeaderText = text.SkillTargetColumn;
            _skillGrid.Columns[6].HeaderText = text.SkillNoteColumn;
        }
        foreach (DataGridViewRow row in _skillGrid.Rows)
        {
            if (row.Tag is SkillInfo skill)
                row.Cells[4].Value = DescribeSkillState(skill.State);
        }
    }

    private void UpdateToggleState()
    {
        var plugin = GetSelected();
        _toggleButton.Enabled = plugin is not null && plugin.ConfigId is not null;
        _toggleButton.Text = plugin?.Enabled == false
            ? _text.Pick("启用插件", "Enable plugin")
            : _text.Pick("禁用插件", "Disable plugin");
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
        if (running && MessageBox.Show(
                this,
                _text.Pick("插件配置已更新，DSH 正在运行。现在重启服务使其生效吗？", "Plugin configuration updated while DSH is running. Restart the service now to apply it?"),
                _text.Pick("插件状态已修改", "Plugin state changed"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            var restart = await _serviceController.RestartAsync(CancellationToken.None);
            _status.Text = restart.Succeeded
                ? _text.Pick("插件状态已应用，DSH 已重启", "Plugin state applied; DSH restarted")
                : _text.Pick($"插件已写入，但重启失败：{restart.CombinedOutput}", $"Plugin was written, but restart failed: {restart.CombinedOutput}");
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
