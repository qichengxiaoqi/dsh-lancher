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
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly List<Button> _operationButtons = [];
    private readonly StatusChip _statusChip;
    private readonly MetricCard _serviceCard;
    private readonly MetricCard _versionCard;
    private readonly LogDrawer _log;
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
        ThemeManager theme)
        : base(theme, "DSH 管理", "让 DeepSeek Harness 的启动、更新和运行状态一目了然。")
    {
        _paths = paths;
        _serviceController = serviceController;
        _statusProbe = statusProbe;
        _gitRepository = gitRepository;
        _patchQueue = patchQueue;
        _patchQueue.EnsureStorage();
        _statusChip = new StatusChip("状态检测中", theme.Palette);
        _serviceCard = new MetricCard("运行状态", "检测中", _paths.WebUrl, theme.Palette);
        _versionCard = new MetricCard("本地版本", "读取中", "package.json", theme.Palette);
        _log = new LogDrawer(theme.Palette);
        Build();
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RefreshStatusAsync(cancellationToken);
        await RefreshRepositoryAsync(cancellationToken);
    }

    public event Action<ServiceState>? ServiceStateChanged;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _operationGate.Dispose();
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
        var start = ActionButton("启动服务", StartServiceAsync, primary: true);
        var stop = ActionButton("停止服务", StopServiceAsync);
        var restart = ActionButton("重启服务", RestartServiceAsync);
        var check = ActionButton("检查 DSH 更新", CheckUpdateAsync);
        var web = ActionButton("打开 Web UI", (_, _) => OpenWebUi());
        actions.Controls.AddRange([start, stop, restart, check, web]);
        layout.Controls.Add(Card(actions, "运行操作"), 0, 2);

        var logPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Palette.Surface, Padding = new Padding(16) };
        var logTitle = new Label
        {
            Text = "运行日志",
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Theme.Palette.Text,
            Tag = "section"
        };
        logPanel.Controls.Add(_log);
        logPanel.Controls.Add(logTitle);
        layout.Controls.Add(logPanel, 0, 3);
        Controls.Add(layout);
        _log.Append("dsh++ 管理页已就绪");
    }

    private GlowButton ActionButton(string text, EventHandler handler, bool primary = false)
    {
        var button = new GlowButton(text, Theme.Palette, primary);
        button.Click += handler;
        _operationButtons.Add(button);
        return button;
    }

    private async void StartServiceAsync(object? sender, EventArgs e) => await RunOperationAsync("启动服务", async cancellationToken =>
    {
        SetStatus("启动中", Theme.Palette.Warning);
        var result = await _serviceController.StartAsync(cancellationToken);
        LogProcess("Start", result);
        if (result.Succeeded)
        {
            await RefreshStatusAsync(cancellationToken);
            OpenWebUi();
        }
        else
        {
            SetStatus("启动失败", Theme.Palette.Danger);
        }
    });

    private async void StopServiceAsync(object? sender, EventArgs e) => await RunOperationAsync("停止服务", async cancellationToken =>
    {
        SetStatus("停止中", Theme.Palette.Warning);
        var result = await _serviceController.StopAsync(cancellationToken);
        LogProcess("Stop", result);
        await RefreshStatusAsync(cancellationToken);
        if (!result.Succeeded)
            SetStatus("停止失败", Theme.Palette.Danger);
    });

    private async void RestartServiceAsync(object? sender, EventArgs e) => await RunOperationAsync("重启服务", async cancellationToken =>
    {
        SetStatus("重启中", Theme.Palette.Warning);
        var result = await _serviceController.RestartAsync(cancellationToken);
        LogProcess("Restart", result);
        if (result.Succeeded)
        {
            await RefreshStatusAsync(cancellationToken);
            OpenWebUi();
        }
        else
        {
            SetStatus("重启失败", Theme.Palette.Danger);
        }
    });

    private async void CheckUpdateAsync(object? sender, EventArgs e) => await RunOperationAsync("检查 DSH 更新", async cancellationToken =>
    {
        _lastUpdateCheck = await _gitRepository.CheckAsync(cancellationToken);
        if (_lastUpdateCheck.Snapshot is not null)
            ApplySnapshot(_lastUpdateCheck.Snapshot);
        try
        {
            _lastPatchQueue = await _patchQueue.InspectAsync(cancellationToken);
            _log.Append(
                $"DSH 本地补丁：{_lastPatchQueue.BranchName}，提交 {_lastPatchQueue.CommitCount} 个；存储 {_lastPatchQueue.StoragePath}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Append($"读取 DSH 本地补丁状态失败：{ex.Message}");
        }
        if (_lastUpdateCheck.Snapshot is not null)
            ApplyUpdateClassification(_lastUpdateCheck.Snapshot);
        _log.Append($"检查 DSH 更新：{_lastUpdateCheck.Message}");
        _log.Append("DSH 更新策略：仅检查和提醒，不拉取、不切换分支、不 rebase、不重启 DSH。\n" +
                    "如需同步，请在 DSH 仓库外部手动完成，并自行保留本地插件与源码修改。");
    });

    private async Task RunOperationAsync(string name, Func<CancellationToken, Task> operation)
    {
        if (!await _operationGate.WaitAsync(0))
        {
            _log.Append($"已忽略{name}：已有操作正在执行");
            return;
        }
        try
        {
            SetBusy(true);
            _log.Append($"开始{name}");
            await operation(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("操作异常", Theme.Palette.Danger);
            _log.Append($"{name}异常：{ex.Message}");
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

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        var result = await _statusProbe.ProbeAsync(cancellationToken);
        CurrentServiceState = result.State;
        ServiceStateChanged?.Invoke(result.State);
        var color = result.State == ServiceState.Running ? Theme.Palette.Success
            : result.State == ServiceState.Stopped ? Theme.Palette.Muted : Theme.Palette.Warning;
        SetStatus(result.State.ToString(), color);
        _serviceCard.SetValue(result.State.ToString(), result.Message);
    }

    private async Task RefreshRepositoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            ApplySnapshot(await _gitRepository.ReadLocalSnapshotAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Append($"读取 Git 状态失败：{ex.Message}");
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
                "受保护脚本已被 Git 跟踪",
                $"检测到 {snapshot.TrackedProtectedChanges.Count} 个已跟踪的自定义文件；dsh++ 只提醒，不会拉取、覆盖或修改该文件。请在 DSH 仓库外部处理。 ");
            return;
        }

        if (snapshot.SourceChanges.Count > 0)
        {
            SetRepositoryNotice(
                "DSH 源码存在本地修改",
                $"检测到 {snapshot.SourceChanges.Count} 个已跟踪源码修改；dsh++ 仅提醒，不会拉取、覆盖或 push。补丁存储：{patchPath}");
            return;
        }

        if (snapshot.UnknownChanges.Count > 0)
        {
            SetRepositoryNotice(
                "DSH 有未知未跟踪文件",
                $"检测到 {snapshot.UnknownChanges.Count} 个未跟踪文件；dsh++ 仅提醒，不会清理或覆盖。补丁存储：{patchPath}");
            return;
        }

        if (snapshot.IsPatchBranch && snapshot.Ahead > 0 && snapshot.Behind > 0)
        {
            SetRepositoryNotice(
                "本地补丁分支有待同步差异",
                $"当前分支 {snapshot.Branch}：本地 {snapshot.Ahead} 个补丁提交，官方新增 {snapshot.Behind} 个提交。dsh++ 仅提醒，不会切换分支或执行 rebase。");
            return;
        }

        if (snapshot.ProtectedLocalChanges.Count > 0)
        {
            SetRepositoryNotice(
                "DSH 自定义文件已识别",
                $"检测到 {snapshot.ProtectedLocalChanges.Count} 个受保护本地文件；dsh++ 只读取并提醒，不会拉取、覆盖或 push。补丁存储：{patchPath}");
            return;
        }

        SetRepositoryNotice(
            snapshot.IsPatchBranch ? "DSH 本地补丁分支" : "DSH 源码状态",
            snapshot.IsPatchBranch
                ? $"当前使用 {snapshot.Branch}；dsh++ 不会向官方仓库 push。补丁存储：{patchPath}"
                : $"仅同步 DSH 官方源码；dsh++ Release 更新与 DSH Git 更新相互独立。补丁存储：{patchPath}");
    }

    private void SetRepositoryNotice(string title, string detail) =>
        _log.Append($"DSH 仓库检查：{title}；{detail}");

    private void LogProcess(string stage, ProcessResult result)
    {
        _log.Append($"{stage}: exit={result.ExitCode}, 成功={result.Succeeded}, 超时={result.TimedOut}");
        var output = result.CombinedOutput.Trim();
        if (output.Length > 0)
            _log.Append(output.Length > 1600 ? output[..1600] + "..." : output);
    }

    private void OpenWebUi()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _paths.WebUrl, UseShellExecute = true });
            _log.Append($"已打开 Web UI：{_paths.WebUrl}");
        }
        catch (Exception ex)
        {
            _log.Append($"打开 Web UI 失败：{ex.Message}");
        }
    }
}
