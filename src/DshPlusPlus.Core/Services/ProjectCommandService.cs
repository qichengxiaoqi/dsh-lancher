using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public interface IProjectCommandService
{
    Task<ProcessResult> InstallDependenciesAsync(CancellationToken cancellationToken);
    Task<ProcessResult> BuildAsync(CancellationToken cancellationToken);
}

public sealed class ProjectCommandService : IProjectCommandService
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    private readonly DshPaths _paths;
    private readonly IProcessRunner _processRunner;

    public ProjectCommandService(DshPaths paths, IProcessRunner processRunner)
    {
        _paths = paths;
        _processRunner = processRunner;
    }

    public Task<ProcessResult> InstallDependenciesAsync(CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "install" };
        if (!string.IsNullOrWhiteSpace(_paths.PnpmStore))
            arguments.AddRange(["--store-dir", _paths.PnpmStore]);

        return RunPnpmAsync(arguments, InstallTimeout, cancellationToken);
    }

    public Task<ProcessResult> BuildAsync(CancellationToken cancellationToken) =>
        RunPnpmAsync(["run", "build"], BuildTimeout, cancellationToken);

    private Task<ProcessResult> RunPnpmAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var executable = File.Exists(_paths.PnpmExecutable)
            ? _paths.PnpmExecutable
            : "pnpm.cmd";
        return _processRunner.RunAsync(
            executable,
            arguments,
            _paths.Root,
            timeout,
            cancellationToken);
    }
}
