using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Theme;
using System.Net;
using System.Security.Cryptography;

static class Program
{
    private static int _failures;

    static async Task<int> Main()
    {
        Run("font scale clamps to supported range", () =>
        {
            Assert.Equal(80, UiMetrics.ClampFontScale(10));
            Assert.Equal(140, UiMetrics.ClampFontScale(200));
            Assert.Equal(110, UiMetrics.ClampFontScale(110));
        });
        Run("navigation collapses below threshold", () =>
        {
            Assert.True(UiMetrics.ShouldCollapseNavigation(960));
            Assert.False(UiMetrics.ShouldCollapseNavigation(1120));
        });
        Run("navigation width uses compact or expanded size", () =>
        {
            Assert.Equal(78, UiMetrics.NavigationWidth(true));
            Assert.Equal(224, UiMetrics.NavigationWidth(false));
        });
        Run("font resolver follows available candidate order", () =>
        {
            Assert.Equal(
                "Microsoft YaHei",
                UiFontResolver.ChooseAvailableFamily(
                    ["Segoe UI", "Microsoft YaHei"],
                    "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Arial"));
            Assert.Equal(
                "Segoe UI",
                UiFontResolver.ChooseAvailableFamily(
                    ["Segoe UI"],
                    "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Arial"));
        });
        Run("safe height respects font scale and minimum", () =>
        {
            Assert.True(UiMetrics.SafeHeight(24, 10, 36, 96) >= 36);
            Assert.True(UiMetrics.SafeHeight(30, 12, 36, 144) > UiMetrics.SafeHeight(30, 12, 36, 96));
        });
        Run("navigation item preserves accessible title", () =>
        {
            var item = NavigationItem.Create("系统级设置", "04");
            Assert.Equal("系统级设置", item.AccessibleName);
            Assert.Equal("04", item.Index);
        });
        Run("ui text truncation preserves short values", () =>
        {
            Assert.Equal(string.Empty, UiText.Truncate(null, 4));
            Assert.Equal("short", UiText.Truncate("short", 8));
            Assert.Equal("abcd…", UiText.Truncate("abcdefgh", 5));
        });

        Run("latest comparison", () =>
            Assert.Equal(UpdateState.Latest, UpdateDecision.Evaluate(0, 0, false)));
        Run("behind means update available", () =>
            Assert.Equal(UpdateState.UpdateAvailable, UpdateDecision.Evaluate(0, 2, false)));
        Run("dirty worktree blocks pull", () =>
            Assert.Equal(UpdateState.DirtyWorktree, UpdateDecision.Evaluate(0, 2, true)));
        Run("upstream has priority", () =>
            Assert.Equal("origin/dev", RemoteResolver.Resolve("origin/dev", "origin/main", "origin/master")));
        Run("local ahead", () =>
            Assert.Equal(UpdateState.LocalAhead, UpdateDecision.Evaluate(2, 0, false)));
        Run("no divergence is latest", () =>
            Assert.Equal(UpdateState.Latest, UpdateDecision.Evaluate(0, 0, false)));
        Run("dirty wins over local ahead", () =>
            Assert.Equal(UpdateState.DirtyWorktree, UpdateDecision.Evaluate(3, 1, true)));
        Run("fallback to main", () =>
            Assert.Equal("origin/main", RemoteResolver.Resolve(null, "origin/main", "origin/master")));
        Run("fallback to master", () =>
            Assert.Equal("origin/master", RemoteResolver.Resolve(null, null, "origin/master")));
        Run("no remote ref", () =>
            Assert.Equal<string?>(null, RemoteResolver.Resolve(null, null, null)));
        Run("github URL validation", () =>
        {
            Assert.True(RemoteResolver.IsGitHubUrl("https://github.com/openai/example.git"));
            Assert.True(RemoteResolver.IsGitHubUrl("git@github.com:openai/example.git"));
            Assert.False(RemoteResolver.IsGitHubUrl("https://gitlab.com/openai/example.git"));
        });

        Run("launcher defaults are machine neutral", () =>
        {
            var settings = LauncherSettings.CreateDefault();
            Assert.True(settings.AutoDetectPaths);
            Assert.True(settings.AutoUpdateEnabled);
            Assert.Equal(24, settings.UpdateCheckIntervalHours);
            Assert.Equal(string.Empty, settings.Paths.DshRoot);
            Assert.Equal(string.Empty, settings.Paths.DshHome);
            Assert.Equal(string.Empty, settings.Paths.PluginRoot);
            Assert.Equal("web", settings.Paths.ProfileName);
            Assert.Equal("git.exe", settings.Paths.GitExecutable);
            Assert.Equal("pnpm.cmd", settings.Paths.PnpmExecutable);
            Assert.Equal(string.Empty, DshPaths.CreateDefault().Root);
            Assert.Equal(string.Empty, DshPaths.CreateDefault().PnpmStore);
            Assert.Equal("Obsidian", settings.Theme.Name);
        });

        await RunAsync("launcher update parses release and verifies asset", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"dsh-launcher-update-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var applicationPath = Path.Combine(root, "dsh++.exe");
            File.WriteAllBytes(applicationPath, [0x01, 0x02, 0x03]);
            var payload = System.Text.Encoding.UTF8.GetBytes("portable dsh++ test payload");
            var digest = Convert.ToHexString(SHA256.HashData(payload));
            var releaseJson = $$"""
                {
                  "tag_name": "v0.2.0",
                  "name": "dsh++ v0.2.0",
                  "html_url": "https://github.com/qichengxiaoqi/dsh-lancher/releases/tag/v0.2.0",
                  "published_at": "2026-08-17T00:00:00Z",
                  "draft": false,
                  "prerelease": false,
                  "assets": [
                    {
                      "name": "dsh++.exe",
                      "browser_download_url": "https://github.com/qichengxiaoqi/dsh-lancher/releases/download/v0.2.0/dsh++.exe",
                      "size": {{payload.Length}},
                      "digest": "sha256:{{digest}}"
                    }
                  ]
                }
                """;
            var handler = new StubHttpHandler(request =>
            {
                if (request.RequestUri?.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal) == true)
                    return JsonResponse(releaseJson);
                if (request.RequestUri?.AbsolutePath.EndsWith("/dsh++.exe", StringComparison.Ordinal) == true)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            try
            {
                using var client = new HttpClient(handler);
                var service = new LauncherUpdateService(
                    client,
                    applicationPath,
                    new Version(0, 1, 0),
                    TimeSpan.FromSeconds(2));
                var check = await service.CheckAsync(CancellationToken.None);
                Assert.True(check.Succeeded);
                Assert.True(check.UpdateAvailable);
                Assert.Equal("v0.2.0", check.Release!.TagName);
                Assert.Equal("dsh++", handler.LastRequest?.Headers.UserAgent.FirstOrDefault()?.Product?.Name);

                var download = await service.DownloadAndPrepareAsync(check.Release, CancellationToken.None);
                Assert.True(download.Succeeded);
                Assert.True(download.DigestVerified);
                Assert.True(download.PreparedPath is not null && File.Exists(download.PreparedPath));

                var mismatch = await service.DownloadAndPrepareAsync(
                    check.Release with
                    {
                        Assets =
                        [check.Release.ExecutableAsset! with { Digest = "sha256:" + new string('0', 64) }]
                    },
                    CancellationToken.None);
                Assert.False(mismatch.Succeeded);
                Assert.Equal<string?>(null, mismatch.PreparedPath);
            }
            finally
            {
                DeleteTree(root);
            }
        });

        await RunAsync("launcher update handles rate limit and timeout", async () =>
        {
            using (var limitedClient = new HttpClient(
                       new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests))))
            {
                var limited = new LauncherUpdateService(
                    limitedClient,
                    Path.Combine(Path.GetTempPath(), "dsh++.exe"),
                    new Version(0, 1, 0),
                    TimeSpan.FromSeconds(2));
                var result = await limited.CheckAsync(CancellationToken.None);
                Assert.False(result.Succeeded);
                Assert.Contains("受限", result.Message);
            }

            using var timeoutClient = new HttpClient(new TimeoutHttpHandler());
            var timeout = new LauncherUpdateService(
                timeoutClient,
                Path.Combine(Path.GetTempPath(), "dsh++.exe"),
                new Version(0, 1, 0),
                TimeSpan.FromMilliseconds(50));
            var timedOut = await timeout.CheckAsync(CancellationToken.None);
            Assert.False(timedOut.Succeeded);
            Assert.Contains("超时", timedOut.Message);
        });

        await RunAsync("launcher update ignores draft and invalid releases", async () =>
        {
            const string draftJson = "{\"tag_name\":\"v9.9.9\",\"draft\":true,\"prerelease\":false}";
            using var draftClient = new HttpClient(new StubHttpHandler(_ => JsonResponse(draftJson)));
            var draftResult = await new LauncherUpdateService(
                    draftClient,
                    Path.Combine(Path.GetTempPath(), "dsh++.exe"),
                    new Version(0, 1, 0),
                    TimeSpan.FromSeconds(2))
                .CheckAsync(CancellationToken.None);
            Assert.True(draftResult.Succeeded);
            Assert.False(draftResult.UpdateAvailable);

            const string invalidJson = "{\"tag_name\":\"latest\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://github.com/qichengxiaoqi/dsh-lancher\"}";
            using var invalidClient = new HttpClient(new StubHttpHandler(_ => JsonResponse(invalidJson)));
            var invalidResult = await new LauncherUpdateService(
                    invalidClient,
                    Path.Combine(Path.GetTempPath(), "dsh++.exe"),
                    new Version(0, 1, 0),
                    TimeSpan.FromSeconds(2))
                .CheckAsync(CancellationToken.None);
            Assert.False(invalidResult.Succeeded);
            Assert.Contains("格式", invalidResult.Message);
        });

        Run("path discovery finds a portable sibling dsh environment", () =>
        {
            var workspace = Path.Combine(Path.GetTempPath(), $"dsh-discovery-{Guid.NewGuid():N}");
            var appBase = Path.Combine(workspace, "dsh++", "publish");
            var dshRoot = Path.Combine(workspace, "Deepseek-dsh");
            var userProfile = Path.Combine(workspace, "user");
            var dshHome = Path.Combine(userProfile, ".dsh");
            var profile = Path.Combine(dshHome, "profiles", "web");
            var pluginRoot = Path.Combine(workspace, "dsp");
            var tools = Path.Combine(workspace, "tools");
            Directory.CreateDirectory(appBase);
            Directory.CreateDirectory(Path.Combine(dshRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(dshRoot, "scripts", "windows"));
            Directory.CreateDirectory(profile);
            Directory.CreateDirectory(pluginRoot);
            Directory.CreateDirectory(tools);
            File.WriteAllText(Path.Combine(dshRoot, "package.json"), "{}\n");
            File.WriteAllText(
                Path.Combine(dshRoot, "scripts", "windows", "DeepSeekHarnessService.ps1"),
                "# test\n");
            File.WriteAllText(Path.Combine(profile, "package.json"), "{}\n");
            var gitPath = Path.Combine(tools, "git.exe");
            var pnpmPath = Path.Combine(tools, "pnpm.cmd");
            var powershellPath = Path.Combine(tools, "pwsh.exe");
            File.WriteAllText(gitPath, string.Empty);
            File.WriteAllText(pnpmPath, string.Empty);
            File.WriteAllText(powershellPath, string.Empty);
            var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PATH"] = tools
            };

            try
            {
                var discovery = new LauncherPathDiscovery(
                    appBase,
                    userProfile,
                    name => environment.TryGetValue(name, out var value) ? value : null);
                var paths = discovery.Discover();

                Assert.Equal(Path.GetFullPath(dshRoot), paths.DshRoot);
                Assert.Equal(Path.GetFullPath(dshHome), paths.DshHome);
                Assert.Equal(Path.GetFullPath(profile), paths.ProfileDirectory);
                Assert.Equal(Path.GetFullPath(pluginRoot), paths.PluginRoot);
                Assert.Equal(
                    Path.Combine(dshRoot, "scripts", "windows", "DeepSeekHarnessService.ps1"),
                    paths.ServiceScript);
                Assert.Equal(Path.GetFullPath(gitPath), paths.GitExecutable);
                Assert.Equal(Path.GetFullPath(pnpmPath), paths.PnpmExecutable);
                Assert.Equal(Path.GetFullPath(powershellPath), paths.PowerShellPath);
            }
            finally
            {
                DeleteTree(workspace);
            }
        });

        await RunAsync("settings store applies discovery but preserves manual override", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"dsh-settings-discovery-{Guid.NewGuid():N}");
            var dshRoot = Path.Combine(root, "Deepseek-dsh");
            var appBase = Path.Combine(root, "dsh++", "publish");
            var userProfile = Path.Combine(root, "user");
            Directory.CreateDirectory(appBase);
            Directory.CreateDirectory(Path.Combine(dshRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(dshRoot, "scripts", "windows"));
            Directory.CreateDirectory(Path.Combine(userProfile, ".dsh", "profiles", "web"));
            File.WriteAllText(Path.Combine(dshRoot, "package.json"), "{}\n");
            File.WriteAllText(
                Path.Combine(dshRoot, "scripts", "windows", "DeepSeekHarnessService.ps1"),
                "# test\n");
            File.WriteAllText(
                Path.Combine(userProfile, ".dsh", "profiles", "web", "package.json"),
                "{}\n");
            var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var discovery = new LauncherPathDiscovery(
                appBase,
                userProfile,
                name => environment.TryGetValue(name, out var value) ? value : null);
            var settingsFile = Path.Combine(root, "settings.json");
            var store = new LauncherSettingsStore(settingsFile, discovery);

            try
            {
                var detected = store.Load();
                Assert.True(detected.AutoDetectPaths);
                Assert.Equal(Path.GetFullPath(dshRoot), detected.Paths.DshRoot);

                var manualRoot = Path.Combine(root, "manual-dsh");
                var manual = detected with
                {
                    AutoDetectPaths = false,
                    AutoUpdateEnabled = false,
                    UpdateCheckIntervalHours = 48,
                    Paths = detected.Paths with { DshRoot = manualRoot }
                };
                await store.SaveAsync(manual, CancellationToken.None);
                var reloaded = new LauncherSettingsStore(settingsFile, discovery).Load();
                Assert.False(reloaded.AutoDetectPaths);
                Assert.False(reloaded.AutoUpdateEnabled);
                Assert.Equal(48, reloaded.UpdateCheckIntervalHours);
                Assert.Equal(manualRoot, reloaded.Paths.DshRoot);
            }
            finally
            {
                DeleteTree(root);
            }
        });

        Run("path validator reports missing required paths", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"dsh-paths-{Guid.NewGuid():N}");
            var paths = LauncherPaths.CreateDefault() with
            {
                DshRoot = root,
                ServiceScript = Path.Combine(root, "service.ps1"),
                ProfileDirectory = Path.Combine(root, "profile")
            };
            var result = new PathValidator().Validate(paths);
            Assert.False(result.IsValid);
            Assert.Contains("DSH 根目录", string.Join(";", result.Errors));
        });

        await RunAsync("credential store preserves unrelated yaml entries", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"dsh-credentials-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var file = Path.Combine(root, ".credentials.yaml");
            File.WriteAllText(file, "OTHER_KEY: keep-me\n# comment\n");
            var store = new DshCredentialStore();
            await store.SetAsync(file, "DEEPSEEK_API_KEY", "test-api-key-value", CancellationToken.None);
            var text = await File.ReadAllTextAsync(file);
            Assert.Contains("OTHER_KEY: keep-me", text);
            Assert.Contains("DEEPSEEK_API_KEY" + ":", text);
            Assert.Equal("••••alue", store.ReadStatus(file, "DEEPSEEK_API_KEY").MaskedValue);
            await store.ClearAsync(file, "DEEPSEEK_API_KEY", CancellationToken.None);
            Assert.False((await File.ReadAllTextAsync(file)).Contains("DEEPSEEK_API_KEY", StringComparison.Ordinal));
            DeleteTree(root);
        });

        await RunAsync("deepseek api parses models and balance", async () =>
        {
            var handler = new StubHttpHandler(request => request.RequestUri?.AbsolutePath switch
            {
                "/models" => JsonResponse("{\"object\":\"list\",\"data\":[{\"id\":\"deepseek-v4-flash\",\"object\":\"model\",\"owned_by\":\"deepseek\"}]}") ,
                "/user/balance" => JsonResponse("{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"12.5\",\"granted_balance\":\"2\",\"topped_up_balance\":\"10.5\"}]}") ,
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
            var client = new DeepSeekApiClient(handler, "https://api.deepseek.com");
            var models = await client.GetModelsAsync("test-api-key", CancellationToken.None);
            var balance = await client.GetBalanceAsync("test-api-key", CancellationToken.None);
            Assert.Equal("deepseek-v4-flash", models[0].Id);
            Assert.Equal(12.5m, balance.Balances[0].TotalBalance);
            Assert.True(handler.LastRequest?.Headers.Authorization?.Scheme == "Bearer");
        });

        await RunAsync("deepseek connection reports unauthorized without throwing", async () =>
        {
            var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var result = await new DeepSeekApiClient(handler, "https://api.deepseek.com")
                .TestConnectionAsync("invalid-test-key", CancellationToken.None);
            Assert.False(result.Success);
            Assert.Equal(401, result.StatusCode);
        });

        await RunAsync("system instruction scanner finds and deduplicates instruction files", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"dsh-instructions-{Guid.NewGuid():N}");
            var home = Path.Combine(root, "home");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(home);
            var projectAgents = Path.Combine(root, "AGENTS.md");
            var projectClaude = Path.Combine(root, "CLAUDE.md");
            File.WriteAllText(projectAgents, "same instructions");
            File.WriteAllText(projectClaude, "same instructions");
            File.WriteAllText(Path.Combine(home, "AGENTS.md"), "global instructions");
            var paths = LauncherPaths.CreateDefault() with
            {
                DshRoot = root,
                DshHome = home,
                ProfileDirectory = Path.Combine(home, "profiles", "web")
            };
            var files = await new SystemInstructionScanner(paths).ScanAsync(CancellationToken.None);
            Assert.True(files.Count >= 3);
            Assert.True(files.Count(file => file.IsDuplicate) >= 2);
            Assert.True(files.Any(file => file.Kind == SystemInstructionKind.MarkdownInstruction
                                          && file.Scope == "global"));
            DeleteTree(root);
        });

        await RunAsync("system instruction scanner returns canceled task", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var task = new SystemInstructionScanner(LauncherPaths.CreateDefault())
                .ScanAsync(cancellation.Token);
            Assert.True(task.IsCanceled);
            await Task.CompletedTask;
        });

        await RunAsync("system instruction scanner skips dependency trees", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"dsh-scan-bounds-{Guid.NewGuid():N}");
            var home = Path.Combine(root, "home");
            var dependency = Path.Combine(root, "node_modules", "third-party");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(dependency);
            File.WriteAllText(Path.Combine(root, "AGENTS.md"), "project instructions");
            File.WriteAllText(Path.Combine(dependency, "AGENTS.md"), "dependency instructions");
            var paths = LauncherPaths.CreateDefault() with
            {
                DshRoot = root,
                DshHome = home,
                ProfileDirectory = Path.Combine(home, "profiles", "web")
            };

            var files = await new SystemInstructionScanner(paths).ScanAsync(CancellationToken.None);

            Assert.True(files.Any(file => file.Path.Equals(Path.Combine(root, "AGENTS.md"), StringComparison.OrdinalIgnoreCase)));
            Assert.False(files.Any(file => file.Path.Contains("node_modules", StringComparison.OrdinalIgnoreCase)));
            DeleteTree(root);
        });

        await RunAsync("system instruction scanner caches and clears results", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"dsh-scan-cache-{Guid.NewGuid():N}");
            var home = Path.Combine(root, "home");
            Directory.CreateDirectory(home);
            var instruction = Path.Combine(root, "AGENTS.md");
            File.WriteAllText(instruction, "cached instructions");
            var paths = LauncherPaths.CreateDefault() with
            {
                DshRoot = root,
                DshHome = home,
                ProfileDirectory = Path.Combine(home, "profiles", "web")
            };
            var scanner = new SystemInstructionScanner(paths);
            var first = await scanner.ScanAsync(CancellationToken.None);
            File.Delete(instruction);
            var cached = await scanner.ScanAsync(CancellationToken.None);
            Assert.True(cached.Any(file => file.Path.Equals(instruction, StringComparison.OrdinalIgnoreCase)));
            scanner.ClearCache();
            var refreshed = await scanner.ScanAsync(CancellationToken.None);
            Assert.False(refreshed.Any(file => file.Path.Equals(instruction, StringComparison.OrdinalIgnoreCase)));
            Assert.True(first.Count > 0);
            DeleteTree(root);
        });

        await RunAsync("plugin patch keeps full config and unrelated entries", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"dsh-plugin-patch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var patch = Path.Combine(root, "cordis.patch.yml");
            File.WriteAllText(patch, "- id: unrelated\n  name: other\n");
            var result = await new ProfilePatchService().SetPluginEnabledAsync(
                patch,
                "caveman",
                "id: caveman\nname: dsh-caveman\ninject: [fs]\n",
                enabled: false,
                CancellationToken.None);
            var text = await File.ReadAllTextAsync(patch);
            Assert.True(result.Succeeded);
            Assert.True(result.BackupPath is not null && File.Exists(result.BackupPath));
            Assert.Contains("id: unrelated", text);
            Assert.Contains("name: dsh-caveman", text);
            Assert.Contains("inject:", text);
            Assert.Contains("disabled: true", text);
            DeleteTree(root);
        });

        await RunAsync("process captures output", async () =>
        {
            var result = await new ProcessRunner().RunAsync(
                "cmd.exe", ["/d", "/c", "echo hello"],
                Environment.CurrentDirectory, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("hello", result.StandardOutput);
            Assert.True(result.Succeeded);
        });

        await RunAsync("service controller uses fixed script arguments", async () =>
        {
            var paths = DshPaths.CreateDefault();
            var runner = new RecordingRunner();
            await new DshServiceController(runner, paths)
                .StartAsync(CancellationToken.None);

            Assert.Equal(paths.PowerShellPath, runner.Last.FileName);
            Assert.SequenceEqual(
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                    "-File", paths.ServiceScript, "-Action", "Start"],
                runner.Last.Arguments);
        });

        Run("tcp unavailable means stopped", () =>
            Assert.Equal(ServiceState.Stopped, ServiceStatusMapper.TcpUnavailable().State));
        Run("http 500 means running", () =>
            Assert.Equal(ServiceState.Running, ServiceStatusMapper.FromHttpStatus(HttpStatusCode.InternalServerError).State));
        Run("http failure means start failed", () =>
            Assert.Equal(ServiceState.StartFailed, ServiceStatusMapper.HttpFailure("connection refused").State));

        await RunAsync("local git snapshot reads package version", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"dsh-plus-plus-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "package.json"), "{\"version\":\"0.1.0-test\"}");
                var runner = new ProcessRunner();
                await RunGit(runner, root, ["init"]);
                await RunGit(runner, root, ["-c", "user.name=dsh-test", "-c", "user.email=dsh-test@example.invalid", "add", "package.json"]);
                await RunGit(runner, root, ["-c", "user.name=dsh-test", "-c", "user.email=dsh-test@example.invalid", "commit", "-m", "initial"]);

                var paths = PathsFor(root);
                var snapshot = await new GitRepositoryService(paths, runner)
                    .ReadLocalSnapshotAsync(CancellationToken.None);
                Assert.Equal("0.1.0-test", snapshot.LocalPackageVersion);
                Assert.True(snapshot.HeadSha.Length >= 7);
                Assert.False(snapshot.IsDirty);
            }
            finally
            {
                DeleteTree(root);
            }
        });

        await RunAsync("project commands preserve explicit pnpm store", async () =>
        {
            var paths = PathsFor(Environment.CurrentDirectory);
            var runner = new RecordingRunner();
            var project = new ProjectCommandService(paths, runner);
            await project.InstallDependenciesAsync(CancellationToken.None);
            await project.BuildAsync(CancellationToken.None);

            Assert.SequenceEqual(
                ["install", "--store-dir", paths.PnpmStore],
                runner.Calls[0].Arguments);
            Assert.SequenceEqual(["run", "build"], runner.Calls[1].Arguments);
            Assert.Equal(paths.Root, runner.Calls[0].WorkingDirectory);
            Assert.Equal(paths.Root, runner.Calls[1].WorkingDirectory);
        });

        await RunAsync("project commands use pnpm default store when unset", async () =>
        {
            var paths = PathsFor(Environment.CurrentDirectory) with { PnpmStore = string.Empty };
            var runner = new RecordingRunner();
            await new ProjectCommandService(paths, runner)
                .InstallDependenciesAsync(CancellationToken.None);

            Assert.SequenceEqual(["install"], runner.Last.Arguments);
            Assert.Equal(paths.Root, runner.Last.WorkingDirectory);
        });

        await RunAsync("git pull names resolved remote ref", async () =>
        {
            var paths = PathsFor(Environment.CurrentDirectory);
            var runner = new RecordingRunner();
            await new GitRepositoryService(paths, runner)
                .PullFastForwardOnlyAsync("origin/main", CancellationToken.None);
            Assert.SequenceEqual(
                ["pull", "--ff-only", "origin", "main"],
                runner.Last.Arguments);
        });

        await RunAsync("dirty worktree blocks pull before stop", async () =>
        {
            var calls = new List<string>();
            var result = await new UpdateCoordinator(
                    new RecordingGit(UpdateState.DirtyWorktree, calls),
                    new RecordingProject(false, calls),
                    new RecordingService(calls))
                .PullAsync(CancellationToken.None);
            Assert.Equal(UpdateState.DirtyWorktree, result.State);
            Assert.SequenceEqual(["check"], calls);
        });

        await RunAsync("successful pull order", async () =>
        {
            var calls = new List<string>();
            var git = new RecordingGit(UpdateState.UpdateAvailable, calls);
            var result = await new UpdateCoordinator(
                    git,
                    new RecordingProject(false, calls),
                    new RecordingService(calls))
                .PullAsync(CancellationToken.None);
            Assert.True(result.Succeeded);
            Assert.SequenceEqual(["check", "stop", "pull", "install", "build", "start"], calls);
            Assert.Equal("origin/main", git.LastPullRef);
        });

        await RunAsync("build failure does not restart or rollback", async () =>
        {
            var calls = new List<string>();
            var result = await new UpdateCoordinator(
                    new RecordingGit(UpdateState.UpdateAvailable, calls),
                    new RecordingProject(true, calls),
                    new RecordingService(calls))
                .PullAsync(CancellationToken.None);
            Assert.False(result.Succeeded);
            Assert.SequenceEqual(["check", "stop", "pull", "install", "build"], calls);
        });

        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {ex}");
        }
    }

    private static async Task RunAsync(string name, Func<Task> test)
    {
        try
        {
            await test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {ex}");
        }
    }

    private static class Assert
    {
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"expected {expected}, got {actual}");
        }

        public static void True(bool value)
        {
            if (!value)
                throw new InvalidOperationException("expected true");
        }

        public static void False(bool value)
        {
            if (value)
                throw new InvalidOperationException("expected false");
        }

        public static void Contains(string expectedFragment, string actual)
        {
            if (!actual.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"expected '{actual}' to contain '{expectedFragment}'");
        }

        public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException(
                    $"expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
        }
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public Invocation Last { get; private set; } = null!;
        public List<Invocation> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Last = new Invocation(fileName, arguments.ToArray(), workingDirectory, timeout);
            Calls.Add(Last);
            return Task.FromResult(new ProcessResult(fileName, arguments, 0, string.Empty, string.Empty));
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class TimeoutHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new TaskCanceledException("simulated timeout");
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed record Invocation(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        TimeSpan Timeout);

    private static DshPaths PathsFor(string root) => new(
        root,
        Path.Combine(root, "scripts", "windows", "DeepSeekHarnessService.ps1"),
        "http://127.0.0.1:3080",
        3080,
        Path.Combine(root, ".pnpm-store"),
        "powershell.exe",
        "git.exe",
        "pnpm.cmd");

    private static async Task RunGit(
        IProcessRunner runner,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var result = await runner.RunAsync(
            "git.exe", arguments, workingDirectory, TimeSpan.FromSeconds(15), CancellationToken.None);
        if (!result.Succeeded)
            throw new InvalidOperationException($"git failed: {result.CombinedOutput}");
    }

    private static void DeleteTree(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
            File.SetAttributes(directory, FileAttributes.Normal);

        Directory.Delete(root, recursive: true);
    }

    private static ProcessResult Success(string name) =>
        new(name, [], 0, string.Empty, string.Empty);

    private static ProcessResult Failure(string name) =>
        new(name, [], 1, string.Empty, "failed");

    private sealed class RecordingGit : IGitRepositoryService
    {
        private readonly UpdateCheckResult _check;
        private readonly List<string> _calls;
        public string? LastPullRef { get; private set; }

        public RecordingGit(UpdateState state, List<string> calls)
        {
            var snapshot = new RepositorySnapshot(
                "root", "main", "head", "head", "0.1.0", "0.2.0",
                "https://github.com/example/dsh.git", "origin/main", "origin/main",
                0, 1, state == UpdateState.DirtyWorktree);
            _check = new UpdateCheckResult(state, state.ToString(), snapshot);
            _calls = calls;
        }

        public Task<RepositorySnapshot> ReadLocalSnapshotAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            _calls.Add("check");
            return Task.FromResult(_check);
        }

        public Task<ProcessResult> PullFastForwardOnlyAsync(
            string remoteRef,
            CancellationToken cancellationToken)
        {
            _calls.Add("pull");
            LastPullRef = remoteRef;
            return Task.FromResult(Success("git"));
        }
    }

    private sealed class RecordingProject : IProjectCommandService
    {
        private readonly bool _buildFails;
        private readonly List<string> _calls;

        public RecordingProject(bool buildFails, List<string> calls)
        {
            _buildFails = buildFails;
            _calls = calls;
        }

        public Task<ProcessResult> InstallDependenciesAsync(CancellationToken cancellationToken)
        {
            _calls.Add("install");
            return Task.FromResult(Success("pnpm"));
        }

        public Task<ProcessResult> BuildAsync(CancellationToken cancellationToken)
        {
            _calls.Add("build");
            return Task.FromResult(_buildFails ? Failure("pnpm") : Success("pnpm"));
        }
    }

    private sealed class RecordingService : IDshServiceController
    {
        private readonly List<string> _calls;

        public RecordingService(List<string> calls) => _calls = calls;

        public Task<ProcessResult> StartAsync(CancellationToken cancellationToken)
        {
            _calls.Add("start");
            return Task.FromResult(Success("powershell"));
        }

        public Task<ProcessResult> StopAsync(CancellationToken cancellationToken)
        {
            _calls.Add("stop");
            return Task.FromResult(Success("powershell"));
        }

        public Task<ProcessResult> RestartAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
