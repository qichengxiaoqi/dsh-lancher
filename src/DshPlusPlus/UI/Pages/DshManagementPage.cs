using System.Diagnostics;
using System.Drawing;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Pages;

public sealed class DshManagementPage : PageBase
{
    private readonly DshPaths _paths;
    private readonly IDshServiceController _serviceController;
    private readonly ServiceStatusProbe _statusProbe;
    private readonly IGitRepositoryService _gitRepository;
    private readonly DshPatchQueueService _patchQueue;
    private LauncherText _text;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly List<Button> _operationButtons = [];
    private readonly StatusChip _statusChip;
    private readonly MetricCard _serviceCard;
    private readonly MetricCard _versionCard;
    private readonly LogDrawer _log;
    private Label? _actionCardTitle;
    private Label? _logTitle;
    private UpdateCheckResult? _lastUpdateCheck;
    private DshPatchQueueSnapshot? _lastPatchQueue;

    public override bool SupportsAutoRefresh => true;
    public ServiceState CurrentServiceState { get; private set; } = ServiceState.Stopped;

    public DshManagementPage(
        DshPaths paths,
        IDshServiceController serviceController,
        ServiceStatusProbe statusProbe,
        IGitRepositoryService gitRepository,
        DshPatchQueueService patchQueue,
        ThemeManager theme,
        LauncherText? text = null)
        : base(
            theme,
            text?.Pick("DSH 管理", "DSH Management") ?? "DSH 管理",
            text?.Pick(
                "让 DeepSeek Harness 的启动、更新和运行状态一目了然。",
                "Start, update and monitor DeepSeek Harness from one place.")
                ?? "让 DeepSeek Harness 的启动、更新和运行状态一目了然。")
    {
        _text = text ?? LauncherTextCatalog.Get(LauncherLanguage.System);
        _paths = paths;
        _serviceController = serviceController;
        _statusProbe = statusProbe;
        _gitRepository = gitRepository;
        _patchQueue = patchQueue;
        _patchQueue.EnsureStorage();
        _statusChip = new StatusChip(_text.Pick("状态检测中", "Checking status"), theme.Palette);
        _serviceCard = new MetricCard(_text.Pick("运行状态", "Service status"), _text.Pick("检测中", "Checking"), _paths.WebUrl, theme.Palette);
        _versionCard = new MetricCard(_text.Pick("本地版本", "Local version"), _text.Pick("读取中", "Reading"), "package.json", theme.Palette);
        _log = new LogDrawer(theme.Palette);
        Build();
        ApplyLanguage(_text);
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken))
            return;

        try
        {
            await RefreshStatusCoreAsync(cancellationToken, forceRefresh: false);
            await RefreshRepositoryAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public event Action<ServiceState>? ServiceStateChanged;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _operationGate.Dispose();
            _refreshGate.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Build()
    {
        var layout = CreatePageLayout(4);
        layout.RowStyles[1] = new RowStyle(SizeType.Percent, 27);
        layout.RowStyles[2] = new RowStyle(SizeType.Percent, 20);
        layout.RowStyles[3] = new RowStyle(SizeType.Percent, 53);

        var statusLine = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 7, 0, 0)
        };
        statusLine.Controls.Add(_statusChip);
        layout.Controls[0].Controls.Add(statusLine);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Palette.Background
        };
        for (var i = 0; i < 2; i++)
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.Controls.Add(_serviceCard, 0, 0);
        metrics.Controls.Add(_versionCard, 1, 0);
        layout.Controls.Add(metrics, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(6),
            BackColor = Theme.Palette.Surface
        };
        var start = ActionButton(_text.Pick("启动服务", "Start service"), StartServiceAsync, primary: true);
        var stop = ActionButton(_text.Pick("停止服务", "Stop service"), StopServiceAsync);
        var restart = ActionButton(_text.Pick("重启服务", "Restart service"), RestartServiceAsync);
        var status = ActionButton(_text.Pick("手动检测 DSH 状态", "Check DSH status"), CheckStatusAsync);
        var check = ActionButton(_text.Pick("检查 DSH 更新", "Check DSH updates"), CheckUpdateAsync);
        var web = ActionButton(_text.Pick("打开 Web UI", "Open Web UI"), (_, _) => OpenWebUi());
        actions.Controls.AddRange([start, stop, restart, status, check, web]);
        var actionCard = Card(actions, _text.Pick("运行操作", "Operations"));
        _actionCardTitle = actionCard.Controls.OfType<Label>().FirstOrDefault();
        layout.Controls.Add(actionCard, 0, 2);

        var logPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Palette.Surface, Padding = new Padding(16) };
        _logTitle = new Label
        {
            Text = _text.Pick("运行日志", "Runtime log"),
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Theme.Palette.Text,
            Tag = "section"
        };
        logPanel.Controls.Add(_log);
        logPanel.Controls.Add(_logTitle);
        layout.Controls.Add(logPanel, 0, 3);
        Controls.Add(layout);
        _log.Append(_text.Pick("dsh++ 管理页已就绪", "dsh++ management page is ready"));
    }

    public override void ApplyLanguage(LauncherText text)
    {
        _text = text;
        ApplyHeader(
            text.Pick("DSH 管理", "DSH Management"),
            text.Pick(
                "让 DeepSeek Harness 的启动、更新和运行状态一目了然。",
                "Start, update and monitor DeepSeek Harness from one place."));
        _serviceCard.SetCaption(text.Pick("运行状态", "Service status"));
        _versionCard.SetCaption(text.Pick("本地版本", "Local version"));
        if (_actionCardTitle is not null)
            _actionCardTitle.Text = text.Pick("运行操作", "Operations");
        if (_logTitle is not null)
            _logTitle.Text = text.Pick("运行日志", "Runtime log");
        var labels = new[]
        {
            text.Pick("启动服务", "Start service"),
            text.Pick("停止服务", "Stop service"),
            text.Pick("重启服务", "Restart service"),
            text.Pick("手动检测 DSH 状态", "Check DSH status"),
            text.Pick("检查 DSH 更新", "Check DSH updates"),
            text.Pick("打开 Web UI", "Open Web UI")
        };
        for (var index = 0; index < Math.Min(labels.Length, _operationButtons.Count); index++)
            _operationButtons[index].Text = labels[index];
        SetStatus(DescribeServiceState(CurrentServiceState),
            CurrentServiceState == ServiceState.Running ? Theme.Palette.Success : Theme.Palette.Muted);
    }

    private string DescribeServiceState(ServiceState state) => state switch
    {
        ServiceState.Running => _text.Pick("运行中", "Running"),
        ServiceState.Stopped => _text.Pick("已停止", "Stopped"),
        ServiceState.StartFailed => _text.Pick("启动失败", "Start failed"),
        _ => _text.Pick("未知", "Unknown")
    };

    private GlowButton ActionButton(string text, EventHandler handler, bool primary = false)
    {
        var button = new GlowButton(text, Theme.Palette, primary);
        button.Click += handler;
        _operationButtons.Add(button);
        return button;
    }

    private async void StartServiceAsync(object? sender, EventArgs e) => await RunOperationAsync(_text.Pick("启动服务", "Start service"), async cancellationToken =>
    {
        SetStatus(_text.Pick("启动中", "Starting"), Theme.Palette.Warning);
        _log.Append(_text.Pick(
            "DSH 正在启动；首次启动可能需要几十秒，请不要重复点击。",
            "DSH is starting; the first launch may take several seconds. Please wait before clicking again."));
        var result = await _serviceController.StartAsync(cancellationToken);
        LogProcess("Start", result);
        if (result.Succeeded)
        {
            await RefreshStatusAsync(cancellationToken, forceRefresh: true);
            OpenWebUi();
        }
        else
        {
            SetStatus(_text.Pick("启动失败", "Start failed"), Theme.Palette.Danger);
        }
    });

    private async void StopServiceAsync(object? sender, EventArgs e) => await RunOperationAsync(_text.Pick("停止服务", "Stop service"), async cancellationToken =>
    {
        SetStatus(_text.Pick("停止中", "Stopping"), Theme.Palette.Warning);
        var result = await _serviceController.StopAsync(cancellationToken);
        LogProcess("Stop", result);
        await RefreshStatusAsync(cancellationToken, forceRefresh: true);
        if (!result.Succeeded)
            SetStatus(_text.Pick("停止失败", "Stop failed"), Theme.Palette.Danger);
    });

    private async void RestartServiceAsync(object? sender, EventArgs e) => await RunOperationAsync(_text.Pick("重启服务", "Restart service"), async cancellationToken =>
    {
        SetStatus(_text.Pick("重启中", "Restarting"), Theme.Palette.Warning);
        _log.Append(_text.Pick(
            "DSH 正在重启；停止旧进程并等待 Web 服务就绪可能需要几十秒。",
            "DSH is restarting; stopping the old process and waiting for the Web service may take several seconds."));
        var result = await _serviceController.RestartAsync(cancellationToken);
        LogProcess("Restart", result);
        if (result.Succeeded)
        {
            await RefreshStatusAsync(cancellationToken, forceRefresh: true);
            OpenWebUi();
        }
        else
        {
            SetStatus(_text.Pick("重启失败", "Restart failed"), Theme.Palette.Danger);
        }
    });

    private async void CheckStatusAsync(object? sender, EventArgs e) => await RunOperationAsync(
        _text.Pick("手动检测 DSH 状态", "Check DSH status"),
        async cancellationToken =>
        {
            await RefreshStatusAsync(cancellationToken, forceRefresh: true);
            _log.Append(_text.Pick(
                "DSH 手动状态检测已完成。",
                "Manual DSH status check completed."));
        });

    private async void CheckUpdateAsync(object? sender, EventArgs e) => await RunOperationAsync(_text.Pick("检查 DSH 更新", "Check DSH updates"), async cancellationToken =>
    {
        _lastUpdateCheck = await _gitRepository.CheckAsync(cancellationToken);
        if (_lastUpdateCheck.Snapshot is not null)
            ApplySnapshot(_lastUpdateCheck.Snapshot);
        try
        {
            _lastPatchQueue = await _patchQueue.InspectAsync(cancellationToken);
            _log.Append(_text.Pick(
                $"DSH 本地补丁：{_lastPatchQueue.BranchName}，提交 {_lastPatchQueue.CommitCount} 个；存储 {_lastPatchQueue.StoragePath}",
                $"DSH local patch: {_lastPatchQueue.BranchName}; {_lastPatchQueue.CommitCount} commits; storage: {_lastPatchQueue.StoragePath}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Append(_text.Pick($"读取 DSH 本地补丁状态失败：{ex.Message}", $"Failed to read DSH local patch state: {ex.Message}"));
        }
        if (_lastUpdateCheck.Snapshot is not null)
            ApplyUpdateClassification(_lastUpdateCheck.Snapshot);
        _log.Append(_text.Pick($"检查 DSH 更新：{_lastUpdateCheck.Message}", $"DSH update check: {_lastUpdateCheck.Message}"));
        _log.Append(_text.Pick(
            "DSH 更新策略：仅检查和提醒，不拉取、不切换分支、不 rebase、不重启 DSH。\n如需同步，请在 DSH 仓库外部手动完成，并自行保留本地插件与源码修改。",
            "DSH update policy: check and notify only; never pull, switch branches, rebase or restart DSH.\nTo synchronize, do it outside DSH and preserve local plugins and source changes yourself."));
    });

    private async Task RunOperationAsync(string name, Func<CancellationToken, Task> operation)
    {
        if (!await _operationGate.WaitAsync(0))
        {
            _log.Append(_text.Pick($"已忽略{name}：已有操作正在执行", $"Ignored {name}: another operation is already running"));
            return;
        }
        try
        {
            SetBusy(true);
            _log.Append(_text.Pick($"开始{name}", $"Starting {name}"));
            await operation(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus(_text.Pick("操作异常", "Operation failed"), Theme.Palette.Danger);
            _log.Append(_text.Pick($"{name}异常：{ex.Message}", $"{name} failed: {ex.Message}"));
        }
        finally
        {
            SetBusy(false);
            _operationGate.Release();
        }
    }

    private void SetBusy(bool busy)
    {
        foreach (var button in _operationButtons)
            button.Enabled = !busy;
    }

    private async Task RefreshStatusAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await RefreshStatusCoreAsync(cancellationToken, forceRefresh);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshStatusCoreAsync(
        CancellationToken cancellationToken,
        bool forceRefresh)
    {
        var result = await _statusProbe.ProbeDshAsync(cancellationToken, forceRefresh);
        CurrentServiceState = result.State;
        ServiceStateChanged?.Invoke(result.State);
        var color = result.State == ServiceState.Running ? Theme.Palette.Success
            : result.State == ServiceState.Stopped ? Theme.Palette.Muted : Theme.Palette.Warning;
        var stateText = DescribeServiceState(result.State);
        SetStatus(stateText, color);
        _serviceCard.SetValue(stateText, result.Message);
    }

    private async Task RefreshRepositoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            ApplySnapshot(await _gitRepository.ReadLocalSnapshotAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Append(_text.Pick($"读取 Git 状态失败：{ex.Message}", $"Failed to read Git status: {ex.Message}"));
        }
    }

    private void ApplySnapshot(RepositorySnapshot snapshot)
    {
        _versionCard.SetValue(snapshot.LocalPackageVersion, snapshot.Root);
    }

    private void SetStatus(string text, Color color)
    {
        _statusChip.SetState(text, color, Color.FromArgb(35, color));
    }

    private void ApplyUpdateClassification(RepositorySnapshot snapshot)
    {
        var patchPath = _patchQueue.Layout.PatchDirectory;
        if (snapshot.TrackedProtectedChanges.Count > 0)
        {
            SetRepositoryNotice(
                _text.Pick("受保护脚本已被 Git 跟踪", "Protected script is tracked by Git"),
                _text.Pick(
                    $"检测到 {snapshot.TrackedProtectedChanges.Count} 个已跟踪的自定义文件；dsh++ 只提醒，不会拉取、覆盖或修改该文件。请在 DSH 仓库外部处理。 ",
                    $"Found {snapshot.TrackedProtectedChanges.Count} tracked custom file(s). dsh++ only warns and will not pull, overwrite or modify them. Handle this outside the DSH repository."));
            return;
        }

        if (snapshot.SourceChanges.Count > 0)
        {
            SetRepositoryNotice(
                _text.Pick("DSH 源码存在本地修改", "DSH source has local changes"),
                _text.Pick(
                    $"检测到 {snapshot.SourceChanges.Count} 个已跟踪源码修改；dsh++ 仅提醒，不会拉取、覆盖或 push。补丁存储：{patchPath}",
                    $"Found {snapshot.SourceChanges.Count} tracked source change(s). dsh++ only warns and will not pull, overwrite or push. Patch storage: {patchPath}"));
            return;
        }

        if (snapshot.UnknownChanges.Count > 0)
        {
            SetRepositoryNotice(
                _text.Pick("DSH 有未知未跟踪文件", "DSH has unknown untracked files"),
                _text.Pick(
                    $"检测到 {snapshot.UnknownChanges.Count} 个未跟踪文件；dsh++ 仅提醒，不会清理或覆盖。补丁存储：{patchPath}",
                    $"Found {snapshot.UnknownChanges.Count} untracked file(s). dsh++ only warns and will not clean or overwrite them. Patch storage: {patchPath}"));
            return;
        }

        if (snapshot.IsPatchBranch && snapshot.Ahead > 0 && snapshot.Behind > 0)
        {
            SetRepositoryNotice(
                _text.Pick("本地补丁分支有待同步差异", "Local patch branch has synchronization differences"),
                _text.Pick(
                    $"当前分支 {snapshot.Branch}：本地 {snapshot.Ahead} 个补丁提交，官方新增 {snapshot.Behind} 个提交。dsh++ 仅提醒，不会切换分支或执行 rebase。",
                    $"Branch {snapshot.Branch}: {snapshot.Ahead} local patch commit(s), {snapshot.Behind} upstream commit(s). dsh++ only warns and will not switch branches or rebase."));
            return;
        }

        if (snapshot.ProtectedLocalChanges.Count > 0)
        {
            SetRepositoryNotice(
                _text.Pick("DSH 自定义文件已识别", "DSH custom files detected"),
                _text.Pick(
                    $"检测到 {snapshot.ProtectedLocalChanges.Count} 个受保护本地文件；dsh++ 只读取并提醒，不会拉取、覆盖或 push。补丁存储：{patchPath}",
                    $"Found {snapshot.ProtectedLocalChanges.Count} protected local file(s). dsh++ only reads and warns; it will not pull, overwrite or push. Patch storage: {patchPath}"));
            return;
        }

        SetRepositoryNotice(
            snapshot.IsPatchBranch ? _text.Pick("DSH 本地补丁分支", "DSH local patch branch") : _text.Pick("DSH 源码状态", "DSH source status"),
            snapshot.IsPatchBranch
                ? _text.Pick($"当前使用 {snapshot.Branch}；dsh++ 不会向官方仓库 push。补丁存储：{patchPath}", $"Using {snapshot.Branch}; dsh++ will not push to the upstream repository. Patch storage: {patchPath}")
                : _text.Pick($"仅同步 DSH 官方源码；dsh++ Release 更新与 DSH Git 更新相互独立。补丁存储：{patchPath}", $"Only the official DSH source is synchronized; dsh++ Release updates and DSH Git updates are independent. Patch storage: {patchPath}"));
    }

    private void SetRepositoryNotice(string title, string detail) =>
        _log.Append(_text.Pick($"DSH 仓库检查：{title}；{detail}", $"DSH repository check: {title}; {detail}"));

    private void LogProcess(string stage, ProcessResult result)
    {
        _log.Append(_text.Pick(
            $"{stage}: exit={result.ExitCode}, 成功={result.Succeeded}, 超时={result.TimedOut}",
            $"{stage}: exit={result.ExitCode}, success={result.Succeeded}, timed out={result.TimedOut}"));
        var output = result.CombinedOutput.Trim();
        if (output.Length > 0)
            _log.Append(output.Length > 1600 ? output[..1600] + "..." : output);
    }

    private void OpenWebUi()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _paths.WebUrl, UseShellExecute = true });
            _log.Append(_text.Pick($"已打开 Web UI：{_paths.WebUrl}", $"Opened Web UI: {_paths.WebUrl}"));
        }
        catch (Exception ex)
        {
            _log.Append(_text.Pick($"打开 Web UI 失败：{ex.Message}", $"Failed to open Web UI: {ex.Message}"));
        }
    }
}
