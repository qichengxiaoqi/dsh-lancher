namespace DshPlusPlus.Core.Models;

public sealed record DshPaths(
    string Root,
    string ServiceScript,
    string WebUrl,
    int Port,
    string PnpmStore,
    string PowerShellPath,
    string GitExecutable,
    string PnpmExecutable)
{
    public string PackageJsonPath => Path.Combine(Root, "package.json");

    public string GitDirectory => Path.Combine(Root, ".git");

    public IReadOnlyList<string> KnownLocalFiles
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Root))
                return [];

            var files = new[]
            {
                ServiceScript,
                Path.Combine(Root, "关闭DeepSeekHarness.bat"),
                Path.Combine(Root, "启动DeepSeekHarness.bat"),
                Path.Combine(Root, "更新DeepSeekHarness.bat"),
                Path.Combine(Root, "重启DeepSeekHarness.bat")
            };

            return files
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static DshPaths CreateDefault() => new(
        Root: string.Empty,
        ServiceScript: string.Empty,
        WebUrl: "http://127.0.0.1:3080",
        Port: 3080,
        PnpmStore: string.Empty,
        PowerShellPath: "powershell.exe",
        GitExecutable: "git.exe",
        PnpmExecutable: "pnpm.cmd");
}
