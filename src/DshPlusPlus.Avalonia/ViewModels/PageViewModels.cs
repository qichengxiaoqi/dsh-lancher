using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.Avalonia.Services;

namespace DshPlusPlus.Avalonia.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    private string _status = "Ready";

    protected PageViewModel(AvaloniaAppHost host, string title, string eyebrow, string description)
    {
        Host = host;
        Title = title;
        Eyebrow = eyebrow;
        Description = description;
        RefreshCommand = new AsyncCommand(() => RefreshAsync(CancellationToken.None));
    }

    protected AvaloniaAppHost Host { get; }
    public string Title { get; }
    public string Eyebrow { get; }
    public string Description { get; }
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ICommand RefreshCommand { get; }

    public virtual Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static string DescribeException(Exception exception) =>
        exception is HttpRequestException ? "Network request failed." : exception.Message;

    public static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // External launch is best effort; the UI keeps the target visible for manual copy.
        }
    }
}

public sealed class NavigationItemViewModel : ObservableObject
{
    private bool _isSelected;

    public NavigationItemViewModel(string number, string icon, string title, PageViewModel page)
    {
        Number = number;
        Icon = icon;
        Title = title;
        Page = page;
    }

    public string Number { get; }
    public string Icon { get; }
    public string Title { get; }
    public PageViewModel Page { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private bool _showTitle = true;
    public bool ShowTitle
    {
        get => _showTitle;
        set => SetProperty(ref _showTitle, value);
    }
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly AvaloniaAppHost _host;
    private PageViewModel _currentPage;
    private bool _isNavigationCollapsed;
    private string _platformLabel = RuntimeInformationLabel();

    public MainWindowViewModel(AvaloniaAppHost host)
    {
        _host = host;
        DshManagement = new DshManagementPageViewModel(host);
        Maintenance = new MaintenancePageViewModel(host);
        Api = new DeepSeekApiPageViewModel(host);
        SystemSettings = new SystemSettingsPageViewModel(host);
        Plugins = new PluginSettingsPageViewModel(host);
        LauncherSettings = new LauncherSettingsPageViewModel(host);
        Pages = new ObservableCollection<NavigationItemViewModel>
        {
            new("01", "⌂", "DSH 管理", DshManagement),
            new("02", "↻", "安装维护", Maintenance),
            new("03", "◇", "DeepSeek API", Api),
            new("04", "⌘", "系统级设置", SystemSettings),
            new("05", "◈", "插件设置", Plugins),
            new("06", "⚙", "启动器设置", LauncherSettings)
        };
        _currentPage = ResolveStartPage(host.Settings.StartPage);
        IsNavigationCollapsed = host.Settings.Theme.NavigationCollapsed;
        SelectPageCommand = new RelayCommand(parameter =>
        {
            if (parameter is NavigationItemViewModel item)
                SelectPage(item);
        });
        ToggleNavigationCommand = new RelayCommand(() => IsNavigationCollapsed = !IsNavigationCollapsed);
        OpenWebCommand = new RelayCommand(() => PageViewModel.OpenExternal(host.Paths.WebUrl));
        _ = RefreshCurrentPageAsync();
    }

    public ObservableCollection<NavigationItemViewModel> Pages { get; }
    public DshManagementPageViewModel DshManagement { get; }
    public MaintenancePageViewModel Maintenance { get; }
    public DeepSeekApiPageViewModel Api { get; }
    public SystemSettingsPageViewModel SystemSettings { get; }
    public PluginSettingsPageViewModel Plugins { get; }
    public LauncherSettingsPageViewModel LauncherSettings { get; }
    public ICommand SelectPageCommand { get; }
    public ICommand ToggleNavigationCommand { get; }
    public ICommand OpenWebCommand { get; }
    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value))
                return;
            foreach (var item in Pages)
                item.IsSelected = ReferenceEquals(item.Page, value);
            OnPropertyChanged(nameof(PageSubtitle));
            _ = RefreshCurrentPageAsync();
        }
    }

    public string PageSubtitle => CurrentPage.Description;
    public bool IsNavigationCollapsed
    {
        get => _isNavigationCollapsed;
        set
        {
            if (!SetProperty(ref _isNavigationCollapsed, value))
                return;
            OnPropertyChanged(nameof(NavigationWidth));
            OnPropertyChanged(nameof(NavigationDisplayMode));
            OnPropertyChanged(nameof(IsNavigationExpanded));
            foreach (var item in Pages)
                item.ShowTitle = !value;
        }
    }

    public double NavigationWidth => IsNavigationCollapsed ? 78 : 246;
    public bool IsNavigationExpanded => !IsNavigationCollapsed;
    public string NavigationDisplayMode => IsNavigationCollapsed ? "展开导航" : "收起导航";
    public string PlatformLabel
    {
        get => _platformLabel;
        private set => SetProperty(ref _platformLabel, value);
    }

    public void SelectPage(NavigationItemViewModel item) => CurrentPage = item.Page;

    private async Task RefreshCurrentPageAsync()
    {
        try
        {
            await CurrentPage.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CurrentPage.Status = PageViewModel.DescribeException(exception);
        }
    }

    private PageViewModel ResolveStartPage(string startPage) =>
        Pages.FirstOrDefault(item => string.Equals(item.Title, startPage, StringComparison.OrdinalIgnoreCase))?.Page
        ?? DshManagement;

    public void Dispose()
    {
        // Core clients are owned by AvaloniaAppHost. This VM only owns no worker threads.
    }

    private static string RuntimeInformationLabel()
    {
        var os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        return $"{os} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
    }
}

public sealed class DshManagementPageViewModel : PageViewModel
{
    private string _serviceStatus = "Not checked";
    private string _gitStatus = "Not checked";
    private string _logText = "No launcher activity yet.";
    private bool _isBusy;

    public DshManagementPageViewModel(AvaloniaAppHost host)
        : base(host, "DSH 管理", "CONTROL DECK / 01", "Start, inspect and safely update the local DeepSeek Harness service.")
    {
        StartCommand = new AsyncCommand(StartAsync, () => !_isBusy);
        StopCommand = new AsyncCommand(StopAsync, () => !_isBusy);
        RestartCommand = new AsyncCommand(RestartAsync, () => !_isBusy);
        CheckGitCommand = new AsyncCommand(CheckGitAsync, () => !_isBusy);
        PullCommand = new AsyncCommand(PullAsync, () => !_isBusy);
        OpenWebCommand = new RelayCommand(() => OpenExternal(Host.Paths.WebUrl));
    }

    public string ServiceStatus { get => _serviceStatus; private set => SetProperty(ref _serviceStatus, value); }
    public string GitStatus { get => _gitStatus; private set => SetProperty(ref _gitStatus, value); }
    public string WebUrl => Host.Paths.WebUrl;
    public string RootPath => string.IsNullOrWhiteSpace(Host.Paths.Root) ? "DSH root not detected" : Host.Paths.Root;
    public string PlatformNotice => OperatingSystem.IsWindows()
        ? "Windows service actions are available."
        : "Service start/stop is Windows-only; status, Git and API tools remain available.";
    public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand CheckGitCommand { get; }
    public ICommand PullCommand { get; }
    public ICommand OpenWebCommand { get; }

    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await Host.StatusProbe.ProbeAsync(cancellationToken);
            ServiceStatus = $"{result.State} · {result.Message}";
            Status = "Service status refreshed";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ServiceStatus = "Unknown · " + exception.Message;
            Status = "Status probe failed";
        }
    }

    private async Task StartAsync() => await RunServiceActionAsync("start", Host.ServiceController.StartAsync);
    private async Task StopAsync() => await RunServiceActionAsync("stop", Host.ServiceController.StopAsync);
    private async Task RestartAsync() => await RunServiceActionAsync("restart", Host.ServiceController.RestartAsync);

    private async Task RunServiceActionAsync(string action, Func<CancellationToken, Task<ProcessResult>> operation)
    {
        if (!OperatingSystem.IsWindows())
        {
            Status = "This service script is only available on Windows.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Host.Paths.Root) || !File.Exists(Host.Paths.ServiceScript))
        {
            Status = "DSH root or service script was not detected.";
            return;
        }
        _isBusy = true;
        try
        {
            var result = await operation(CancellationToken.None);
            AppendLog($"[{DateTime.Now:T}] {action}: {Summarize(result)}");
            Status = result.Succeeded ? $"{action} completed" : $"{action} failed";
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendLog($"[{DateTime.Now:T}] {action}: {exception.Message}");
            Status = $"{action} failed";
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task CheckGitAsync()
    {
        _isBusy = true;
        try
        {
            var result = await Host.GitRepository.CheckAsync(CancellationToken.None);
            GitStatus = result.Message;
            Status = "Git status refreshed";
            AppendLog($"[{DateTime.Now:T}] git: {result.Message}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            GitStatus = exception.Message;
            Status = "Git check failed";
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task PullAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            Status = "Automatic DSH update is currently Windows-only.";
            return;
        }
        _isBusy = true;
        try
        {
            var result = await Host.UpdateCoordinator.PullAsync(CancellationToken.None);
            Status = result.Message;
            AppendLog($"[{DateTime.Now:T}] update: {result.Message}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = exception.Message;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void AppendLog(string line)
    {
        var lines = (LogText + Environment.NewLine + line)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(80);
        LogText = string.Join(Environment.NewLine, lines);
    }

    private static string Summarize(ProcessResult result)
    {
        var output = result.CombinedOutput.Trim();
        return output.Length <= 600 ? output : output[..600] + "…";
    }
}

public sealed class MaintenancePageViewModel : PageViewModel
{
    private string _dshRoot = string.Empty;
    private string _dshHome = string.Empty;
    private string _profileDirectory = string.Empty;
    private string _pluginRoot = string.Empty;
    private string _serviceScript = string.Empty;
    private string _webUrl = string.Empty;
    private string _port = "3080";
    private string _validationMessage = "Not validated";

    public MaintenancePageViewModel(AvaloniaAppHost host)
        : base(host, "安装维护", "PATHS / 02", "Detect and review DSH paths without hard-coding a machine-specific layout.")
    {
        LoadFrom(host.Settings.Paths);
        DetectCommand = new RelayCommand(() => LoadFrom(Host.PathDiscovery.Discover()));
        ValidateCommand = new RelayCommand(Validate);
        SaveCommand = new AsyncCommand(SaveAsync);
    }

    public string DshRoot { get => _dshRoot; set => SetProperty(ref _dshRoot, value); }
    public string DshHome { get => _dshHome; set => SetProperty(ref _dshHome, value); }
    public string ProfileDirectory { get => _profileDirectory; set => SetProperty(ref _profileDirectory, value); }
    public string PluginRoot { get => _pluginRoot; set => SetProperty(ref _pluginRoot, value); }
    public string ServiceScript { get => _serviceScript; set => SetProperty(ref _serviceScript, value); }
    public string WebUrl { get => _webUrl; set => SetProperty(ref _webUrl, value); }
    public string Port { get => _port; set => SetProperty(ref _port, value); }
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public ICommand DetectCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand SaveCommand { get; }

    private void LoadFrom(LauncherPaths paths)
    {
        DshRoot = paths.DshRoot;
        DshHome = paths.DshHome;
        ProfileDirectory = paths.ProfileDirectory;
        PluginRoot = paths.PluginRoot;
        ServiceScript = paths.ServiceScript;
        WebUrl = paths.WebUrl;
        Port = paths.Port.ToString(CultureInfo.InvariantCulture);
        Validate();
    }

    private void Validate()
    {
        if (!int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            ValidationMessage = "Port must be a number.";
            return;
        }
        var paths = new LauncherPaths
        {
            DshRoot = DshRoot.Trim(), DshHome = DshHome.Trim(), ProfileDirectory = ProfileDirectory.Trim(),
            PluginRoot = PluginRoot.Trim(), ServiceScript = ServiceScript.Trim(), WebUrl = WebUrl.Trim(), Port = port,
            PnpmStore = Host.Settings.Paths.PnpmStore, PowerShellPath = Host.Settings.Paths.PowerShellPath,
            GitExecutable = Host.Settings.Paths.GitExecutable, PnpmExecutable = Host.Settings.Paths.PnpmExecutable
        };
        var result = new PathValidator().Validate(paths);
        ValidationMessage = result.IsValid
            ? "Paths are valid. " + string.Join(" ", result.Warnings)
            : string.Join(" ", result.Errors);
    }

    private async Task SaveAsync()
    {
        if (!int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            ValidationMessage = "Port must be between 1 and 65535.";
            return;
        }
        var paths = Host.Settings.Paths with
        {
            DshRoot = DshRoot.Trim(), DshHome = DshHome.Trim(), ProfileDirectory = ProfileDirectory.Trim(),
            PluginRoot = PluginRoot.Trim(), ServiceScript = ServiceScript.Trim(), WebUrl = WebUrl.Trim(), Port = port
        };
        try
        {
            await Host.SaveSettingsAsync(Host.Settings with { AutoDetectPaths = false, Paths = paths });
            ValidationMessage = "Saved. Restart the launcher to apply service-bound paths.";
            Status = "Settings saved";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ValidationMessage = exception.Message;
            Status = "Save failed";
        }
    }
}

public sealed class DeepSeekApiPageViewModel : PageViewModel
{
    private string _apiKeyInput = string.Empty;
    private string _credentialStatus = "Not loaded";
    private string _connectionStatus = "Not tested";
    private string _latency = "—";
    private string _balance = "Not loaded";
    private bool _isBusy;

    public DeepSeekApiPageViewModel(AvaloniaAppHost host)
        : base(host, "DeepSeek API", "API / 03", "Check connectivity, models and balance without sending billable chat requests.")
    {
        TestCommand = new AsyncCommand(TestAsync, () => !_isBusy);
        LoadModelsCommand = new AsyncCommand(LoadModelsAsync, () => !_isBusy);
        BalanceCommand = new AsyncCommand(BalanceAsync, () => !_isBusy);
        SaveKeyCommand = new AsyncCommand(SaveKeyAsync, () => !_isBusy);
        ClearKeyCommand = new AsyncCommand(ClearKeyAsync, () => !_isBusy);
        RefreshCredentialStatus();
    }

    public string ApiKeyInput { get => _apiKeyInput; set => SetProperty(ref _apiKeyInput, value); }
    public string CredentialStatus { get => _credentialStatus; private set => SetProperty(ref _credentialStatus, value); }
    public string ConnectionStatus { get => _connectionStatus; private set => SetProperty(ref _connectionStatus, value); }
    public string Latency { get => _latency; private set => SetProperty(ref _latency, value); }
    public string Balance { get => _balance; private set => SetProperty(ref _balance, value); }
    public ObservableCollection<string> Models { get; } = [];
    public ICommand TestCommand { get; }
    public ICommand LoadModelsCommand { get; }
    public ICommand BalanceCommand { get; }
    public ICommand SaveKeyCommand { get; }
    public ICommand ClearKeyCommand { get; }
    public ICommand GithubCommand => new RelayCommand(() => OpenExternal("https://github.com/deepseek-ai/deepseek-harness"));
    public ICommand ConsoleCommand => new RelayCommand(() => OpenExternal("https://platform.deepseek.com/usage"));

    public override Task RefreshAsync(CancellationToken cancellationToken)
    {
        RefreshCredentialStatus();
        return Task.CompletedTask;
    }

    private void RefreshCredentialStatus()
    {
        var status = Host.CredentialStore.ReadStatus(Host.Settings.Paths.CredentialsFile, "DEEPSEEK_API_KEY");
        CredentialStatus = $"{status.Message} · {status.MaskedValue} · file: {status.HasFileValue} · env override: {status.HasEnvironmentOverride}";
    }

    private string EffectiveKey() => string.IsNullOrWhiteSpace(ApiKeyInput)
        ? Host.CredentialStore.ReadSecret(Host.Settings.Paths.CredentialsFile, "DEEPSEEK_API_KEY") ?? string.Empty
        : ApiKeyInput.Trim();

    private async Task TestAsync()
    {
        await RunApiAsync(async key =>
        {
            var result = await Host.ApiClient.TestConnectionAsync(key, CancellationToken.None);
            ConnectionStatus = result.Message;
            Latency = result.Success ? $"{result.LatencyMs} ms" : "—";
            Status = result.Success ? "Connection healthy" : "Connection failed";
        });
    }

    private async Task LoadModelsAsync()
    {
        await RunApiAsync(async key =>
        {
            var models = await Host.ApiClient.GetModelsAsync(key, CancellationToken.None);
            Models.Clear();
            foreach (var model in models)
                Models.Add($"{model.Id} · {model.OwnedBy}");
            Status = $"Loaded {Models.Count} models";
        });
    }

    private async Task BalanceAsync()
    {
        await RunApiAsync(async key =>
        {
            var snapshot = await Host.ApiClient.GetBalanceAsync(key, CancellationToken.None);
            Balance = snapshot.IsAvailable && snapshot.Balances.Count > 0
                ? string.Join(" | ", snapshot.Balances.Select(item => $"{item.Currency} {item.TotalBalance:0.####}"))
                : snapshot.Message;
            Status = snapshot.IsAvailable ? "Balance loaded" : "Balance unavailable";
        });
    }

    private async Task SaveKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            Status = "Enter an API key first.";
            return;
        }
        _isBusy = true;
        try
        {
            await Host.CredentialStore.SetAsync(Host.Settings.Paths.CredentialsFile, "DEEPSEEK_API_KEY", ApiKeyInput.Trim(), CancellationToken.None);
            ApiKeyInput = string.Empty;
            RefreshCredentialStatus();
            Status = "Key saved locally.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = exception.Message;
        }
        finally { _isBusy = false; }
    }

    private async Task ClearKeyAsync()
    {
        _isBusy = true;
        try
        {
            await Host.CredentialStore.ClearAsync(Host.Settings.Paths.CredentialsFile, "DEEPSEEK_API_KEY", CancellationToken.None);
            RefreshCredentialStatus();
            Status = "File key cleared. Environment override, if present, remains effective.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Status = exception.Message; }
        finally { _isBusy = false; }
    }

    private async Task RunApiAsync(Func<string, Task> action)
    {
        var key = EffectiveKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            Status = "No API key configured.";
            return;
        }
        _isBusy = true;
        try { await action(key); }
        catch (Exception exception) when (exception is not OperationCanceledException) { Status = DescribeException(exception); }
        finally { _isBusy = false; }
    }
}

public sealed class SystemSettingsPageViewModel : PageViewModel
{
    private SystemInstructionFileInfo? _selected;
    private string _previewText = "Select a file to preview.";

    public SystemSettingsPageViewModel(AvaloniaAppHost host)
        : base(host, "系统级设置", "INSTRUCTIONS / 04", "Inspect AGENTS.md, CLAUDE.md and structured DSH settings in scope order.")
    {
        OpenSelectedCommand = new RelayCommand(OpenSelected);
    }

    public ObservableCollection<SystemInstructionFileInfo> Files { get; } = [];
    public SystemInstructionFileInfo? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            PreviewText = value is null || !File.Exists(value.Path) ? "No readable preview." : ReadPreview(value.Path);
        }
    }
    public string PreviewText { get => _previewText; private set => SetProperty(ref _previewText, value); }
    public ICommand OpenSelectedCommand { get; }

    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var files = await Host.InstructionScanner.ScanAsync(cancellationToken);
        Files.Clear();
        foreach (var file in files) Files.Add(file);
        Status = $"Found {Files.Count} instruction/settings files.";
    }

    private void OpenSelected()
    {
        if (Selected is { } item && File.Exists(item.Path)) OpenExternal(item.Path);
    }

    private static string ReadPreview(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.Length <= 12000 ? text : text[..12000] + Environment.NewLine + "…";
        }
        catch (Exception exception) { return exception.Message; }
    }
}

public sealed class PluginRowViewModel : ObservableObject
{
    private readonly Func<PluginRowViewModel, Task> _toggle;
    private string _status;
    private bool _isBusy;

    public PluginRowViewModel(PluginInfo info, Func<PluginRowViewModel, Task> toggle)
    {
        Info = info;
        _toggle = toggle;
        _status = info.RuntimeAvailable ? "Runtime" : "Discovered";
        ToggleCommand = new AsyncCommand(() => _toggle(this), () => !_isBusy && CanToggle);
    }

    public PluginInfo Info { get; private set; }
    public string Name => Info.Name;
    public string ModuleName => Info.ModuleName;
    public string Version => string.IsNullOrWhiteSpace(Info.Version) ? "—" : Info.Version;
    public string Source => string.IsNullOrWhiteSpace(Info.SourcePath) ? Info.SourceKind.ToString() : Info.SourcePath;
    public string Profile => Info.Profile;
    public string RuntimeState => Info.Enabled is null ? "unknown" : Info.Enabled.Value ? "enabled" : "disabled";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool CanToggle => !string.IsNullOrWhiteSpace(Info.ConfigId ?? Info.EntryId ?? Info.ModuleName)
                             && Info.SourceKind != PluginSourceKind.RuntimeOnly;
    public ICommand ToggleCommand { get; }

    public void Update(PluginInfo info)
    {
        Info = info;
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Version)); OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(Profile)); OnPropertyChanged(nameof(RuntimeState)); OnPropertyChanged(nameof(CanToggle));
    }

    public async Task RunToggleAsync(Func<Task> action)
    {
        _isBusy = true;
        try { await action(); }
        finally { _isBusy = false; }
    }
}

public sealed class PluginSettingsPageViewModel : PageViewModel
{
    public PluginSettingsPageViewModel(AvaloniaAppHost host)
        : base(host, "插件设置", "PLUGINS / 05", "Inventory profile, local and runtime plugins. Toggle state writes a safe patch backup.")
    {
    }

    public ObservableCollection<PluginRowViewModel> Plugins { get; } = [];

    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var items = await Host.PluginInventory.ScanAsync(cancellationToken);
        foreach (var row in Plugins.ToArray())
            if (!items.Any(item => string.Equals(item.ModuleName, row.Info.ModuleName, StringComparison.OrdinalIgnoreCase))) Plugins.Remove(row);
        foreach (var item in items)
        {
            var row = Plugins.FirstOrDefault(existing => string.Equals(existing.Info.ModuleName, item.ModuleName, StringComparison.OrdinalIgnoreCase));
            if (row is null) Plugins.Add(new PluginRowViewModel(item, ToggleAsync)); else row.Update(item);
        }
        Status = $"Found {Plugins.Count} plugins. Changes require a DSH restart.";
    }

    private async Task ToggleAsync(PluginRowViewModel row)
    {
        if (!row.CanToggle) { row.Status = "No stable config ID available."; return; }
        await row.RunToggleAsync(async () =>
        {
            var id = row.Info.ConfigId ?? row.Info.EntryId ?? row.Info.ModuleName;
            var packageDirectory = string.IsNullOrWhiteSpace(row.Info.SourcePath) ? Host.Settings.Paths.ProfileDirectory : row.Info.SourcePath;
            var fullConfig = ProfilePatchService.FindPluginConfigYaml(packageDirectory, id) ?? $"id: {id}\nname: {row.Info.Name}\n";
            var patchPath = Path.Combine(Host.Settings.Paths.DshHome, "cordis.patch.yml");
            if (!File.Exists(patchPath)) patchPath = Host.Settings.Paths.ProfilePatchFile;
            var enabled = row.Info.Enabled != true;
            var result = await Host.PatchService.SetPluginEnabledAsync(patchPath, id, fullConfig, enabled, CancellationToken.None);
            row.Status = result.Message + (result.BackupPath is null ? string.Empty : $" Backup: {result.BackupPath}");
            Status = result.Succeeded ? "Plugin patch saved; restart DSH to apply." : result.Message;
            if (result.Succeeded) await RefreshAsync(CancellationToken.None);
        });
    }
}

public sealed class LauncherSettingsPageViewModel : PageViewModel
{
    private string _themeName;
    private string _fontScale;
    private bool _navigationCollapsed;
    private bool _autoCollapse;
    private string _refreshSeconds;
    private bool _autoUpdate;
    private string _updateIntervalHours;

    public LauncherSettingsPageViewModel(AvaloniaAppHost host)
        : base(host, "启动器设置", "LAUNCHER / 06", "Tune the responsive shell, refresh cadence and GitHub update checks.")
    {
        var theme = host.Settings.Theme;
        _themeName = theme.Name; _fontScale = theme.FontScale.ToString(CultureInfo.InvariantCulture);
        _navigationCollapsed = theme.NavigationCollapsed; _autoCollapse = theme.AutoCollapseNavigation;
        _refreshSeconds = host.Settings.RefreshSeconds.ToString(CultureInfo.InvariantCulture);
        _autoUpdate = host.Settings.AutoUpdateEnabled;
        _updateIntervalHours = host.Settings.UpdateCheckIntervalHours.ToString(CultureInfo.InvariantCulture);
        SaveCommand = new AsyncCommand(SaveAsync);
        CheckUpdateCommand = new AsyncCommand(CheckUpdateAsync);
    }

    public string ThemeName { get => _themeName; set => SetProperty(ref _themeName, value); }
    public string FontScale { get => _fontScale; set => SetProperty(ref _fontScale, value); }
    public bool NavigationCollapsed { get => _navigationCollapsed; set => SetProperty(ref _navigationCollapsed, value); }
    public bool AutoCollapse { get => _autoCollapse; set => SetProperty(ref _autoCollapse, value); }
    public string RefreshSeconds { get => _refreshSeconds; set => SetProperty(ref _refreshSeconds, value); }
    public bool AutoUpdate { get => _autoUpdate; set => SetProperty(ref _autoUpdate, value); }
    public string UpdateIntervalHours { get => _updateIntervalHours; set => SetProperty(ref _updateIntervalHours, value); }
    public ICommand SaveCommand { get; }
    public ICommand CheckUpdateCommand { get; }

    private async Task SaveAsync()
    {
        var theme = Host.Settings.Theme with
        {
            Name = ThemeName.Trim(),
            FontScale = ParseClamped(FontScale, 80, 140, 100),
            NavigationCollapsed = NavigationCollapsed,
            AutoCollapseNavigation = AutoCollapse
        };
        var settings = Host.Settings with
        {
            Theme = theme,
            RefreshSeconds = ParseClamped(RefreshSeconds, 5, 120, 10),
            AutoUpdateEnabled = AutoUpdate,
            UpdateCheckIntervalHours = ParseClamped(UpdateIntervalHours, 6, 168, 24)
        };
        try
        {
            await Host.SaveSettingsAsync(settings);
            Status = "Saved. New UI metrics apply immediately; service paths apply after restart.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Status = exception.Message; }
    }

    private async Task CheckUpdateAsync()
    {
        if (!OperatingSystem.IsWindows()) { Status = "Self-install update is currently Windows-only."; return; }
        try
        {
            var result = await Host.LauncherUpdateService.CheckAsync(CancellationToken.None);
            Status = result.Message;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Status = DescribeException(exception); }
    }

    private static int ParseClamped(string value, int min, int max, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? Math.Clamp(number, min, max) : fallback;
}
