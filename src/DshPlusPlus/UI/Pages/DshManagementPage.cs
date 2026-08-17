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
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly List<Button> _operationButtons = [];
    private readonly StatusChip _statusChip;
    private readonly MetricCard _serviceCard;
    private readonly MetricCard _versionCard;
    private readonly MetricCard _commitCard;
    private readonly MetricCard _remoteCard;
    private readonly LogDrawer _log;
    private readonly GlowButton _pullButton;
    private UpdateCheckResult? _lastUpdateCheck;

    public override bool SupportsAutoRefresh => true;
    public ServiceState CurrentServiceState { get; private set; } = ServiceState.Stopped;

    public DshManagementPage(
        DshPaths paths,
        IDshServiceController serviceController,
        ServiceStatusProbe statusProbe,
        IGitRepositoryService gitRepository,
        UpdateCoordinator updateCoordinator,
        ThemeManager theme)
        : base(theme, "DSH 管理", "让 DeepSeek Harness 的启动、更新和运行状态一目了然。")
    {
        _paths = paths;
        _serviceController = serviceController;
        _statusProbe = statusProbe;
        _gitRepository = gitRepository;
        _updateCoordinator = updateCoordinator;
        _statusChip = new StatusChip("状态检测中", theme.Palette);
        _serviceCard = new MetricCard("运行状态", "检测中", _paths.WebUrl, theme.Palette);
        _versionCard = new MetricCard("本地版本", "读取中", "package.json", theme.Palette);
        _commitCard = new MetricCard("当前提交", "读取中", _paths.Root, theme.Palette);
        _remoteCard = new MetricCard("远程状态", "未检查", "git origin", theme.Palette);
        _log = new LogDrawer(theme.Palette);
        _pullButton = new GlowButton("拉取更新", theme.Palette, primary: true) { Enabled = false };
        Build();
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RefreshStatusAsync(cancellationToken);
        await RefreshRepositoryAsync(cancellationToken);
    }

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
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Theme.Palette.Background
        };
        for (var i = 0; i < 4; i++)
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        metrics.Controls.Add(_serviceCard, 0, 0);
        metrics.Controls.Add(_versionCard, 1, 0);
        metrics.Controls.Add(_commitCard, 2, 0);
        metrics.Controls.Add(_remoteCard, 3, 0);
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
        var check = ActionButton("检查更新", CheckUpdateAsync);
        _pullButton.Click += PullUpdateAsync;
        var web = ActionButton("打开 Web UI", (_, _) => OpenWebUi());
        actions.Controls.AddRange([start, stop, restart, check, _pullButton, web]);
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

    private async void CheckUpdateAsync(object? sender, EventArgs e) => await RunOperationAsync("检查更新", async cancellationToken =>
    {
        _lastUpdateCheck = await _gitRepository.CheckAsync(cancellationToken);
        if (_lastUpdateCheck.Snapshot is not null)
            ApplySnapshot(_lastUpdateCheck.Snapshot);
        _pullButton.Enabled = _lastUpdateCheck.CanPull;
        _remoteCard.SetValue(_lastUpdateCheck.State.ToString(), _lastUpdateCheck.Message);
        _log.Append($"检查更新：{_lastUpdateCheck.Message}");
    });

    private async void PullUpdateAsync(object? sender, EventArgs e)
    {
        if (_lastUpdateCheck is not { CanPull: true })
        {
            _log.Append("拉取更新已阻止：请先检查到可用更新");
            return;
        }
        if (MessageBox.Show(this, "将停止 DSH，执行 fast-forward 更新、安装依赖和构建。继续吗？", "确认拉取更新",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        await RunOperationAsync("拉取更新", async cancellationToken =>
        {
            SetStatus("更新中", Theme.Palette.Warning);
            var result = await _updateCoordinator.PullAsync(cancellationToken);
            if (result.ProcessResult is not null)
                LogProcess(result.Stage, result.ProcessResult);
            _log.Append($"更新阶段 {result.Stage}：{result.Message}");
            _lastUpdateCheck = null;
            _pullButton.Enabled = false;
            await RefreshAsync(cancellationToken);
        });
    }

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
        if (!busy)
            _pullButton.Enabled = _lastUpdateCheck?.CanPull == true;
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        var result = await _statusProbe.ProbeAsync(cancellationToken);
        CurrentServiceState = result.State;
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
        _commitCard.SetValue(snapshot.ShortSha, snapshot.Branch);
        _remoteCard.SetValue(snapshot.IsDirty ? "工作区有改动" : "工作区干净",
            snapshot.RemoteUrl);
    }

    private void SetStatus(string text, Color color)
    {
        _statusChip.SetState(text, color, Color.FromArgb(35, color));
    }

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
