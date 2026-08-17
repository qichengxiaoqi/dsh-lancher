namespace DshPlusPlus.Core.Services;

public sealed record DshServiceScriptBackupHandle(
    string SourcePath,
    string BackupPath,
    string BackupDirectory);

public interface IDshServiceScriptBackup
{
    string PolicyDescription { get; }

    DshServiceScriptBackupHandle? Prepare();

    void Restore(DshServiceScriptBackupHandle handle);

    void Delete(DshServiceScriptBackupHandle handle);
}

public sealed class DshServiceScriptBackup : IDshServiceScriptBackup
{
    private readonly string _sourcePath;
    private readonly string _backupRoot;

    public DshServiceScriptBackup(string sourcePath, string? backupRoot = null)
    {
        _sourcePath = Path.GetFullPath(sourcePath);
        _backupRoot = Path.GetFullPath(
            backupRoot
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dsh++",
                "dsh-backups"));
    }

    public string PolicyDescription =>
        $"更新前备份 {Path.GetFileName(_sourcePath)}；更新并重启成功后删除备份；失败时恢复原脚本并保留备份；未知工作区修改仍会阻止拉取。";

    public DshServiceScriptBackupHandle? Prepare()
    {
        if (!File.Exists(_sourcePath))
            return null;

        Directory.CreateDirectory(_backupRoot);
        var backupDirectory = Path.Combine(
            _backupRoot,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, Path.GetFileName(_sourcePath));
        File.Copy(_sourcePath, backupPath, overwrite: false);
        return new DshServiceScriptBackupHandle(_sourcePath, backupPath, backupDirectory);
    }

    public void Restore(DshServiceScriptBackupHandle handle)
    {
        ValidateHandle(handle);
        if (!File.Exists(handle.BackupPath))
            throw new FileNotFoundException("DSH 自定义服务脚本备份不存在。", handle.BackupPath);

        Directory.CreateDirectory(Path.GetDirectoryName(handle.SourcePath)!);
        File.Copy(handle.BackupPath, handle.SourcePath, overwrite: true);
    }

    public void Delete(DshServiceScriptBackupHandle handle)
    {
        ValidateHandle(handle);
        if (Directory.Exists(handle.BackupDirectory))
            Directory.Delete(handle.BackupDirectory, recursive: true);
    }

    private void ValidateHandle(DshServiceScriptBackupHandle handle)
    {
        if (!string.Equals(
                Path.GetFullPath(handle.SourcePath),
                _sourcePath,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("服务脚本备份来源不匹配。");

        var backupDirectory = Path.GetFullPath(handle.BackupDirectory);
        var backupPath = Path.GetFullPath(handle.BackupPath);
        if (!IsWithin(backupDirectory, _backupRoot)
            || !string.Equals(
                backupPath,
                Path.Combine(backupDirectory, Path.GetFileName(backupPath)),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("服务脚本备份路径无效。");
    }

    private static bool IsWithin(string path, string parent)
    {
        var normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}
