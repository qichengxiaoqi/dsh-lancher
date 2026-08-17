using System.ComponentModel;
using System.Diagnostics;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var argumentCopy = arguments.ToArray();
        using var process = new Process
        {
            StartInfo = CreateStartInfo(fileName, argumentCopy, workingDirectory)
        };

        try
        {
            if (!process.Start())
            {
                return Failure(fileName, argumentCopy, "进程无法启动。");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Failure(fileName, argumentCopy, ex.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timedOut = false;
        var canceled = false;

        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            timedOut = true;
            TryStop(process);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            TryStop(process);
        }

        if (!process.HasExited)
            TryStop(process);

        if (!process.HasExited)
            await process.WaitForExitAsync(CancellationToken.None);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var exitCode = process.ExitCode;
        return new ProcessResult(fileName, argumentCopy, exitCode, stdout, stderr, timedOut, canceled);
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static ProcessResult Failure(string fileName, IReadOnlyList<string> arguments, string error) =>
        new(fileName, arguments.ToArray(), -1, string.Empty, error);

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (InvalidOperationException)
        {
        }
    }
}
