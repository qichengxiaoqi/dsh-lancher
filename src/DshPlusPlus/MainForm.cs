using System.Drawing;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Pages;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus;

public sealed class MainForm : Form
{
    private readonly LauncherSettingsStore _settingsStore;
    private readonly LauncherUpdateService _launcherUpdateService;
    private LauncherSettings _settings;
    private readonly ThemeManager _theme;
    private readonly TableLayoutPanel _shell = new();
    private readonly Panel _contentHost = new();
    private readonly FlowLayoutPanel _navigation = new();
    private readonly Dictionary<string, PageBase> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NavigationButton> _navigationButtons = new(StringComparer.Ordinal);
    private readonly ToolTip _navigationToolTip = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly SkillPathResolver _skillPathResolver = new();
    private StatusChip? _trayStatusChip;
    private DshManagementPage? _dshManagementPage;
    private DeepSeekApiPage? _deepSeekApiPage;
    private SystemSettingsPage? _systemSettingsPage;
    private PluginSettingsPage? _pluginSettingsPage;
    private LauncherSettingsPage? _launcherSettingsPage;
    private bool _allowClose;
    private bool _isInTray;
    private bool _navigationCollapsed;
    private TrayStatusKind _trayStatusKind = TrayStatusKind.Checking;
    private ThemePalette? _trayPalette;
    private TableLayoutPanel? _sidebar;
    private Label? _brandKicker;
    private Label? _brandName;
    private Label? _footerLabel;
    private string _activePage = string.Empty;

    public MainForm(
        LauncherSettings settings,
        LauncherSettingsStore settingsStore,
        DshPaths dshPaths,
        IDshServiceController serviceController,
        ServiceStatusProbe statusProbe,
        IGitRepositoryService gitRepository,
        DshCredentialStore credentialStore,
        IDeepSeekApiClient apiClient,
        SystemInstructionScanner instructionScanner,
        PluginInventoryService pluginInventory,
        SkillPathSet skillPaths,
        SkillInventoryService skillInventory,
        SkillImportService skillImporter,
        ProfilePatchService patchService,
        LauncherPathDiscovery pathDiscovery,
        LauncherUpdateService launcherUpdateService,
        DshPatchQueueService patchQueue)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _launcherUpdateService = launcherUpdateService;
        _theme = new ThemeManager(settings.Theme);
        _navigationCollapsed = false;

        Text = "dsh++ · DeepSeek Harness Control Deck";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(960, 640);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);

        BuildShell();
        RegisterPages(
            dshPaths,
            serviceController,
            statusProbe,
            gitRepository,
            credentialStore,
            apiClient,
            instructionScanner,
            pluginInventory,
            skillPaths,
            skillInventory,
            skillImporter,
            patchService,
            pathDiscovery,
            launcherUpdateService,
            patchQueue);
        _deepSeekApiPage = _pages.Values.OfType<DeepSeekApiPage>().Single();
        _systemSettingsPage = _pages.Values.OfType<SystemSettingsPage>().Single();
        _pluginSettingsPage = _pages.Values.OfType<PluginSettingsPage>().Single();
        ConfigureTrayIcon();
        _theme.Apply(this);
        ApplyNavigationMode(UiMetrics.ResolveNavigationMode(
            settings.Theme.NavigationCollapsed,
            settings.Theme.AutoCollapseNavigation,
            ClientSize.Width).IsCollapsed);
        SelectPage(settings.StartPage);
        _refreshTimer.Interval = Math.Max(5000, settings.RefreshSeconds * 1000);
        _refreshTimer.Tick += async (_, _) => await RefreshActivePageAsync();
        _refreshTimer.Start();
        FormClosing += HandleFormClosing;
        Resize += HandleResize;
        Shown += async (_, _) =>
        {
            HandleResponsiveLayout();
            await StartAutomaticUpdateCheckAsync();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
            _navigationToolTip.Dispose();
            _theme.Dispose();
            foreach (var page in _pages.Values)
                page.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildShell()
    {
        _shell.Dock = DockStyle.Fill;
        _shell.Margin = new Padding(0);
        _shell.Padding = new Padding(0);
        _shell.ColumnCount = 2;
        _shell.RowCount = 1;
        _shell.ColumnStyles.Add(new ColumnStyle(
            SizeType.Absolute,
            UiMetrics.NavigationWidth(_navigationCollapsed, _settings.Theme.NavigationWidth)));
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _shell.BackColor = _theme.Palette.Background;

        var sidebar = BuildSidebar();
        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = _theme.Palette.Background;
        _shell.Controls.Add(sidebar, 0, 0);
        _shell.Controls.Add(_contentHost, 1, 0);
        Controls.Add(_shell);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = _navigationCollapsed
                ? new Padding(8, 18, 8, 14)
                : new Padding(16, 18, 12, 14),
            BackColor = _theme.Palette.Surface,
            Tag = "surface"
        };
        _sidebar = sidebar;
        sidebar.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            UiMetrics.PixelsFromDip(78, DeviceDpi, _settings.Theme.FontScale)));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            UiMetrics.PixelsFromDip(70, DeviceDpi, _settings.Theme.FontScale)));

        var brand = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Palette.Surface };
        var brandHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = UiMetrics.PixelsFromDip(26, DeviceDpi, _settings.Theme.FontScale),
            BackColor = _theme.Palette.Surface
        };
        _brandKicker = new Label
        {
            Text = "DEEPSEEK HARNESS",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = _theme.Palette.Accent,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = "small"
        };
        brandHeader.Controls.Add(_brandKicker);
        _brandName = new Label
        {
            Text = "dsh++",
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = _theme.Palette.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = "title"
        };
        brand.Controls.Add(_brandName);
        brand.Controls.Add(brandHeader);
        sidebar.Controls.Add(brand, 0, 0);

        _navigation.Dock = DockStyle.Fill;
        _navigation.FlowDirection = FlowDirection.TopDown;
        _navigation.WrapContents = false;
        _navigation.AutoScroll = true;
        _navigation.BackColor = _theme.Palette.Surface;
        AddNavigation("DSH 管理", "01");
        AddNavigation("安装维护", "02");
        AddNavigation("DeepSeek API", "03");
        AddNavigation("系统级设置", "04");
        AddNavigation("插件设置", "05");
        AddNavigation("启动器设置", "06");
        sidebar.Controls.Add(_navigation, 0, 1);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Palette.Surface };
        _footerLabel = new Label
        {
            Text = "LOCAL CONTROL DECK\n.NET 9 · WIN-X64",
            Dock = DockStyle.Fill,
            ForeColor = _theme.Palette.Muted,
            TextAlign = ContentAlignment.BottomLeft,
            Tag = "mono"
        };
        footer.Controls.Add(_footerLabel);
        _trayStatusChip = new StatusChip("托盘待命", _theme.Palette)
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 6)
        };
        footer.Controls.Add(_trayStatusChip);
        sidebar.Controls.Add(footer, 0, 2);
        return sidebar;
    }

    private void AddNavigation(string title, string index)
    {
        var item = NavigationItem.Create(title, index);
        var button = new NavigationButton(item, _theme.Palette)
        {
            Width = Math.Max(56, UiMetrics.NavigationWidth(_navigationCollapsed, _settings.Theme.NavigationWidth) - 16),
            Tag = title
        };
        button.Click += (_, _) => SelectPage(title);
        _navigationToolTip.SetToolTip(button, title);
        _navigationButtons[title] = button;
        _navigation.Controls.Add(button);
    }

    private void RegisterPages(
        DshPaths dshPaths,
        IDshServiceController serviceController,
        ServiceStatusProbe statusProbe,
        IGitRepositoryService gitRepository,
        DshCredentialStore credentialStore,
        IDeepSeekApiClient apiClient,
        SystemInstructionScanner instructionScanner,
        PluginInventoryService pluginInventory,
        SkillPathSet skillPaths,
        SkillInventoryService skillInventory,
        SkillImportService skillImporter,
        ProfilePatchService patchService,
        LauncherPathDiscovery pathDiscovery,
        LauncherUpdateService launcherUpdateService,
        DshPatchQueueService patchQueue)
    {
        _pages["DSH 管理"] = new DshManagementPage(
            dshPaths, serviceController, statusProbe, gitRepository, patchQueue, _theme);
        _pages["安装维护"] = new MaintenancePage(
            _settings,
            _settingsStore,
            new PathValidator(),
            pathDiscovery,
            SaveSettingsAsync,
            _theme);
        _pages["DeepSeek API"] = new DeepSeekApiPage(_settings, credentialStore, apiClient, _theme);
        _pages["系统级设置"] = new SystemSettingsPage(instructionScanner, _theme);
        _pages["插件设置"] = new PluginSettingsPage(
            _settings.Paths,
            pluginInventory,
            skillPaths,
            skillInventory,
            skillImporter,
            patchService,
            serviceController,
            statusProbe,
            _theme);
        _launcherSettingsPage = new LauncherSettingsPage(
            _settings,
            _settingsStore,
            SaveSettingsAsync,
            launcherUpdateService,
            ExitApplication,
            _theme);
        _pages["启动器设置"] = _launcherSettingsPage;
    }

    private void ConfigureTrayIcon()
    {
        _dshManagementPage = _pages.Values.OfType<DshManagementPage>().Single();
        _dshManagementPage.ServiceStateChanged += state =>
            UpdateTrayStatus(DescribeState(state), TrayStatusMapper.From(state, busy: false));
        var launcherIcon = TrayIconFactory.Create(_theme.Palette, TrayStatusKind.Checking);
        Icon = (Icon)launcherIcon.Clone();
        _notifyIcon.Icon = launcherIcon;
        _notifyIcon.Text = "dsh++ · DeepSeek Harness";
        _notifyIcon.Visible = true;
        _notifyIcon.ContextMenuStrip = _trayMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();

        var open = new ToolStripMenuItem("打开 dsh++");
        open.Click += (_, _) => ShowFromTray();
        var refresh = new ToolStripMenuItem("刷新 DSH 状态");
        refresh.Click += async (_, _) => await RefreshDshStatusFromTrayAsync();
        var exit = new ToolStripMenuItem("退出 dsh++");
        exit.Click += (_, _) => ExitApplication();
        _trayMenu.Items.AddRange([open, refresh, new ToolStripSeparator(), exit]);
        UpdateTrayStatus("正在检测 DSH", TrayStatusKind.Checking);
    }

    private async Task RefreshDshStatusFromTrayAsync()
    {
        if (_dshManagementPage is null)
            return;
        try
        {
            UpdateTrayStatus("正在检测 DSH", TrayStatusKind.Checking);
            await _dshManagementPage.RefreshAsync(CancellationToken.None);
            UpdateTrayStatus(
                DescribeState(_dshManagementPage.CurrentServiceState),
                TrayStatusMapper.From(_dshManagementPage.CurrentServiceState, busy: false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UpdateTrayStatus("DSH 探测异常", TrayStatusKind.Attention);
        }
    }

    private void UpdateTrayStatus(string status, TrayStatusKind? kind = null)
    {
        if (IsDisposed)
            return;
        var visual = kind
                     ?? (_dshManagementPage is null
                         ? TrayStatusKind.Checking
                         : TrayStatusMapper.From(_dshManagementPage.CurrentServiceState, busy: false));
        if (_trayStatusKind != visual || _trayPalette != _theme.Palette || _notifyIcon.Icon is null)
        {
            var previousIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = TrayIconFactory.Create(_theme.Palette, visual);
            previousIcon?.Dispose();
            _trayStatusKind = visual;
            _trayPalette = _theme.Palette;
        }
        var color = visual switch
        {
            TrayStatusKind.Connected => _theme.Palette.Success,
            TrayStatusKind.Disconnected => _theme.Palette.Danger,
            TrayStatusKind.Attention => _theme.Palette.Warning,
            _ => _theme.Palette.Warning
        };
        var normalized = status.Length > 36 ? status[..36] : status;
        _notifyIcon.Text = $"dsh++ - {normalized}";
        _trayStatusChip?.SetState(
            _navigationCollapsed ? "●" : $"托盘 · {normalized}",
            color,
            Color.FromArgb(35, color));
        if (_trayStatusChip is not null)
            _navigationToolTip.SetToolTip(_trayStatusChip, $"托盘：{normalized}");
    }

    private static string DescribeState(ServiceState state) => state switch
    {
        ServiceState.Running => "运行中",
        ServiceState.Stopped => "已停止",
        ServiceState.StartFailed => "启动失败",
        _ => "未知"
    };

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason != CloseReason.UserClosing)
            return;
        if (!_settings.CloseToTray)
        {
            _notifyIcon.Visible = false;
            return;
        }
        e.Cancel = true;
        HideToTray();
    }

    private void HandleResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
            return;
        }
        HandleResponsiveLayout();
    }

    private void HandleResponsiveLayout()
    {
        var collapsed = UiMetrics.ResolveNavigationMode(
            _settings.Theme.NavigationCollapsed,
            _settings.Theme.AutoCollapseNavigation,
            ClientSize.Width).IsCollapsed;
        if (collapsed != _navigationCollapsed)
            ApplyNavigationMode(collapsed);
    }

    private void ApplyNavigationMode(bool collapsed)
    {
        // The text navigation is intentionally always expanded. Compact mode
        // caused labels to be clipped on narrow windows and was not reliable
        // with DPI-scaled fonts.
        collapsed = false;
        _navigationCollapsed = false;
        var width = UiMetrics.NavigationWidth(collapsed, _settings.Theme.NavigationWidth);
        _shell.ColumnStyles[0].Width = width;
        if (_sidebar is not null)
            _sidebar.Padding = new Padding(16, 18, 12, 14);
        foreach (var pair in _navigationButtons)
        {
            pair.Value.ApplyLayout(
                collapsed,
                Math.Max(56, width - (collapsed ? 16 : 28)),
                DeviceDpi);
            pair.Value.IsActive = pair.Key == _activePage;
            pair.Value.Invalidate();
        }

        if (_brandKicker is not null)
            _brandKicker.Visible = !collapsed;
        if (_brandName is not null)
        {
            _brandName.Text = "dsh++";
            _brandName.TextAlign = ContentAlignment.MiddleLeft;
        }
        if (_footerLabel is not null)
            _footerLabel.Visible = true;
        if (_trayStatusChip is not null)
            _navigationToolTip.SetToolTip(_trayStatusChip, "托盘状态");
        _navigation.PerformLayout();
        _shell.PerformLayout();
        UpdateTrayStatus(
            _dshManagementPage is null ? "正在检测 DSH" : DescribeState(_dshManagementPage.CurrentServiceState),
            _dshManagementPage is null
                ? TrayStatusKind.Checking
                : TrayStatusMapper.From(_dshManagementPage.CurrentServiceState, busy: false));
    }

    private void HideToTray()
    {
        if (_isInTray)
            return;
        _isInTray = true;
        _refreshTimer.Stop();
        ShowInTaskbar = false;
        Hide();
        UpdateTrayStatus(
            "后台待命",
            _dshManagementPage is null
                ? TrayStatusKind.Checking
                : TrayStatusMapper.From(_dshManagementPage.CurrentServiceState, busy: false));
    }

    private void ShowFromTray()
    {
        _isInTray = false;
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _refreshTimer.Start();
        _ = RefreshActivePageAsync();
        UpdateTrayStatus(
            _dshManagementPage is null ? "正在检测 DSH" : DescribeState(_dshManagementPage.CurrentServiceState),
            _dshManagementPage is null
                ? TrayStatusKind.Checking
                : TrayStatusMapper.From(_dshManagementPage.CurrentServiceState, busy: false));
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _notifyIcon.Visible = false;
        Close();
    }

    private void SelectPage(string pageName)
    {
        if (!_pages.ContainsKey(pageName))
            pageName = "DSH 管理";
        _activePage = pageName;
        _contentHost.SuspendLayout();
        _contentHost.Controls.Clear();
        var page = _pages[pageName];
        _contentHost.Controls.Add(page);
        page.ApplyCurrentTheme();
        _contentHost.ResumeLayout(true);
        foreach (var pair in _navigationButtons)
        {
            pair.Value.BackColor = pair.Key == pageName ? _theme.Palette.AccentSoft : _theme.Palette.Surface;
            pair.Value.ForeColor = pair.Key == pageName ? _theme.Palette.Accent : _theme.Palette.Muted;
            pair.Value.IsActive = pair.Key == pageName;
            pair.Value.Invalidate();
        }
        if (page.SupportsAutoRefresh)
            _ = RefreshActivePageAsync();
    }

    private async Task RefreshActivePageAsync()
    {
        if (_pages.TryGetValue(_activePage, out var page) && !IsDisposed)
        {
            if (!page.SupportsAutoRefresh)
                return;
            try
            {
                await page.RefreshAsync(CancellationToken.None);
                if (ReferenceEquals(page, _dshManagementPage))
                    UpdateTrayStatus(
                        DescribeState(_dshManagementPage.CurrentServiceState),
                        TrayStatusMapper.From(_dshManagementPage.CurrentServiceState, busy: false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
    }

    private async Task SaveSettingsAsync(LauncherSettings settings)
    {
        var pageToRestore = ResolvePageAfterSettingsSave(_activePage, settings.StartPage);
        _settings = settings;
        _launcherSettingsPage?.UpdateSettings(settings);
        _deepSeekApiPage?.UpdateSettings(settings);
        _systemSettingsPage?.UpdatePaths(settings.Paths);
        _pluginSettingsPage?.UpdatePaths(settings.Paths);
        _pluginSettingsPage?.UpdateSkillPaths(
            _skillPathResolver.Resolve(settings.Paths, settings.SkillImport));
        _theme.Update(settings.Theme);
        foreach (var page in _pages.Values)
            page.ApplyCurrentTheme();
        _theme.Apply(this);
        _theme.ReleaseRetiredFonts();
        _refreshTimer.Interval = Math.Max(5000, settings.RefreshSeconds * 1000);
        ApplyNavigationMode(UiMetrics.ResolveNavigationMode(
            settings.Theme.NavigationCollapsed,
            settings.Theme.AutoCollapseNavigation,
            ClientSize.Width).IsCollapsed);
        SelectPage(pageToRestore);
        await Task.CompletedTask;
    }

    private static string ResolvePageAfterSettingsSave(string activePage, string fallbackPage) =>
        string.IsNullOrWhiteSpace(activePage) ? fallbackPage : activePage;

    private async Task StartAutomaticUpdateCheckAsync()
    {
        if (!_settings.AutoUpdateEnabled
            || _settings.LastUpdateCheckUtc is { } last
               && DateTimeOffset.UtcNow - last
                  < TimeSpan.FromHours(Math.Clamp(_settings.UpdateCheckIntervalHours, 6, 168)))
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), _lifetimeCts.Token);
            if (IsDisposed || _lifetimeCts.IsCancellationRequested)
                return;

            var result = await _launcherUpdateService.CheckAsync(_lifetimeCts.Token);
            var checkedSettings = _settings with { LastUpdateCheckUtc = DateTimeOffset.UtcNow };
            _settings = checkedSettings;
            try
            {
                await _settingsStore.SaveAsync(checkedSettings, _lifetimeCts.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            if (!result.Succeeded)
                return;

            _launcherSettingsPage?.SetAutomaticUpdateStatus(
                result.Message,
                result.UpdateAvailable ? result.Release : null,
                result.UpdateAvailable);
            if (!result.UpdateAvailable || result.Release is null)
                return;

            var version = result.LatestVersion?.ToString() ?? "新版本";
            UpdateTrayStatus($"有新版本 {version}", TrayStatusKind.Attention);
            _notifyIcon.BalloonTipTitle = "dsh++ 有新版本";
            _notifyIcon.BalloonTipText = $"发现 dsh++ {version}，可在“启动器设置”中下载并重启。";
            _notifyIcon.ShowBalloonTip(5000);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
