using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class UpdateCoordinator
{
    private readonly IGitRepositoryService _gitRepository;
    private readonly IProjectCommandService _projectCommands;
    private readonly IDshServiceController _serviceController;
    private readonly IDshServiceScriptBackup? _serviceScriptBackup;

    public UpdateCoordinator(
        IGitRepositoryService gitRepository,
        IProjectCommandService projectCommands,
        IDshServiceController serviceController,
        IDshServiceScriptBackup? serviceScriptBackup = null)
    {
        _gitRepository = gitRepository;
        _projectCommands = projectCommands;
        _serviceController = serviceController;
        _serviceScriptBackup = serviceScriptBackup;
    }

    public string BackupPolicyDescription => _serviceScriptBackup?.PolicyDescription
                                             ?? "更新前不备份自定义服务脚本。";

    public async Task<UpdateOperationResult> PullAsync(CancellationToken cancellationToken)
    {
        var check = await _gitRepository.CheckAsync(cancellationToken);
        if (!check.CanPull || check.Snapshot is null)
        {
            return new UpdateOperationResult(
                Succeeded: false,
                State: check.State,
                Stage: "check",
                Message: check.Message,
                Snapshot: check.Snapshot);
        }

        var snapshot = check.Snapshot;
        if (snapshot.ResolvedRemoteRef is not { Length: > 0 } remoteRef)
        {
            return new UpdateOperationResult(
                Succeeded: false,
                State: UpdateState.NoUpstream,
                Stage: "check",
                Message: "没有可用于拉取的远程 ref。",
                Snapshot: snapshot);
        }

        DshServiceScriptBackupHandle? backup = null;
        try
        {
            backup = _serviceScriptBackup?.Prepare();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UpdateOperationResult(
                Succeeded: false,
                State: UpdateState.Error,
                Stage: "backup",
                Message: $"备份 DSH 自定义服务脚本失败：{ex.Message}",
                Snapshot: snapshot);
        }

        var stop = await _serviceController.StopAsync(cancellationToken);
        if (!stop.Succeeded)
            return Failure(UpdateState.Error, "stop", "关闭服务失败", stop, snapshot);

        var pull = await _gitRepository.PullFastForwardOnlyAsync(remoteRef, cancellationToken);
        if (!pull.Succeeded)
            return FailureWithRestore(
                UpdateState.Error, "pull", "git pull --ff-only 失败", pull, snapshot, backup);

        var install = await _projectCommands.InstallDependenciesAsync(cancellationToken);
        if (!install.Succeeded)
            return FailureWithRestore(
                UpdateState.Error, "install", "pnpm install 失败", install, snapshot, backup);

        var build = await _projectCommands.BuildAsync(cancellationToken);
        if (!build.Succeeded)
            return FailureWithRestore(
                UpdateState.Error, "build", "pnpm run build 失败", build, snapshot, backup);

        var start = await _serviceController.StartAsync(cancellationToken);
        if (!start.Succeeded)
            return FailureWithRestore(
                UpdateState.Error, "start", "重新启动服务失败", start, snapshot, backup);

        if (backup is not null && _serviceScriptBackup is not null)
        {
            try
            {
                _serviceScriptBackup.Delete(backup);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new UpdateOperationResult(
                    Succeeded: false,
                    State: UpdateState.Error,
                    Stage: "cleanup",
                    Message: $"DSH 已更新并启动，但自定义服务脚本备份删除失败：{ex.Message}",
                    ProcessResult: start,
                    Snapshot: snapshot);
            }
        }

        return new UpdateOperationResult(
            Succeeded: true,
            State: UpdateState.Latest,
            Stage: "complete",
            Message: backup is null
                ? "DSH 更新完成，服务已重新启动；未找到自定义服务脚本，未创建备份。"
                : "DSH 更新完成，服务已重新启动；自定义服务脚本备份已删除。",
            Snapshot: snapshot);
    }

    private UpdateOperationResult FailureWithRestore(
        UpdateState state,
        string stage,
        string message,
        ProcessResult processResult,
        RepositorySnapshot snapshot,
        DshServiceScriptBackupHandle? backup)
    {
        var result = Failure(state, stage, message, processResult, snapshot);
        if (backup is null || _serviceScriptBackup is null)
            return result;

        try
        {
            _serviceScriptBackup.Restore(backup);
            return result with { Message = result.Message + "；自定义服务脚本已恢复，备份保留。" };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result with
            {
                Message = result.Message + $"；自定义服务脚本恢复失败，备份仍保留：{ex.Message}"
            };
        }
    }

    private static UpdateOperationResult Failure(
        UpdateState state,
        string stage,
        string message,
        ProcessResult processResult,
        RepositorySnapshot snapshot) =>
        new(
            Succeeded: false,
            State: state,
            Stage: stage,
            Message: $"{message}：{Summarize(processResult)}",
            ProcessResult: processResult,
            Snapshot: snapshot);

    private static string Summarize(ProcessResult result)
    {
        var text = result.CombinedOutput;
        return text.Length <= 600 ? text : text[..600] + "…";
    }
}
