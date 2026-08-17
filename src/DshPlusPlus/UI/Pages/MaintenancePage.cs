using System.Drawing;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Pages;

public sealed class MaintenancePage : PageBase
{
    private readonly LauncherSettingsStore _store;
    private readonly PathValidator _validator;
    private readonly LauncherPathDiscovery _pathDiscovery;
    private readonly Func<LauncherSettings, Task> _onSaved;
    private LauncherSettings _settings;
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private readonly Label _validationLabel;

    public MaintenancePage(
        LauncherSettings settings,
        LauncherSettingsStore store,
        PathValidator validator,
        LauncherPathDiscovery pathDiscovery,
        Func<LauncherSettings, Task> onSaved,
        ThemeManager theme)
        : base(theme, "安装维护", "集中管理 DSH 源码、Profile、插件和工具链路径。")
    {
        _settings = settings;
        _store = store;
        _validator = validator;
        _pathDiscovery = pathDiscovery;
        _onSaved = onSaved;
        _validationLabel = MutedLabel("保存前会验证路径和工具链");
        Build();
    }

    private void Build()
    {
        var layout = CreatePageLayout(3);
        layout.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
        layout.RowStyles[2] = new RowStyle(SizeType.AutoSize);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Palette.Background };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(6),
            BackColor = Theme.Palette.Surface
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        AddRow(table, "DSH 源码目录", "DshRoot", _settings.Paths.DshRoot, folder: true);
        AddRow(table, "服务脚本", "ServiceScript", _settings.Paths.ServiceScript, folder: false);
        AddRow(table, "Web URL", "WebUrl", _settings.Paths.WebUrl, folder: false);
        AddRow(table, "Web 端口", "Port", _settings.Paths.Port.ToString(), folder: false);
        AddRow(table, "DSH Home", "DshHome", _settings.Paths.DshHome, folder: true);
        AddRow(table, "Profile 目录", "ProfileDirectory", _settings.Paths.ProfileDirectory, folder: true);
        AddRow(table, "插件根目录", "PluginRoot", _settings.Paths.PluginRoot, folder: true);
        AddRow(table, "pnpm store", "PnpmStore", _settings.Paths.PnpmStore, folder: true);
        AddRow(table, "PowerShell", "PowerShellPath", _settings.Paths.PowerShellPath, folder: false);
        AddRow(table, "Git", "GitExecutable", _settings.Paths.GitExecutable, folder: false);
        AddRow(table, "pnpm", "PnpmExecutable", _settings.Paths.PnpmExecutable, folder: false);
        scroll.Controls.Add(table);
        layout.Controls.Add(Card(scroll, "路径与工具链"), 0, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(6, 8, 0, 0),
            BackColor = Theme.Palette.Background
        };
        footer.Controls.Add(_validationLabel);
        var save = new GlowButton("验证并保存", Theme.Palette, primary: true);
        save.Click += SaveAsync;
        var detect = new GlowButton("自动检测并应用", Theme.Palette);
        detect.Click += AutoDetectAsync;
        var reset = new GlowButton("重新载入", Theme.Palette);
        reset.Click += (_, _) => LoadSettings(_settings);
        footer.Controls.Add(save);
        footer.Controls.Add(detect);
        footer.Controls.Add(reset);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
    }

    private void AddRow(TableLayoutPanel table, string label, string key, string value, bool folder)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Palette.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        var textBox = new TextBox { Text = value, Dock = DockStyle.Fill, Margin = new Padding(4, 7, 4, 7) };
        _fields[key] = textBox;
        table.Controls.Add(textBox, 1, row);
        var browse = new GlowButton(folder ? "浏览" : "打开", Theme.Palette) { MinimumSize = new Size(70, 34) };
        browse.Click += (_, _) => Browse(key, folder);
        table.Controls.Add(browse, 2, row);
    }

    private void Browse(string key, bool folder)
    {
        if (folder)
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _fields[key].Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _fields[key].Text = dialog.SelectedPath;
            return;
        }

        if (key is "ServiceScript" or "PowerShellPath")
        {
            using var dialog = new OpenFileDialog { FileName = _fields[key].Text, Filter = "所有文件|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _fields[key].Text = dialog.FileName;
        }
    }

    private async void SaveAsync(object? sender, EventArgs e)
    {
        if (!int.TryParse(_fields["Port"].Text, out var port))
        {
            _validationLabel.Text = "端口必须是数字";
            _validationLabel.ForeColor = Theme.Palette.Danger;
            return;
        }

        var old = _settings.Paths;
        var paths = old with
        {
            DshRoot = _fields["DshRoot"].Text.Trim(),
            ServiceScript = _fields["ServiceScript"].Text.Trim(),
            WebUrl = _fields["WebUrl"].Text.Trim(),
            Port = port,
            DshHome = _fields["DshHome"].Text.Trim(),
            ProfileDirectory = _fields["ProfileDirectory"].Text.Trim(),
            PluginRoot = _fields["PluginRoot"].Text.Trim(),
            PnpmStore = _fields["PnpmStore"].Text.Trim(),
            PowerShellPath = _fields["PowerShellPath"].Text.Trim(),
            GitExecutable = _fields["GitExecutable"].Text.Trim(),
            PnpmExecutable = _fields["PnpmExecutable"].Text.Trim()
        };
        var validation = _validator.Validate(paths);
        if (!validation.IsValid)
        {
            _validationLabel.Text = string.Join(" | ", validation.Errors);
            _validationLabel.ForeColor = Theme.Palette.Danger;
            return;
        }

        var next = _settings with
        {
            AutoDetectPaths = false,
            Paths = paths
        };
        await _store.SaveAsync(next, CancellationToken.None);
        _settings = next;
        _validationLabel.Text = "已保存；路径将在重启 dsh++ 后用于服务操作";
        _validationLabel.ForeColor = Theme.Palette.Success;
        await _onSaved(next);
    }

    private async void AutoDetectAsync(object? sender, EventArgs e)
    {
        try
        {
            var next = _settings with
            {
                AutoDetectPaths = true,
                Paths = _pathDiscovery.Discover()
            };
            await _store.SaveAsync(next, CancellationToken.None);
            LoadSettings(next);
            _validationLabel.Text = "已自动检测并应用路径；下次启动仍会自动重新检测";
            _validationLabel.ForeColor = Theme.Palette.Success;
            await _onSaved(next);
        }
        catch (Exception ex)
        {
            _validationLabel.Text = $"自动检测失败：{ex.Message}";
            _validationLabel.ForeColor = Theme.Palette.Danger;
        }
    }

    private void LoadSettings(LauncherSettings settings)
    {
        _settings = settings;
        var paths = settings.Paths;
        _fields["DshRoot"].Text = paths.DshRoot;
        _fields["ServiceScript"].Text = paths.ServiceScript;
        _fields["WebUrl"].Text = paths.WebUrl;
        _fields["Port"].Text = paths.Port.ToString();
        _fields["DshHome"].Text = paths.DshHome;
        _fields["ProfileDirectory"].Text = paths.ProfileDirectory;
        _fields["PluginRoot"].Text = paths.PluginRoot;
        _fields["PnpmStore"].Text = paths.PnpmStore;
        _fields["PowerShellPath"].Text = paths.PowerShellPath;
        _fields["GitExecutable"].Text = paths.GitExecutable;
        _fields["PnpmExecutable"].Text = paths.PnpmExecutable;
        _validationLabel.Text = settings.AutoDetectPaths
            ? "当前使用自动探测路径"
            : "当前使用手动覆盖路径";
        _validationLabel.ForeColor = Theme.Palette.Muted;
    }
}
