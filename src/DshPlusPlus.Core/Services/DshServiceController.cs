using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public interface IDshServiceController
{
    Task<ProcessResult> StartAsync(CancellationToken cancellationToken);
    Task<ProcessResult> StopAsync(CancellationToken cancellationToken);
    Task<ProcessResult> RestartAsync(CancellationToken cancellationToken);
}

public sealed class DshServiceController : IDshServiceController
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RestartTimeout = TimeSpan.FromSeconds(120);

    private readonly IProcessRunner _processRunner;
    private readonly DshPaths _paths;

    public DshServiceController(IProcessRunner processRunner, DshPaths paths)
    {
        _processRunner = processRunner;
        _paths = paths;
    }

    public Task<ProcessResult> StartAsync(CancellationToken cancellationToken) =>
        RunActionAsync("Start", StartTimeout, cancellationToken);

    public Task<ProcessResult> StopAsync(CancellationToken cancellationToken) =>
        RunActionAsync("Stop", StopTimeout, cancellationToken);

    public Task<ProcessResult> RestartAsync(CancellationToken cancellationToken) =>
        RunActionAsync("Restart", RestartTimeout, cancellationToken);

    private Task<ProcessResult> RunActionAsync(
        string action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> arguments =
        [
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            _paths.ServiceScript,
            "-Action",
            action
        ];

        return _processRunner.RunAsync(
            _paths.PowerShellPath,
            arguments,
            _paths.Root,
            timeout,
            cancellationToken);
    }
}
