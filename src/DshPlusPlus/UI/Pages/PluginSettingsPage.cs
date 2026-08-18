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
        ThemeManager theme)
        : base(theme, "插件设置", "扫描 Profile、第三方插件和运行时 Loader 状态，并安全切换启用状态。")
    {
        _paths = paths;
        _skillPaths = skillPaths;
        _inventory = inventory;
        _skillInventory = skillInventory;
        _skillImporter = skillImporter;
        _patchService = patchService;
        _serviceController = serviceController;
        _statusProbe = statusProbe;
        _status = MutedLabel("尚未扫描");
        _toggleButton = new GlowButton("启用/禁用", theme.Palette, primary: true) { Width = 110, Enabled = false };
        _skillStatus = MutedLabel("Skills not scanned.");
        _skillPathLabel = MutedLabel(string.Empty);
        _skillPathLabel.Dock = DockStyle.Fill;
        _scanSkillsButton = new GlowButton("Scan skills", theme.Palette);
        _importSkillsButton = new GlowButton("Import selected", theme.Palette, primary: true) { Enabled = false };
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
        layout.Controls.Add(Card(_grid, "已安装与运行时插件"), 0, 1);

        layout.Controls.Add(BuildSkillCard(), 0, 2);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(6, 5, 0, 0) };
        footer.Controls.Add(_status);
        var refresh = new GlowButton("重新扫描", Theme.Palette);
        refresh.Click += async (_, _) => await RefreshAsync(CancellationToken.None);
        _toggleButton.Click += ToggleSelectedAsync;
        _scanSkillsButton.Click += async (_, _) => await ScanSkillsAsync(CancellationToken.None);
        _importSkillsButton.Click += ImportSelectedSkillsAsync;
        footer.Controls.Add(refresh);
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
            HeaderText = "Import",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 60,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _skillGrid.Columns.Add("name", "Name");
        _skillGrid.Columns.Add("description", "Description");
        _skillGrid.Columns.Add("source", "Source");
        _skillGrid.Columns.Add("state", "State");
        _skillGrid.Columns.Add("target", "Target");
        _skillGrid.Columns.Add("warning", "Note");
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
        return Card(content, "Skills from Codex / Claude Code");
    }

    private void UpdateSkillPathLabel()
    {
        if (_skillPathLabel is null)
            return;
        _skillPathLabel.Text =
            $"Codex: {_skillPaths.Codex}  |  Claude: {_skillPaths.ClaudeCode}  |  DSH target: {_skillPaths.DshTarget}";
    }

    private async Task ScanSkillsAsync(CancellationToken cancellationToken)
    {
        _scanSkillsButton.Enabled = false;
        _importSkillsButton.Enabled = false;
        _skillStatus.Text = "Scanning Codex and Claude Code skills...";
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
            _skillStatus.Text = $"Found {_skills.Count} skills. Select New or Conflict items to import.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _skillStatus.Text = "Skill scan canceled.";
        }
        catch (Exception exception)
        {
            _skillStatus.Text = $"Skill scan failed: {exception.Message}";
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
            _skillStatus.Text = "Select at least one New or Conflict skill.";
            return;
        }

        if (selected.Any(skill => skill.State == SkillImportState.Conflict)
            && MessageBox.Show(
                this,
                "Some target skills already exist. A timestamped backup will be created before replacement. Continue?",
                "Confirm skill replacement",
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
            _skillStatus.Text = $"Imported {succeeded}; failed {failures}. Restart DSH if a new skill is not visible.";
            _skillsLoaded = false;
            await ScanSkillsAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _skillStatus.ForeColor = Theme.Palette.Danger;
            _skillStatus.Text = $"Skill import failed: {exception.Message}";
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

    private static string DescribeSkillState(SkillImportState state) => state switch
    {
        SkillImportState.New => "New",
        SkillImportState.SameContent => "Same content",
        SkillImportState.Conflict => "Conflict (backup)",
        SkillImportState.Invalid => "Invalid",
        SkillImportState.Unsupported => "Unsupported",
        _ => "Error"
    };

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
