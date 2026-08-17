using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class LauncherUpdateService
{
    public const string Repository = "qichengxiaoqi/dsh-lancher";

    private static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{Repository}/releases/latest");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const long MaxAssetBytes = 250L * 1024 * 1024;
    private const string InstallerScript = """
param(
    [int]$ParentPid,
    [string]$Source,
    [string]$Target,
    [string]$ErrorPath,
    [string]$ScriptPath
)

try {
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($null -eq (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) {
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if ($null -ne (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) {
        throw "主程序未能退出。"
    }

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "更新文件不存在。"
    }

    Move-Item -LiteralPath $Source -Destination $Target -Force
    Start-Process -FilePath $Target
}
catch {
    try {
        [System.IO.File]::WriteAllText($ErrorPath, "dsh++ 更新失败。")
    }
    catch {
    }
}
finally {
    Remove-Item -LiteralPath $ScriptPath -Force -ErrorAction SilentlyContinue
}
""";

    private readonly HttpClient _httpClient;
    private readonly string _applicationPath;
    private readonly string _applicationDirectory;
    private readonly Version _currentVersion;
    private readonly TimeSpan _requestTimeout;

    public LauncherUpdateService(
        HttpClient httpClient,
        string? applicationPath = null,
        Version? currentVersion = null,
        TimeSpan? requestTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _applicationPath = Path.GetFullPath(
            applicationPath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定启动器程序路径。"));
        _applicationDirectory = Path.GetDirectoryName(_applicationPath)
                                ?? throw new InvalidOperationException("启动器程序目录无效。");
        _currentVersion = currentVersion ?? ReadCurrentVersion();
        _requestTimeout = requestTimeout is { } timeout && timeout > TimeSpan.Zero
            ? timeout
            : TimeSpan.FromSeconds(10);
    }

    public Version CurrentVersion => _currentVersion;

    public async Task<LauncherUpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
            request.Headers.UserAgent.ParseAdd("dsh++");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Failure(DescribeStatus(response.StatusCode));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var dto = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
                stream,
                JsonOptions,
                timeout.Token);
            if (dto is null || dto.Draft || dto.Prerelease)
            {
                return new LauncherUpdateCheckResult(
                    Succeeded: true,
                    UpdateAvailable: false,
                    CurrentVersion: _currentVersion,
                    LatestVersion: null,
                    Message: "没有可用的稳定 Release。" );
            }

            if (!TryCreateRelease(dto, out var release))
            {
                return Failure("Release 版本或下载资产格式无效。");
            }

            if (release.ExecutableAsset is null)
            {
                return Failure("最新 Release 缺少 dsh++.exe。", release.Version);
            }

            var updateAvailable = release.Version > _currentVersion;
            return new LauncherUpdateCheckResult(
                Succeeded: true,
                UpdateAvailable: updateAvailable,
                CurrentVersion: _currentVersion,
                LatestVersion: release.Version,
                Message: updateAvailable
                    ? $"发现新版本 {release.Version}。"
                    : $"当前已是最新版本 {_currentVersion}。",
                Release: release);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure("检查更新超时。");
        }
        catch (HttpRequestException)
        {
            return Failure("无法连接 GitHub 更新服务。");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Failure($"检查 GitHub Release 失败：{ex.Message}");
        }
        catch (JsonException)
        {
            return Failure("GitHub Release 数据格式无效。");
        }
    }

    public async Task<LauncherUpdateDownloadResult> DownloadAndPrepareAsync(
        LauncherReleaseInfo release,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null)
    {
        var asset = release.ExecutableAsset;
        if (asset is null)
            return DownloadFailure("Release 缺少 dsh++.exe。");
        if (!IsAllowedDownloadUri(asset.DownloadUri))
            return DownloadFailure("下载地址不是受支持的 GitHub HTTPS 地址。");

        var updateDirectory = Path.Combine(
            _applicationDirectory,
            ".dsh++-update",
            Guid.NewGuid().ToString("N"));
        var preparedPath = Path.Combine(updateDirectory, "dsh++.exe.download");
        var succeeded = false;

        try
        {
            Directory.CreateDirectory(updateDirectory);
            using var timeout = CreateTimeout(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUri);
            request.Headers.UserAgent.ParseAdd("dsh++");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
                return DownloadFailure(DescribeStatus(response.StatusCode));

            var declaredLength = response.Content.Headers.ContentLength ?? asset.Size;
            if (declaredLength is > MaxAssetBytes)
                return DownloadFailure("更新文件超过 250 MB 限制。");

            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var output = new FileStream(
                preparedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
            {
                total += read;
                if (total > MaxAssetBytes)
                    return DownloadFailure("更新文件超过 250 MB 限制。");
                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                hash.AppendData(buffer, 0, read);
                if (declaredLength is > 0)
                    progress?.Report(Math.Min(1d, total / (double)declaredLength.Value));
            }

            await output.FlushAsync(timeout.Token);
            var sha256 = Convert.ToHexString(hash.GetHashAndReset());
            var digestVerified = false;
            if (!string.IsNullOrWhiteSpace(asset.Digest))
            {
                if (!TryParseSha256(asset.Digest, out var expectedDigest))
                    return DownloadFailure("Release digest 格式无效。");
                if (!string.Equals(expectedDigest, sha256, StringComparison.OrdinalIgnoreCase))
                    return DownloadFailure("更新文件 SHA-256 校验失败。");
                digestVerified = true;
            }

            progress?.Report(1d);
            succeeded = true;
            return new LauncherUpdateDownloadResult(
                Succeeded: true,
                Message: digestVerified
                    ? "更新文件已下载并通过 SHA-256 校验。"
                    : "更新文件已下载并完成本地 SHA-256 计算。",
                PreparedPath: preparedPath,
                Sha256: sha256,
                DigestVerified: digestVerified);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DownloadFailure("下载更新超时。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return DownloadFailure("无法下载 GitHub Release 资产。");
        }
        catch (IOException)
        {
            return DownloadFailure("无法写入更新临时文件，请检查启动器目录权限。");
        }
        finally
        {
            if (!succeeded)
                DeleteDirectory(updateDirectory);
        }
    }

    public bool TryStartInstaller(string preparedPath, out string message)
    {
        try
        {
            var fullPreparedPath = Path.GetFullPath(preparedPath);
            var updateRoot = Path.GetFullPath(Path.Combine(_applicationDirectory, ".dsh++-update"));
            if (!IsWithin(fullPreparedPath, updateRoot) || !File.Exists(fullPreparedPath))
            {
                message = "更新临时文件无效。";
                return false;
            }

            var scriptPath = Path.Combine(Path.GetDirectoryName(fullPreparedPath)!, "apply-update.ps1");
            var errorPath = Path.Combine(Path.GetDirectoryName(fullPreparedPath)!, "update-error.txt");
            File.WriteAllText(scriptPath, InstallerScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(fullPreparedPath)!
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-ParentPid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("-Source");
            startInfo.ArgumentList.Add(fullPreparedPath);
            startInfo.ArgumentList.Add("-Target");
            startInfo.ArgumentList.Add(_applicationPath);
            startInfo.ArgumentList.Add("-ErrorPath");
            startInfo.ArgumentList.Add(errorPath);
            startInfo.ArgumentList.Add("-ScriptPath");
            startInfo.ArgumentList.Add(scriptPath);
            if (Process.Start(startInfo) is null)
            {
                message = "无法启动更新安装器。";
                return false;
            }
            message = "更新已准备，启动器将关闭并重启。";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            message = "无法启动更新安装器，请检查启动器目录权限。";
            return false;
        }
    }

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        return timeout;
    }

    private LauncherUpdateCheckResult Failure(string message, Version? latestVersion = null) =>
        new(
            Succeeded: false,
            UpdateAvailable: false,
            CurrentVersion: _currentVersion,
            LatestVersion: latestVersion,
            Message: message);

    private static LauncherUpdateDownloadResult DownloadFailure(string message) =>
        new(Succeeded: false, Message: message);

    private static bool TryCreateRelease(GitHubReleaseDto dto, out LauncherReleaseInfo release)
    {
        release = null!;
        if (string.IsNullOrWhiteSpace(dto.TagName)
            || !Version.TryParse(dto.TagName.Trim().TrimStart('v', 'V'), out var version)
            || !Uri.TryCreate(dto.HtmlUrl, UriKind.Absolute, out var htmlUri))
            return false;

        var assets = (dto.Assets ?? [])
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name)
                            && Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out _))
            .Select(asset => new LauncherReleaseAsset(
                asset.Name!,
                new Uri(asset.BrowserDownloadUrl!, UriKind.Absolute),
                asset.Size,
                asset.Digest))
            .ToArray();
        DateTimeOffset? publishedAt = null;
        if (DateTimeOffset.TryParse(dto.PublishedAt, out var parsedPublishedAt))
            publishedAt = parsedPublishedAt;

        release = new LauncherReleaseInfo(
            dto.TagName,
            version,
            string.IsNullOrWhiteSpace(dto.Name) ? dto.TagName : dto.Name,
            htmlUri,
            publishedAt,
            dto.Prerelease,
            assets);
        return true;
    }

    private static bool TryParseSha256(string? digest, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(digest))
            return false;
        var value = digest.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            value = value["sha256:".Length..];
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            return false;
        normalized = value;
        return true;
    }

    private static bool IsAllowedDownloadUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsWithin(string path, string parent)
    {
        var normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static Version ReadCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? typeof(LauncherUpdateService).Assembly.GetName().Version
        ?? new Version(0, 0, 0);

    private static string DescribeStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "GitHub 更新服务拒绝访问。",
        HttpStatusCode.Forbidden or (HttpStatusCode)429 => "GitHub API 请求受限，请稍后重试。",
        HttpStatusCode.NotFound => "GitHub 仓库或 Release 不存在。",
        _ when (int)statusCode >= 500 => "GitHub 更新服务暂时不可用。",
        _ => $"GitHub 更新服务返回 HTTP {(int)statusCode}。"
    };

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto>? Assets { get; set; }
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
