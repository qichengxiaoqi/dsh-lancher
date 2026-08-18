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
    private LauncherText _text;
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> _fieldLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GlowButton> _browseButtons = new(StringComparer.Ordinal);
    private readonly Label _validationLabel;
    private Label? _cardTitle;
    private GlowButton? _saveButton;
    private GlowButton? _detectButton;
    private GlowButton? _resetButton;

    public MaintenancePage(
        LauncherSettings settings,
        LauncherSettingsStore store,
        PathValidator validator,
        LauncherPathDiscovery pathDiscovery,
        Func<LauncherSettings, Task> onSaved,
        ThemeManager theme,
        LauncherText? text = null)
        : base(
            theme,
            text?.Maintenance ?? LauncherTextCatalog.Get(LauncherLanguage.System).Maintenance,
            text?.Pick("集中管理 DSH 源码、Profile、插件和工具链路径。", "Manage DSH source, Profile, plugin and toolchain paths.")
                ?? "集中管理 DSH 源码、Profile、插件和工具链路径。")
    {
        _text = text ?? LauncherTextCatalog.Get(LauncherLanguage.System);
        _settings = settings;
        _store = store;
        _validator = validator;
        _pathDiscovery = pathDiscovery;
        _onSaved = onSaved;
        _validationLabel = MutedLabel(_text.Pick("保存前会验证路径和工具链", "Paths and toolchain are validated before saving"));
        Build();
        ApplyLanguage(_text);
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
        AddRow(table, _text.Pick("DSH 源码目录", "DSH source directory"), "DshRoot", _settings.Paths.DshRoot, folder: true);
        AddRow(table, _text.Pick("服务脚本", "Service script"), "ServiceScript", _settings.Paths.ServiceScript, folder: false);
        AddRow(table, "Web URL", "WebUrl", _settings.Paths.WebUrl, folder: false);
        AddRow(table, _text.Pick("Web 端口", "Web port"), "Port", _settings.Paths.Port.ToString(), folder: false);
        AddRow(table, "DSH Home", "DshHome", _settings.Paths.DshHome, folder: true);
        AddRow(table, _text.Pick("Profile 目录", "Profile directory"), "ProfileDirectory", _settings.Paths.ProfileDirectory, folder: true);
        AddRow(table, _text.Pick("插件根目录", "Plugin root"), "PluginRoot", _settings.Paths.PluginRoot, folder: true);
        AddRow(table, "pnpm store", "PnpmStore", _settings.Paths.PnpmStore, folder: true);
        AddRow(table, "PowerShell", "PowerShellPath", _settings.Paths.PowerShellPath, folder: false);
        AddRow(table, "Git", "GitExecutable", _settings.Paths.GitExecutable, folder: false);
        AddRow(table, "pnpm", "PnpmExecutable", _settings.Paths.PnpmExecutable, folder: false);
        scroll.Controls.Add(table);
        var card = Card(scroll, _text.Pick("路径与工具链", "Paths and toolchain"));
        _cardTitle = card.Controls.OfType<Label>().FirstOrDefault();
        layout.Controls.Add(card, 0, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(6, 8, 0, 0),
            BackColor = Theme.Palette.Background
        };
        footer.Controls.Add(_validationLabel);
        _saveButton = new GlowButton(_text.Pick("验证并保存", "Validate and save"), Theme.Palette, primary: true);
        _saveButton.Click += SaveAsync;
        _detectButton = new GlowButton(_text.Pick("自动检测并应用", "Detect and apply"), Theme.Palette);
        _detectButton.Click += AutoDetectAsync;
        _resetButton = new GlowButton(_text.Pick("重新载入", "Reload"), Theme.Palette);
        _resetButton.Click += (_, _) => LoadSettings(_settings);
        footer.Controls.Add(_saveButton);
        footer.Controls.Add(_detectButton);
        footer.Controls.Add(_resetButton);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
    }

    private void AddRow(TableLayoutPanel table, string label, string key, string value, bool folder)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Palette.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        table.Controls.Add(labelControl, 0, row);
        _fieldLabels[key] = labelControl;
        var textBox = new TextBox { Text = value, Dock = DockStyle.Fill, Margin = new Padding(4, 7, 4, 7) };
        _fields[key] = textBox;
        table.Controls.Add(textBox, 1, row);
        var browse = new GlowButton(folder ? _text.Pick("浏览", "Browse") : _text.Pick("打开", "Open"), Theme.Palette) { MinimumSize = new Size(70, 34), Tag = folder };
        browse.Click += (_, _) => Browse(key, folder);
        _browseButtons[key] = browse;
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
            using var dialog = new OpenFileDialog { FileName = _fields[key].Text, Filter = _text.Pick("所有文件|*.*", "All files|*.*") };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _fields[key].Text = dialog.FileName;
        }
    }

    private async void SaveAsync(object? sender, EventArgs e)
    {
        if (!int.TryParse(_fields["Port"].Text, out var port))
        {
            _validationLabel.Text = _text.Pick("端口必须是数字", "Port must be a number");
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
        _validationLabel.Text = _text.Pick("已保存；路径将在重启 dsh++ 后用于服务操作", "Saved. The paths will be used for service operations after dsh++ restarts.");
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
            _validationLabel.Text = _text.Pick("已自动检测并应用路径；下次启动仍会自动重新检测", "Paths detected and applied. They will be detected again on the next launch.");
            _validationLabel.ForeColor = Theme.Palette.Success;
            await _onSaved(next);
        }
        catch (Exception ex)
        {
            _validationLabel.Text = _text.Pick($"自动检测失败：{ex.Message}", $"Automatic detection failed: {ex.Message}");
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
            ? _text.Pick("当前使用自动探测路径", "Using automatically detected paths")
            : _text.Pick("当前使用手动覆盖路径", "Using manually overridden paths");
        _validationLabel.ForeColor = Theme.Palette.Muted;
    }

    public override void ApplyLanguage(LauncherText text)
    {
        _text = text;
        ApplyHeader(
            text.Maintenance,
            text.Pick("集中管理 DSH 源码、Profile、插件和工具链路径。", "Manage DSH source, Profile, plugin and toolchain paths."));
        SetFieldLabel("DshRoot", text.Pick("DSH 源码目录", "DSH source directory"));
        SetFieldLabel("ServiceScript", text.Pick("服务脚本", "Service script"));
        SetFieldLabel("WebUrl", "Web URL");
        SetFieldLabel("Port", text.Pick("Web 端口", "Web port"));
        SetFieldLabel("DshHome", "DSH Home");
        SetFieldLabel("ProfileDirectory", text.Pick("Profile 目录", "Profile directory"));
        SetFieldLabel("PluginRoot", text.Pick("插件根目录", "Plugin root"));
        SetFieldLabel("PnpmStore", "pnpm store");
        SetFieldLabel("PowerShellPath", "PowerShell");
        SetFieldLabel("GitExecutable", "Git");
        SetFieldLabel("PnpmExecutable", "pnpm");
        if (_cardTitle is not null)
            _cardTitle.Text = text.Pick("路径与工具链", "Paths and toolchain");
        if (_saveButton is not null)
            _saveButton.Text = text.Pick("验证并保存", "Validate and save");
        if (_detectButton is not null)
            _detectButton.Text = text.Pick("自动检测并应用", "Detect and apply");
        if (_resetButton is not null)
            _resetButton.Text = text.Pick("重新载入", "Reload");
        foreach (var (key, button) in _browseButtons)
            button.Text = button.Tag is true ? text.Pick("浏览", "Browse") : text.Pick("打开", "Open");
        _validationLabel.Text = _settings.AutoDetectPaths
            ? text.Pick("当前使用自动探测路径", "Using automatically detected paths")
            : text.Pick("当前使用手动覆盖路径", "Using manually overridden paths");
    }

    private void SetFieldLabel(string key, string value)
    {
        if (_fieldLabels.TryGetValue(key, out var label))
            label.Text = value;
    }
}
