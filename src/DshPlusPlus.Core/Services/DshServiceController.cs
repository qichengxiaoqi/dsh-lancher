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
    private static readonly TimeSpan RecoveryProbeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RecoveryProbeInterval = TimeSpan.FromMilliseconds(400);

    private readonly IProcessRunner _processRunner;
    private readonly DshPaths _paths;
    private readonly Func<CancellationToken, Task<ServiceProbeResult>>? _readinessProbe;

    public DshServiceController(
        IProcessRunner processRunner,
        DshPaths paths,
        Func<CancellationToken, Task<ServiceProbeResult>>? readinessProbe = null)
    {
        _processRunner = processRunner;
        _paths = paths;
        _readinessProbe = readinessProbe;
    }

    public Task<ProcessResult> StartAsync(CancellationToken cancellationToken) =>
        RunActionAsync("Start", StartTimeout, cancellationToken);

    public Task<ProcessResult> StopAsync(CancellationToken cancellationToken) =>
        RunActionAsync("Stop", StopTimeout, cancellationToken);

    public Task<ProcessResult> RestartAsync(CancellationToken cancellationToken) =>
        RunActionAsync("Restart", RestartTimeout, cancellationToken);

    private async Task<ProcessResult> RunActionAsync(
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

        var result = await _processRunner.RunAsync(
            _paths.PowerShellPath,
            arguments,
            _paths.Root,
            timeout,
            cancellationToken);

        var readinessProbe = _readinessProbe;
        if (result.Canceled || !IsStartAction(action) || readinessProbe is null)
            return result;

        var readiness = await WaitForReadyAsync(readinessProbe, cancellationToken);
        if (readiness is not null)
        {
            if (result.Succeeded)
                return result;

            var recoveredDiagnostic =
                $"DSH became ready after the {action} script returned exit {result.ExitCode}. " +
                "The original process result was recovered from the service health check.";
            return result with
            {
                ExitCode = 0,
                TimedOut = false,
                StandardError = AppendDiagnostic(result.StandardError, recoveredDiagnostic)
            };
        }

        if (!result.Succeeded)
            return result;

        return result with
        {
            ExitCode = 1,
            StandardError = AppendDiagnostic(
                result.StandardError,
                $"The {action} script returned success, but DSH did not pass the host.describe health check within {RecoveryProbeTimeout.TotalSeconds:0} seconds.")
        };
    }

    private async Task<ServiceProbeResult?> WaitForReadyAsync(
        Func<CancellationToken, Task<ServiceProbeResult>> readinessProbe,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + RecoveryProbeTimeout;
        var consecutiveReady = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var result = await readinessProbe(cancellationToken);
                if (result.State == ServiceState.Running)
                {
                    consecutiveReady++;
                    if (consecutiveReady >= 2)
                        return result;
                }
                else
                {
                    consecutiveReady = 0;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                // A failed probe is expected while the service is still restarting.
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            try
            {
                await Task.Delay(
                    remaining < RecoveryProbeInterval ? remaining : RecoveryProbeInterval,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsStartAction(string action) =>
        string.Equals(action, "Start", StringComparison.OrdinalIgnoreCase)
        || string.Equals(action, "Restart", StringComparison.OrdinalIgnoreCase);

    private static string AppendDiagnostic(string existing, string diagnostic) =>
        string.IsNullOrWhiteSpace(existing)
            ? diagnostic
            : existing.TrimEnd() + Environment.NewLine + diagnostic;
}
