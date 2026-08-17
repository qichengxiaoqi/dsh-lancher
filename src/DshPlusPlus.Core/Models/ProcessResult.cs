namespace DshPlusPlus.Core.Models;

public sealed record ProcessResult(
    string FileName,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool Canceled = false)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut && !Canceled;

    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(text => text.Length > 0));
}
