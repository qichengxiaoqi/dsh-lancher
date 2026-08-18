using System.Drawing;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Pages;

public sealed class LauncherSettingsPage : PageBase
{
    private LauncherSettings _settings;
    private LauncherText _text;
    private readonly LauncherSettingsStore _store;
    private readonly Func<LauncherSettings, Task> _onSaved;
    private readonly LauncherUpdateService _updateService;
    private readonly Action _requestRestart;
    private readonly ComboBox _theme = new();
    private readonly TextBox _accent = new();
    private readonly CheckBox _glow = new();
    private readonly NumericUpDown _fontScale = new();
    private readonly NumericUpDown _navigationWidth = new();
    private readonly ComboBox _startPage = new();
    private readonly NumericUpDown _refreshSeconds = new();
    private readonly CheckBox _showLogs = new();
    private readonly CheckBox _closeToTray = new();
    private readonly ComboBox _language = new();
    private readonly CheckBox _autoUpdate = new();
    private readonly NumericUpDown _updateIntervalHours = new();
    private readonly TextBox _upstreamRemote = new();
    private readonly TextBox _patchBranch = new();
    private readonly Label _updateStatus;
    private readonly GlowButton _checkUpdateButton;
    private readonly GlowButton _downloadUpdateButton;
    private LauncherReleaseInfo? _availableRelease;
    private CancellationTokenSource? _updateCts;
    private readonly Label _status;
    private readonly Dictionary<string, Label> _settingLabels = new(StringComparer.Ordinal);
    private Label? _languageLabel;
    private Label? _closeBehaviorLabel;
    private Label? _navigationModeDescription;
    private Label? _isolationDescription;
    private Label? _cardTitle;
    private Label? _releaseUpdateLabel;
    private GlowButton? _saveButton;
    private GlowButton? _resetButton;

    private sealed record LanguageOption(LauncherLanguage Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record PageOption(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    public LauncherSettingsPage(
        LauncherSettings settings,
        LauncherSettingsStore store,
        Func<LauncherSettings, Task> onSaved,
        LauncherUpdateService updateService,
        Action requestRestart,
        ThemeManager theme,
        LauncherText? text = null)
        : base(
            theme,
            LauncherTextCatalog.Get(settings.Language).LauncherSettings,
            LauncherTextCatalog.Get(settings.Language).Pick(
                "定制 dsh++ 的颜色、密度、导航和启动行为；这里的更新来自 GitHub Release，不检查 DSH 源码。",
                "Customize dsh++ colors, density, navigation and startup behavior. Updates come from GitHub Releases; DSH source is not checked."))
    {
        _settings = settings;
        _text = text ?? LauncherTextCatalog.Get(settings.Language);
        _store = store;
        _onSaved = onSaved;
        _updateService = updateService;
        _requestRestart = requestRestart;
        _status = MutedLabel(_text.Pick("设置保存在当前用户的 LocalAppData", "Settings are stored in the current user's LocalAppData"));
        _updateStatus = MutedLabel(_text.Pick("尚未检查启动器更新", "Launcher update has not been checked"));
        _updateStatus.AutoSize = true;
        _checkUpdateButton = new GlowButton(_text.Pick("检查 dsh++ Release", "Check dsh++ Release"), Theme.Palette);
        _checkUpdateButton.Click += CheckUpdateAsync;
        _downloadUpdateButton = new GlowButton(_text.Pick("下载 x64 并重启", "Download x64 and restart"), Theme.Palette, primary: true)
        {
            Enabled = false
        };
        _downloadUpdateButton.Click += DownloadUpdateAsync;
        Build();
        LoadValues();
    }

    private void Build()
    {
        var layout = CreatePageLayout(3);
        layout.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
        layout.RowStyles[2] = new RowStyle(SizeType.AutoSize);
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(10),
            BackColor = Theme.Palette.Surface
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _settingLabels["theme"] = AddRow(table, _text.Pick("界面主题", "Interface theme"), _theme);
        _languageLabel = AddRow(table, _text.LanguageLabel, _language);
        _settingLabels["accent"] = AddRow(table, _text.Pick("强调色", "Accent color"), _accent);
        _settingLabels["glow"] = AddRow(table, _text.Pick("微光效果", "Glow effect"), _glow);
        _settingLabels["fontScale"] = AddRow(table, _text.Pick("字体缩放", "Font scale"), _fontScale);
        _settingLabels["navigationWidth"] = AddRow(table, _text.Pick("导航栏宽度", "Navigation width"), _navigationWidth);
        var navigationMode = MutedLabel(_text.Pick("固定展开文字导航；窄窗口通过滚动保持完整显示", "Fixed expanded text navigation; narrow windows use scrolling to keep labels visible"));
        _navigationModeDescription = navigationMode;
        _settingLabels["navigationMode"] = AddRow(table, _text.Pick("导航栏模式", "Navigation mode"), navigationMode);
        _settingLabels["startPage"] = AddRow(table, _text.Pick("默认页面", "Default page"), _startPage);
        _settingLabels["refresh"] = AddRow(table, _text.Pick("自动刷新（秒）", "Auto refresh (seconds)"), _refreshSeconds);
        _settingLabels["showLogs"] = AddRow(table, _text.Pick("显示日志抽屉", "Show log drawer"), _showLogs);
        _settingLabels["autoUpdate"] = AddRow(table, _text.Pick("自动检查更新", "Check for updates automatically"), _autoUpdate);
        _settingLabels["updateInterval"] = AddRow(table, _text.Pick("检查间隔（小时）", "Check interval (hours)"), _updateIntervalHours);
        _settingLabels["upstream"] = AddRow(table, _text.Pick("DSH 官方远程", "DSH upstream remote"), _upstreamRemote);
        _settingLabels["patchBranch"] = AddRow(table, _text.Pick("本地补丁分支", "Local patch branch"), _patchBranch);
        var isolation = MutedLabel(_text.Pick("源码补丁与插件/配置分离；dsh++ Release 更新独立执行", "Source patches stay separate from plugins/configuration; dsh++ Release updates are independent"));
        _isolationDescription = isolation;
        _settingLabels["isolation"] = AddRow(table, _text.Pick("DSH 更新隔离", "DSH update isolation"), isolation);
        _closeBehaviorLabel = AddRow(table, _text.CloseBehavior, _closeToTray);
        var updateActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(4, 4, 4, 4)
        };
        updateActions.Controls.Add(_updateStatus);
        updateActions.Controls.Add(_checkUpdateButton);
        updateActions.Controls.Add(_downloadUpdateButton);
        _releaseUpdateLabel = AddRow(table, _text.Pick("dsh++ Release 更新", "dsh++ Release updates"), updateActions);
        var customizationCard = Card(
            new Panel { Dock = DockStyle.Fill, AutoScroll = true, Controls = { table } },
            _text.Pick("界面定制", "Interface customization"));
        _cardTitle = customizationCard.Controls.OfType<Label>().FirstOrDefault();
        layout.Controls.Add(customizationCard, 0, 1);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(6, 5, 0, 0) };
        footer.Controls.Add(_status);
        _saveButton = new GlowButton(_text.Pick("保存并应用", "Save and apply"), Theme.Palette, primary: true);
        _saveButton.Click += SaveAsync;
        _resetButton = new GlowButton(_text.Pick("恢复默认", "Reset to defaults"), Theme.Palette);
        _resetButton.Click += (_, _) =>
        {
            _settings = LauncherSettings.CreateDefault();
            LoadValues();
        };
        footer.Controls.Add(_saveButton);
        footer.Controls.Add(_resetButton);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
    }

    private Label AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = Theme.Palette.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = "small"
        };
        table.Controls.Add(labelControl, 0, row);
        control.Dock = DockStyle.Left;
        control.Margin = new Padding(4, 6, 4, 6);
        table.Controls.Add(control, 1, row);
        return labelControl;
    }

    private void LoadValues()
    {
        _theme.Items.Clear();
        _theme.Items.AddRange(["Obsidian", "Light", "High Contrast"]);
        _theme.SelectedItem = _settings.Theme.Name;
        LoadLanguageOptions();
        _accent.Text = _settings.Theme.Accent;
        _glow.Text = _text.Pick("启用卡片光晕", "Enable card glow");
        _glow.Checked = _settings.Theme.Glow;
        _fontScale.Minimum = 80;
        _fontScale.Maximum = 140;
        _fontScale.Value = Math.Clamp(_settings.Theme.FontScale, 80, 140);
        _navigationWidth.Minimum = 180;
        _navigationWidth.Maximum = 320;
        _navigationWidth.Value = Math.Clamp(_settings.Theme.NavigationWidth, 180, 320);
        LoadStartPageOptions();
        _refreshSeconds.Minimum = 5;
        _refreshSeconds.Maximum = 120;
        _refreshSeconds.Value = Math.Clamp(_settings.RefreshSeconds, 5, 120);
        _showLogs.Text = _text.Pick("显示 DSH 管理日志", "Show DSH management logs");
        _showLogs.Checked = _settings.ShowLogDrawer;
        _closeToTray.Text = _text.CloseToTray;
        _closeToTray.Checked = _settings.CloseToTray;
        _autoUpdate.Text = _text.Pick("启动后低频检查 GitHub Release", "Check GitHub Releases at a low frequency after startup");
        _autoUpdate.Checked = _settings.AutoUpdateEnabled;
        _updateIntervalHours.Minimum = 6;
        _updateIntervalHours.Maximum = 168;
        _updateIntervalHours.Value = Math.Clamp(_settings.UpdateCheckIntervalHours, 6, 168);
        _upstreamRemote.Text = _settings.DshUpdates.UpstreamRemoteName;
        _patchBranch.Text = _settings.DshUpdates.PatchBranchName;
    }

    private async void SaveAsync(object? sender, EventArgs e)
    {
        var theme = _settings.Theme with
        {
            Name = _theme.SelectedItem?.ToString() ?? "Obsidian",
            Accent = _accent.Text.Trim(),
            Glow = _glow.Checked,
            FontScale = (int)_fontScale.Value,
            NavigationWidth = (int)_navigationWidth.Value,
            NavigationCollapsed = false,
            AutoCollapseNavigation = false
        };
        var next = _settings with
        {
            Theme = theme,
            Language = _language.SelectedItem is LanguageOption language
                ? language.Value
                : _settings.Language,
            StartPage = _startPage.SelectedItem is PageOption page
                ? page.Key
                : NormalizeStartPageKey(_startPage.SelectedItem?.ToString() ?? "DSH 管理"),
            RefreshSeconds = (int)_refreshSeconds.Value,
            ShowLogDrawer = _showLogs.Checked,
            CloseToTray = _closeToTray.Checked,
            AutoUpdateEnabled = _autoUpdate.Checked,
            UpdateCheckIntervalHours = (int)_updateIntervalHours.Value,
            DshUpdates = _settings.DshUpdates with
            {
                UpstreamRemoteName = _upstreamRemote.Text.Trim(),
                PatchBranchName = _patchBranch.Text.Trim()
            }
        };
        if (!DshPatchQueueService.IsValidBranchName(next.DshUpdates.PatchBranchName)
            || string.IsNullOrWhiteSpace(next.DshUpdates.UpstreamRemoteName)
            || next.DshUpdates.UpstreamRemoteName.Any(char.IsWhiteSpace))
        {
            _status.Text = _text.Pick("DSH 远程名或补丁分支名称无效", "The DSH remote or patch branch name is invalid");
            _status.ForeColor = Theme.Palette.Danger;
            return;
        }
        try
        {
            await _store.SaveAsync(next, CancellationToken.None);
            _settings = next;
            _status.Text = _text.Pick("已保存并应用主题", "Settings saved and applied");
            _status.ForeColor = Theme.Palette.Success;
            try
            {
                await _onSaved(next);
            }
            catch (Exception ex)
            {
                _status.Text = _text.Pick($"已保存，但应用失败：{ex.Message}", $"Saved, but applying failed: {ex.Message}");
                _status.ForeColor = Theme.Palette.Warning;
            }
        }
        catch (Exception ex)
        {
            _status.Text = _text.Pick($"保存失败：{ex.Message}", $"Save failed: {ex.Message}");
            _status.ForeColor = Theme.Palette.Danger;
        }
    }

    public void SetAutomaticUpdateStatus(string message, LauncherReleaseInfo? release, bool positive)
    {
        if (IsDisposed)
            return;
        _availableRelease = release;
        _updateStatus.Text = LocalizeUpdateMessage(message);
        _updateStatus.ForeColor = positive ? Theme.Palette.Success : Theme.Palette.Muted;
        _downloadUpdateButton.Enabled = release is not null;
    }

    public void UpdateSettings(LauncherSettings settings)
    {
        _settings = settings;
        LoadValues();
    }

    public override void ApplyLanguage(LauncherText text)
    {
        _text = text;
        ApplyHeader(
            text.LauncherSettings,
            text.Pick(
                "定制 dsh++ 的颜色、密度、导航和启动行为；这里的更新来自 GitHub Release，不检查 DSH 源码。",
                "Customize dsh++ colors, density, navigation and startup behavior. Updates come from GitHub Releases; DSH source is not checked."));
        SetLabel("theme", text.Pick("界面主题", "Interface theme"));
        SetLabel("accent", text.Pick("强调色", "Accent color"));
        SetLabel("glow", text.Pick("微光效果", "Glow effect"));
        SetLabel("fontScale", text.Pick("字体缩放", "Font scale"));
        SetLabel("navigationWidth", text.Pick("导航栏宽度", "Navigation width"));
        SetLabel("navigationMode", text.Pick("导航栏模式", "Navigation mode"));
        SetLabel("startPage", text.Pick("默认页面", "Default page"));
        SetLabel("refresh", text.Pick("自动刷新（秒）", "Auto refresh (seconds)"));
        SetLabel("showLogs", text.Pick("显示日志抽屉", "Show log drawer"));
        SetLabel("autoUpdate", text.Pick("自动检查更新", "Check for updates automatically"));
        SetLabel("updateInterval", text.Pick("检查间隔（小时）", "Check interval (hours)"));
        SetLabel("upstream", text.Pick("DSH 官方远程", "DSH upstream remote"));
        SetLabel("patchBranch", text.Pick("本地补丁分支", "Local patch branch"));
        SetLabel("isolation", text.Pick("DSH 更新隔离", "DSH update isolation"));
        SetLabel("release", text.Pick("dsh++ Release 更新", "dsh++ Release updates"));
        if (_navigationModeDescription is not null)
            _navigationModeDescription.Text = text.Pick("固定展开文字导航；窄窗口通过滚动保持完整显示", "Fixed expanded text navigation; narrow windows use scrolling to keep labels visible");
        if (_isolationDescription is not null)
            _isolationDescription.Text = text.Pick("源码补丁与插件/配置分离；dsh++ Release 更新独立执行", "Source patches stay separate from plugins/configuration; dsh++ Release updates are independent");
        if (_cardTitle is not null)
            _cardTitle.Text = text.Pick("界面定制", "Interface customization");
        if (_languageLabel is not null)
            _languageLabel.Text = text.LanguageLabel;
        if (_closeBehaviorLabel is not null)
            _closeBehaviorLabel.Text = text.CloseBehavior;
        _closeToTray.Text = text.CloseToTray;
        _glow.Text = text.Pick("启用卡片光晕", "Enable card glow");
        _showLogs.Text = text.Pick("显示 DSH 管理日志", "Show DSH management logs");
        _autoUpdate.Text = text.Pick("启动后低频检查 GitHub Release", "Check GitHub Releases at a low frequency after startup");
        _checkUpdateButton.Text = text.Pick("检查 dsh++ Release", "Check dsh++ Release");
        _downloadUpdateButton.Text = text.Pick("下载 x64 并重启", "Download x64 and restart");
        if (_saveButton is not null)
            _saveButton.Text = text.Pick("保存并应用", "Save and apply");
        if (_resetButton is not null)
            _resetButton.Text = text.Pick("恢复默认", "Reset to defaults");
        if (_updateStatus.Text is "尚未检查启动器更新" or "Launcher update has not been checked")
            _updateStatus.Text = text.Pick("尚未检查启动器更新", "Launcher update has not been checked");
        _status.Text = text.Pick("设置保存在当前用户的 LocalAppData", "Settings are stored in the current user's LocalAppData");
        LoadStartPageOptions();
        LoadLanguageOptions();
    }

    private void SetLabel(string key, string value)
    {
        if (_settingLabels.TryGetValue(key, out var label))
            label.Text = value;
    }

    private void LoadStartPageOptions()
    {
        var selectedKey = _startPage.SelectedItem is PageOption current
            ? current.Key
            : NormalizeStartPageKey(_settings.StartPage);
        var options = new[]
        {
            new PageOption("DSH 管理", _text.DshManagement),
            new PageOption("安装维护", _text.Maintenance),
            new PageOption("DeepSeek API", _text.DeepSeekApi),
            new PageOption("系统级设置", _text.SystemSettings),
            new PageOption("插件设置", _text.PluginSettings),
            new PageOption("启动器设置", _text.LauncherSettings)
        };
        _startPage.Items.Clear();
        _startPage.Items.AddRange(options);
        _startPage.SelectedItem = options.FirstOrDefault(option => option.Key == selectedKey) ?? options[0];
    }

    private static string NormalizeStartPageKey(string value) => value switch
    {
        "DSH Management" => "DSH 管理",
        "Maintenance" => "安装维护",
        "System Settings" => "系统级设置",
        "Plugin Settings" => "插件设置",
        "Launcher Settings" => "启动器设置",
        _ => value
    };

    private void LoadLanguageOptions()
    {
        var selected = _language.SelectedItem is LanguageOption current
            ? current.Value
            : _settings.Language;
        _language.Items.Clear();
        _language.Items.Add(new LanguageOption(LauncherLanguage.System, _text.LanguageSystem));
        _language.Items.Add(new LanguageOption(LauncherLanguage.SimplifiedChinese, _text.LanguageSimplifiedChinese));
        _language.Items.Add(new LanguageOption(LauncherLanguage.English, _text.LanguageEnglish));
        _language.SelectedItem = _language.Items.Cast<LanguageOption>()
            .FirstOrDefault(option => option.Value == selected)
            ?? _language.Items[0];
    }

    private async void CheckUpdateAsync(object? sender, EventArgs e)
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();
        _checkUpdateButton.Enabled = false;
        _downloadUpdateButton.Enabled = false;
        _updateStatus.Text = _text.Pick("正在检查 GitHub Release…", "Checking GitHub Release…");
        _updateStatus.ForeColor = Theme.Palette.Muted;
        try
        {
            var result = await _updateService.CheckAsync(_updateCts.Token);
            _availableRelease = result.UpdateAvailable ? result.Release : null;
            SetAutomaticUpdateStatus(result.Message, _availableRelease, result.UpdateAvailable);
        }
        catch (OperationCanceledException)
        {
            SetAutomaticUpdateStatus(_text.Pick("更新检查已取消。", "Update check canceled."), null, false);
        }
        catch (Exception ex)
        {
            SetAutomaticUpdateStatus(_text.Pick($"检查失败：{ex.Message}", $"Check failed: {ex.Message}"), null, false);
        }
        finally
        {
            _checkUpdateButton.Enabled = true;
        }
    }

    private async void DownloadUpdateAsync(object? sender, EventArgs e)
    {
        var release = _availableRelease;
        if (release is null)
            return;
        if (MessageBox.Show(
                this,
                _text.Pick(
                    $"将下载 dsh++ {release.Version} 并重启启动器。继续吗？",
                    $"dsh++ {release.Version} will be downloaded and the launcher restarted. Continue?"),
                _text.Pick("确认更新", "Confirm update"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information) != DialogResult.Yes)
            return;

        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();
        _checkUpdateButton.Enabled = false;
        _downloadUpdateButton.Enabled = false;
        var progress = new Progress<double>(value =>
        {
            _updateStatus.Text = _text.Pick($"正在下载更新… {value:P0}", $"Downloading update… {value:P0}");
            _updateStatus.ForeColor = Theme.Palette.Muted;
        });
        try
        {
            var result = await _updateService.DownloadAndPrepareAsync(
                release,
                _updateCts.Token,
                progress);
            if (!result.Succeeded || result.PreparedPath is null)
            {
                SetAutomaticUpdateStatus(result.Message, release, false);
                return;
            }

            if (!_updateService.TryStartInstaller(result.PreparedPath, out var message))
            {
                SetAutomaticUpdateStatus(message, release, false);
                return;
            }

            SetAutomaticUpdateStatus(message, null, true);
            _requestRestart();
        }
        catch (OperationCanceledException)
        {
            SetAutomaticUpdateStatus(_text.Pick("更新下载已取消。", "Update download canceled."), release, false);
        }
        finally
        {
            _checkUpdateButton.Enabled = true;
        }
    }

    private string LocalizeUpdateMessage(string message)
    {
        if (!_text.IsEnglish)
            return message;
        return message switch
        {
            "没有可用的稳定 Release。" => "No stable release is available.",
            "Release 版本或下载资产格式无效。" => "The Release version or assets are invalid.",
            "更新检查超时。" => "Update check timed out.",
            "无法连接 GitHub 更新服务。" => "Could not connect to the GitHub update service.",
            "GitHub Release 数据格式无效。" => "GitHub Release data is invalid.",
            "更新文件超过 250 MB 限制。" => "The update file exceeds the 250 MB limit.",
            "更新文件 SHA-256 校验失败。" => "The update file failed SHA-256 verification.",
            "下载更新超时。" => "Update download timed out.",
            "无法下载 GitHub Release 资产。" => "Could not download the GitHub Release asset.",
            "无法写入更新临时文件，请检查启动器目录权限。" => "Could not write the update temporary file. Check launcher directory permissions.",
            "更新临时文件无效。" => "The update temporary file is invalid.",
            "无法启动更新安装器。" => "Could not start the update installer.",
            "更新已准备，启动器将关闭并重启。" => "The update is ready. The launcher will close and restart.",
            "无法启动更新安装器，请检查启动器目录权限。" => "Could not start the update installer. Check launcher directory permissions.",
            _ when message.StartsWith("最新 Release 缺少", StringComparison.Ordinal) => "The latest Release is missing dsh++.exe.",
            _ when message.StartsWith("检查 GitHub Release 失败：", StringComparison.Ordinal) => $"GitHub Release check failed: {message[13..]}",
            _ when message.StartsWith("GitHub 更新服务返回 HTTP ", StringComparison.Ordinal) => message.Replace("GitHub 更新服务返回 HTTP ", "GitHub update service returned HTTP ", StringComparison.Ordinal),
            _ when message.StartsWith("GitHub 更新服务拒绝访问", StringComparison.Ordinal) => "The GitHub update service denied access.",
            _ when message.StartsWith("GitHub 仓库或 Release 不存在", StringComparison.Ordinal) => "The GitHub repository or Release does not exist.",
            _ when message.StartsWith("GitHub 更新服务暂时不可用", StringComparison.Ordinal) => "The GitHub update service is temporarily unavailable.",
            _ => message
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateCts?.Cancel();
            _updateCts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
