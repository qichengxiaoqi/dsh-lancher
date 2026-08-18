using System.Security.Cryptography;
using System.Text;

namespace DshPlusPlus.Core.Services;

public static class SkillContentHasher
{
    public static string Compute(
        string path,
        bool isDirectoryBundle,
        CancellationToken cancellationToken)
    {
        var root = isDirectoryBundle
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path))!;
        var files = isDirectoryBundle
            ? EnumerateRegularFiles(root, cancellationToken)
            : [Path.GetFullPath(path)];

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = isDirectoryBundle
                ? Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')
                : Path.GetFileName(file);
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            var info = new FileInfo(file);
            hash.AppendData(BitConverter.GetBytes(info.Length));
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, options: FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static IReadOnlyList<string> EnumerateRegularFiles(
        string root,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (IsReparsePoint(directory))
                throw new InvalidDataException("技能包包含目录链接");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(entry))
                    throw new InvalidDataException("技能包包含链接文件");
                if (Directory.Exists(entry))
                    pending.Push(entry);
                else if (File.Exists(entry))
                    files.Add(Path.GetFullPath(entry));
            }
        }
        return files;
    }

    public static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public static bool IsWithin(string path, string parent)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedPath,
                   normalizedParent.TrimEnd(Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }
}
