using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class UpdateCoordinator
{
    private readonly IGitRepositoryService _gitRepository;
    private readonly IProjectCommandService _projectCommands;
    private readonly IDshServiceController _serviceController;

    public UpdateCoordinator(
        IGitRepositoryService gitRepository,
        IProjectCommandService projectCommands,
        IDshServiceController serviceController)
    {
        _gitRepository = gitRepository;
        _projectCommands = projectCommands;
        _serviceController = serviceController;
    }

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

        var stop = await _serviceController.StopAsync(cancellationToken);
        if (!stop.Succeeded)
            return Failure(UpdateState.Error, "stop", "关闭服务失败", stop, snapshot);

        var pull = await _gitRepository.PullFastForwardOnlyAsync(remoteRef, cancellationToken);
        if (!pull.Succeeded)
            return Failure(UpdateState.Error, "pull", "git pull --ff-only 失败", pull, snapshot);

        var install = await _projectCommands.InstallDependenciesAsync(cancellationToken);
        if (!install.Succeeded)
            return Failure(UpdateState.Error, "install", "pnpm install 失败", install, snapshot);

        var build = await _projectCommands.BuildAsync(cancellationToken);
        if (!build.Succeeded)
            return Failure(UpdateState.Error, "build", "pnpm run build 失败", build, snapshot);

        var start = await _serviceController.StartAsync(cancellationToken);
        if (!start.Succeeded)
            return Failure(UpdateState.Error, "start", "重新启动服务失败", start, snapshot);

        return new UpdateOperationResult(
            Succeeded: true,
            State: UpdateState.Latest,
            Stage: "complete",
            Message: "更新完成，服务已重新启动。",
            Snapshot: snapshot);
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
