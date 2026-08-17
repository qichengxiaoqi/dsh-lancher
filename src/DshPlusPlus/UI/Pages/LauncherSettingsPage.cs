using System.Drawing;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Pages;

public sealed class LauncherSettingsPage : PageBase
{
    private LauncherSettings _settings;
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
    private readonly CheckBox _autoUpdate = new();
    private readonly NumericUpDown _updateIntervalHours = new();
    private readonly Label _updateStatus;
    private readonly GlowButton _checkUpdateButton;
    private readonly GlowButton _downloadUpdateButton;
    private LauncherReleaseInfo? _availableRelease;
    private CancellationTokenSource? _updateCts;
    private readonly Label _status;

    public LauncherSettingsPage(
        LauncherSettings settings,
        LauncherSettingsStore store,
        Func<LauncherSettings, Task> onSaved,
        LauncherUpdateService updateService,
        Action requestRestart,
        ThemeManager theme)
        : base(theme, "启动器设置", "定制 dsh++ 的颜色、密度、导航和启动行为；这里的更新来自 GitHub Release，不检查 DSH 源码。")
    {
        _settings = settings;
        _store = store;
        _onSaved = onSaved;
        _updateService = updateService;
        _requestRestart = requestRestart;
        _status = MutedLabel("设置保存在当前用户的 LocalAppData");
        _updateStatus = MutedLabel("尚未检查启动器更新");
        _updateStatus.AutoSize = true;
        _checkUpdateButton = new GlowButton("检查 dsh++ Release", Theme.Palette);
        _checkUpdateButton.Click += CheckUpdateAsync;
        _downloadUpdateButton = new GlowButton("下载 x64 并重启", Theme.Palette, primary: true)
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
        AddRow(table, "界面主题", _theme);
        AddRow(table, "强调色", _accent);
        AddRow(table, "微光效果", _glow);
        AddRow(table, "字体缩放", _fontScale);
        AddRow(table, "导航栏宽度", _navigationWidth);
        AddRow(table, "导航栏模式", MutedLabel("固定展开文字导航；窄窗口通过滚动保持完整显示"));
        AddRow(table, "默认页面", _startPage);
        AddRow(table, "自动刷新（秒）", _refreshSeconds);
        AddRow(table, "显示日志抽屉", _showLogs);
        AddRow(table, "自动检查更新", _autoUpdate);
        AddRow(table, "检查间隔（小时）", _updateIntervalHours);
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
        AddRow(table, "dsh++ Release 更新", updateActions);
        layout.Controls.Add(Card(new Panel { Dock = DockStyle.Fill, AutoScroll = true, Controls = { table } }, "界面定制"), 0, 1);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(6, 5, 0, 0) };
        footer.Controls.Add(_status);
        var save = new GlowButton("保存并应用", Theme.Palette, primary: true);
        save.Click += SaveAsync;
        var reset = new GlowButton("恢复默认", Theme.Palette);
        reset.Click += (_, _) =>
        {
            _settings = LauncherSettings.CreateDefault();
            LoadValues();
        };
        footer.Controls.Add(save);
        footer.Controls.Add(reset);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
    }

    private void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = Theme.Palette.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = "small"
        }, 0, row);
        control.Dock = DockStyle.Left;
        control.Margin = new Padding(4, 6, 4, 6);
        table.Controls.Add(control, 1, row);
    }

    private void LoadValues()
    {
        _theme.Items.Clear();
        _theme.Items.AddRange(["Obsidian", "Light", "High Contrast"]);
        _theme.SelectedItem = _settings.Theme.Name;
        _accent.Text = _settings.Theme.Accent;
        _glow.Text = "启用卡片光晕";
        _glow.Checked = _settings.Theme.Glow;
        _fontScale.Minimum = 80;
        _fontScale.Maximum = 140;
        _fontScale.Value = Math.Clamp(_settings.Theme.FontScale, 80, 140);
        _navigationWidth.Minimum = 180;
        _navigationWidth.Maximum = 320;
        _navigationWidth.Value = Math.Clamp(_settings.Theme.NavigationWidth, 180, 320);
        _startPage.Items.Clear();
        _startPage.Items.AddRange(["DSH 管理", "安装维护", "DeepSeek API", "系统级设置", "插件设置", "启动器设置"]);
        _startPage.SelectedItem = _settings.StartPage;
        _refreshSeconds.Minimum = 5;
        _refreshSeconds.Maximum = 120;
        _refreshSeconds.Value = Math.Clamp(_settings.RefreshSeconds, 5, 120);
        _showLogs.Text = "显示 DSH 管理日志";
        _showLogs.Checked = _settings.ShowLogDrawer;
        _autoUpdate.Text = "启动后低频检查 GitHub Release";
        _autoUpdate.Checked = _settings.AutoUpdateEnabled;
        _updateIntervalHours.Minimum = 6;
        _updateIntervalHours.Maximum = 168;
        _updateIntervalHours.Value = Math.Clamp(_settings.UpdateCheckIntervalHours, 6, 168);
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
            StartPage = _startPage.SelectedItem?.ToString() ?? "DSH 管理",
            RefreshSeconds = (int)_refreshSeconds.Value,
            ShowLogDrawer = _showLogs.Checked,
            AutoUpdateEnabled = _autoUpdate.Checked,
            UpdateCheckIntervalHours = (int)_updateIntervalHours.Value
        };
        try
        {
            await _store.SaveAsync(next, CancellationToken.None);
            _settings = next;
            _status.Text = "已保存并应用主题";
            _status.ForeColor = Theme.Palette.Success;
            try
            {
                await _onSaved(next);
            }
            catch (Exception ex)
            {
                _status.Text = $"已保存，但应用失败：{ex.Message}";
                _status.ForeColor = Theme.Palette.Warning;
            }
        }
        catch (Exception ex)
        {
            _status.Text = $"保存失败：{ex.Message}";
            _status.ForeColor = Theme.Palette.Danger;
        }
    }

    public void SetAutomaticUpdateStatus(string message, LauncherReleaseInfo? release, bool positive)
    {
        if (IsDisposed)
            return;
        _availableRelease = release;
        _updateStatus.Text = message;
        _updateStatus.ForeColor = positive ? Theme.Palette.Success : Theme.Palette.Muted;
        _downloadUpdateButton.Enabled = release is not null;
    }

    public void UpdateSettings(LauncherSettings settings)
    {
        _settings = settings;
        LoadValues();
    }

    private async void CheckUpdateAsync(object? sender, EventArgs e)
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();
        _checkUpdateButton.Enabled = false;
        _downloadUpdateButton.Enabled = false;
        _updateStatus.Text = "正在检查 GitHub Release…";
        _updateStatus.ForeColor = Theme.Palette.Muted;
        try
        {
            var result = await _updateService.CheckAsync(_updateCts.Token);
            _availableRelease = result.UpdateAvailable ? result.Release : null;
            SetAutomaticUpdateStatus(result.Message, _availableRelease, result.UpdateAvailable);
        }
        catch (OperationCanceledException)
        {
            SetAutomaticUpdateStatus("更新检查已取消。", null, false);
        }
        catch (Exception ex)
        {
            SetAutomaticUpdateStatus($"检查失败：{ex.Message}", null, false);
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
                $"将下载 dsh++ {release.Version} 并重启启动器。继续吗？",
                "确认更新",
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
            _updateStatus.Text = $"正在下载更新… {value:P0}";
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
            SetAutomaticUpdateStatus("更新下载已取消。", release, false);
        }
        finally
        {
            _checkUpdateButton.Enabled = true;
        }
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
