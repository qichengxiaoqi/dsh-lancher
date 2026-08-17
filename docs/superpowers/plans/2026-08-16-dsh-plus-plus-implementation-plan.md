# dsh++ Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在独立的启动器项目目录中实现一个 .NET 9 WinForms 单文件 Windows 启动器，用于安全地管理自动探测到的 DeepSeek Harness 服务，并发布 `publish\dsh++.exe`。

**Architecture:** 将无界面的业务逻辑放入 `DshPlusPlus.Core`，包括自动探测路径、进程执行、服务脚本控制、服务探测、Git 状态和更新编排；WinForms 项目只负责 B 布局、状态展示、确认框和日志。更新检查只 fetch，更新拉取必须二次检查干净工作区并使用 `git pull --ff-only`，依赖安装和构建失败不回滚、不 push、不重置用户文件。

**Tech Stack:** .NET 9、C#、WinForms、`net9.0` 核心库、`net9.0-windows` GUI、Windows PowerShell、Git、pnpm；测试使用无第三方依赖的 .NET 控制台测试运行器，避免为启动器引入额外运行时包。

---

## 文件结构与职责

目标目录最终包含：

```text
<launcher-root>\
├─ src\
│  ├─ DshPlusPlus.Core\
│  │  ├─ DshPlusPlus.Core.csproj
│  │  ├─ Models\
│  │  │  ├─ DshPaths.cs
│  │  │  ├─ ProcessResult.cs
│  │  │  ├─ RepositorySnapshot.cs
│  │  │  ├─ ServiceState.cs
│  │  │  ├─ ServiceProbeResult.cs
│  │  │  ├─ UpdateCheckResult.cs
│  │  │  └─ UpdateState.cs
│  │  └─ Services\
│  │     ├─ ProcessRunner.cs
│  │     ├─ DshServiceController.cs
│  │     ├─ ServiceStatusProbe.cs
│  │     ├─ GitRepositoryService.cs
│  │     ├─ ProjectCommandService.cs
│  │     └─ UpdateCoordinator.cs
│  └─ DshPlusPlus\
│     ├─ DshPlusPlus.csproj
│     ├─ Program.cs
│     └─ MainForm.cs
├─ tests\
│  └─ DshPlusPlus.Core.Tests\
│     ├─ DshPlusPlus.Core.Tests.csproj
│     └─ Program.cs
├─ docs\
│  ├─ 2026-08-16-dsh-plus-plus-design.md
│  └─ superpowers\plans\2026-08-16-dsh-plus-plus-implementation-plan.md
└─ publish\
   └─ dsh++.exe
```

`<dsh-root>` 只作为被管理的现有项目读取和调用；不在其中添加启动器代码，不修改现有 BAT、PowerShell、`.dsh`、`<plugin-root>` 或插件文件。

### Task 1: 创建解决方案和可执行测试入口

**Files:**
- Create: `<launcher-root>\DshPlusPlus.sln`
- Create: `<launcher-root>\src\DshPlusPlus.Core\DshPlusPlus.Core.csproj`
- Create: `<launcher-root>\src\DshPlusPlus\DshPlusPlus.csproj`
- Create: `<launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj`
- Create: `<launcher-root>\tests\DshPlusPlus.Core.Tests\Program.cs`

- [ ] **Step 1: 创建项目文件。**

  核心库使用 `net9.0`；GUI 使用 `net9.0-windows`、`UseWindowsForms=true`、`OutputType=WinExe`；测试项目为 `net9.0` 控制台并引用核心库。三个项目均启用 nullable 和 implicit usings；不要添加 NuGet 包。

- [ ] **Step 2: 写入最小失败测试入口。**

  测试入口先声明将由后续实现提供的行为，并使用最小断言工具：

  ```csharp
  using DshPlusPlus.Core.Models;
  using DshPlusPlus.Core.Services;

  static class Program
  {
      private static int _failures;

      static int Main()
      {
          Run("latest comparison", () =>
              Assert.Equal(UpdateState.Latest, UpdateDecision.Evaluate(0, 0, false)));
          Run("behind means update available", () =>
              Assert.Equal(UpdateState.UpdateAvailable, UpdateDecision.Evaluate(0, 2, false)));
          Run("dirty worktree blocks pull", () =>
              Assert.Equal(UpdateState.DirtyWorktree, UpdateDecision.Evaluate(0, 2, true)));
          Run("upstream has priority", () =>
              Assert.Equal("origin/dev", RemoteResolver.Resolve("origin/dev", "origin/main", "origin/master")));
          return _failures == 0 ? 0 : 1;
      }

      private static void Run(string name, Action test)
      {
          try { test(); Console.WriteLine($"PASS {name}"); }
          catch (Exception ex) { _failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
      }

      private static class Assert
      {
          public static void Equal<T>(T expected, T actual)
          {
              if (!EqualityComparer<T>.Default.Equals(expected, actual))
                  throw new InvalidOperationException($"expected {expected}, got {actual}");
          }
      }
  }
  ```

- [ ] **Step 3: 运行测试，确认它因功能缺失而失败。**

  Run: `dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj`

  Expected: 编译失败，原因是 `UpdateDecision` 和 `RemoteResolver` 尚不存在；不是因为项目文件格式错误或路径错误。

### Task 2: 实现可测试的领域模型和更新判定

**Files:**
- Create: `<launcher-root>\src\DshPlusPlus.Core\Models\DshPaths.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Models\ProcessResult.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Models\RepositorySnapshot.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Models\ServiceState.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Models\ServiceProbeResult.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Models\UpdateCheckResult.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Models\UpdateState.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Services\UpdateDecision.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Services\RemoteResolver.cs`
- Modify: `<launcher-root>\tests\DshPlusPlus.Core.Tests\Program.cs`

- [ ] **Step 1: 扩充失败测试。**

  增加以下测试，先运行确认新增行为失败：

  ```csharp
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
  ```

- [ ] **Step 2: 实现最小领域类型。**

  `DshPaths.CreateDefault()` 固定返回：

  ```csharp
  new DshPaths(
      Root: @"<dsh-root>",
      ServiceScript: @"<dsh-root>\scripts\windows\DeepSeekHarnessService.ps1",
      WebUrl: "http://127.0.0.1:3080",
      Port: 3080,
      PnpmStore: @"<plugin-root>\.pnpm-store",
      PowerShellPath: @"powershell.exe",
      GitExecutable: "git.exe",
      PnpmExecutable: "pnpm.cmd");
  ```

  `ProcessResult` 保存文件名、参数、标准输出、标准错误、退出码、超时和取消标志，并提供 `Succeeded`；`RepositorySnapshot` 保存分支、HEAD、短 SHA、本地/远程 package 版本、remote、upstream、ahead、behind、dirty 和错误信息；枚举与结果对象只保存状态和可展示消息，不保存 token、对话正文或完整环境变量。

- [ ] **Step 3: 实现纯函数并运行测试。**

  `UpdateDecision.Evaluate` 的优先级必须是：`dirty=true` 返回 `DirtyWorktree`；`behind>0` 返回 `UpdateAvailable`；`ahead>0` 返回 `LocalAhead`；否则返回 `Latest`。`RemoteResolver.Resolve` 按 upstream、`origin/main`、`origin/master` 顺序返回第一个非空值；GitHub 校验只接受 `https://github.com/owner/repo[.git]` 和 `git@github.com:owner/repo[.git]`。

  Run: `dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj`

  Expected: 所有领域测试输出 `PASS`，退出码为 0。

### Task 3: 实现进程执行器和现有服务脚本控制器

**Files:**
- Create: `<launcher-root>\src\DshPlusPlus.Core\Services\ProcessRunner.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Services\DshServiceController.cs`
- Modify: `<launcher-root>\tests\DshPlusPlus.Core.Tests\Program.cs`

- [ ] **Step 1: 写进程执行器失败测试。**

  用真实 Windows `cmd.exe` 验证参数数组和 stdout/stderr 捕获，不使用 mock：

  ```csharp
  RunAsync("process captures output", async () =>
  {
      var result = await new ProcessRunner().RunAsync(
          "cmd.exe", ["/d", "/c", "echo hello"],
          Environment.CurrentDirectory, TimeSpan.FromSeconds(5), CancellationToken.None);
      Assert.Equal(0, result.ExitCode);
      Assert.Contains("hello", result.StandardOutput);
      Assert.True(result.Succeeded);
  });
  ```

  先运行测试，确认缺少 `ProcessRunner` 时失败。

- [ ] **Step 2: 实现 `ProcessRunner`。**

  使用 `ProcessStartInfo.ArgumentList`，`UseShellExecute=false`、重定向 stdout/stderr、`CreateNoWindow=true`；同时读取两条输出流；超时或取消时只结束本次启动的外部进程并返回相应标志，不执行 `taskkill /IM node.exe`，不杀 DSH 进程。所有操作都接受 `CancellationToken`，保留退出码和截断后的错误文本供日志显示。

- [ ] **Step 3: 写服务控制器失败测试并实现。**

  控制器使用 `PowerShell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File <固定脚本> -Action <Start|Stop|Restart>` 的参数数组；`Start` 超时 90 秒，`Stop` 45 秒，`Restart` 120 秒。测试使用注入的记录型进程执行器断言 `-Action` 值和脚本路径来自 `DshPaths`，不允许命令字符串拼接。

  `DshServiceController` 只调用 `<dsh-root>\scripts\windows\DeepSeekHarnessService.ps1`，不复制或实现进程树结束逻辑。

- [ ] **Step 4: 运行核心测试。**

  Run: `dotnet run --project .\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj`

  Expected: 进程捕获、参数和服务脚本调用测试全部通过。

### Task 4: 实现服务状态探测

**Files:**
- Create: `<launcher-root>\src\DshPlusPlus.Core\Services\ServiceStatusProbe.cs`
- Modify: `<launcher-root>\tests\DshPlusPlus.Core.Tests\Program.cs`

- [ ] **Step 1: 写探测判定测试。**

  增加纯映射测试：无 TCP 监听映射为 `Stopped`；TCP 可连且 HTTP 返回任意 2xx–5xx 映射为 `Running`；TCP 可连但 HTTP 请求失败映射为 `StartFailed`；探测器自身异常映射为 `Unknown`。先运行确认判定入口缺失。

- [ ] **Step 2: 实现探测器。**

  `ServiceStatusProbe.ProbeAsync` 先用 loopback TCP 连接 `127.0.0.1:3080`，连接失败返回 `Stopped`；连接成功后用注入的 `HttpClient` 请求固定 `WebUrl`，状态码 200–599 返回 `Running`，HTTP 异常返回 `StartFailed`。TCP/HTTP 探测分别设置短超时，不阻塞 UI；结果包含 `ServiceState` 和不含敏感信息的简短说明。

- [ ] **Step 3: 运行测试并编译核心库。**

  Run: `dotnet run --project .\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj`

  Expected: 所有测试通过。

### Task 5: 实现 Git 仓库读取、fetch-only 检查和 pnpm 命令服务

**Files:**
- Modify: `<launcher-root>\src\DshPlusPlus.Core\Models\RepositorySnapshot.cs`
- Modify: `<launcher-root>\src\DshPlusPlus.Core\Models\UpdateCheckResult.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Services\GitRepositoryService.cs`
- Create: `<launcher-root>\src\DshPlusPlus.Core\Services\ProjectCommandService.cs`
- Modify: `<launcher-root>\tests\DshPlusPlus.Core.Tests\Program.cs`

- [ ] **Step 1: 写临时 Git 仓库测试。**

  测试在 `%TEMP%` 创建临时 Git 仓库，使用 `git -c user.name=dsh-test -c user.email=dsh-test@example.invalid commit` 写入两个提交，再用真实 `git.exe` 验证本地 HEAD、短 SHA、`package.json` 版本和 dirty 状态读取。测试结束删除自己创建的临时目录；不触碰 `<dsh-root>`。

  增加行为测试：

  ```csharp
  RunAsync("local git snapshot reads package version", async () =>
  {
      var snapshot = await GitRepositoryService.ReadLocalSnapshotAsync(tempRoot, new ProcessRunner(), CancellationToken.None);
      Assert.Equal("0.1.0-test", snapshot.LocalPackageVersion);
      Assert.True(snapshot.HeadSha.Length >= 7);
      Assert.False(snapshot.IsDirty);
  });
  ```

  先运行确认服务类缺失而失败。

- [ ] **Step 2: 实现安全的 Git 命令封装。**

  所有 Git 调用使用固定 `git.exe` 和 `ArgumentList`，工作目录固定为 `DshPaths.Root`，每个命令有独立超时。读取：`git status --porcelain=v1 --untracked-files=all`、`git branch --show-current`、`git rev-parse HEAD`、`git rev-parse --short HEAD`、`git remote get-url origin`、当前 upstream 和本地 `package.json`；远程版本使用 `git show <remoteRef>:package.json` 解析。

  `CheckAsync` 顺序固定为：验证 root/.git/package.json；读取 remote 并校验 GitHub；读取工作区和当前分支；执行 `git fetch origin --prune`；按 upstream、`origin/main`、`origin/master` 解析远程 ref；用 `git rev-list --left-right --count HEAD...<remoteRef>` 计算 ahead/behind；返回 `Latest`、`UpdateAvailable`、`LocalAhead`、`DirtyWorktree`、`CannotConnect`、`NoUpstream`、`InvalidRemote` 或 `Error`。fetch 失败只显示错误，不 pull，不修改工作树。

- [ ] **Step 3: 写 Git 安全边界测试并运行。**

  测试断言 dirty 工作区仍允许检查但 `CanPull=false`；无 upstream 时 `CanPull=false`；`git pull --ff-only` 只能由显式更新方法调用。所有 test 输出必须确认不存在 `git push`、`git reset --hard`、`git checkout --` 字符串。

- [ ] **Step 4: 实现 `ProjectCommandService`。**

  解析 pnpm 可执行文件：优先使用环境变量或 PATH 中的 `pnpm.cmd`，不接受界面输入的任意命令行。更新依赖调用 `pnpm.cmd install`，仅在配置 store 时追加 `--store-dir <plugin-root>\.pnpm-store`，构建调用 `pnpm.cmd run build`，工作目录为自动探测到的 `<dsh-root>`，分别设置超时并返回 `ProcessResult`。

- [ ] **Step 5: 运行核心测试和构建。**

  Run: `dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj`

  Expected: Git 读取、安全状态和 pnpm 参数测试通过；不得修改 `<dsh-root>` 工作树。

### Task 6: 实现更新编排并覆盖失败顺序

**Files:**
- Create: `<launcher-root>\src\DshPlusPlus.Core\Services\UpdateCoordinator.cs`
- Modify: `<launcher-root>\tests\DshPlusPlus.Core.Tests\Program.cs`

- [ ] **Step 1: 写使用记录型依赖的失败测试。**

  外部 Git/PowerShell/pnpm 有副作用，因此测试用小型记录型接口实现验证编排顺序：

  ```csharp
  RunAsync("dirty worktree blocks pull before stop", async () =>
  {
      var calls = new List<string>();
      var result = await new UpdateCoordinator(
          new RecordingGit(UpdateState.DirtyWorktree),
          new RecordingProject(false),
          new RecordingService(calls), calls)
          .PullAsync(CancellationToken.None);
      Assert.Equal(UpdateState.DirtyWorktree, result.State);
      Assert.Equal(0, calls.Count);
  });

  RunAsync("successful pull order", async () =>
  {
      var calls = new List<string>();
      var result = await new UpdateCoordinator(
          new RecordingGit(UpdateState.UpdateAvailable),
          new RecordingProject(false),
          new RecordingService(calls), calls)
          .PullAsync(CancellationToken.None);
      Assert.True(result.Succeeded);
      Assert.SequenceEqual(["check", "stop", "pull", "install", "build", "start"], calls);
  });

  RunAsync("build failure does not restart or rollback", async () =>
  {
      var calls = new List<string>();
      var result = await new UpdateCoordinator(
          new RecordingGit(UpdateState.UpdateAvailable),
          new RecordingProject(true),
          new RecordingService(calls), calls)
          .PullAsync(CancellationToken.None);
      Assert.False(result.Succeeded);
      Assert.SequenceEqual(["check", "stop", "pull", "install", "build"], calls);
  });
  ```

  先运行确认 `UpdateCoordinator` 缺失或行为不正确而失败。

- [ ] **Step 2: 实现更新编排。**

  `PullAsync` 首先重新执行 `CheckAsync`（包括 fetch）；只有状态为 `UpdateAvailable`、工作区干净且存在有效远程 ref 时才继续。顺序固定为：服务脚本 Stop、使用已解析远程 ref 的 `git pull --ff-only <remote> <branch>`（例如 `git pull --ff-only origin main`）、pnpm install、pnpm build、服务脚本 Start。任一阶段失败立即返回阶段和错误，不执行 reset、checkout、push、回滚或覆盖用户文件；pull/build 失败后保持服务停止，允许用户再次点启动。

- [ ] **Step 3: 运行核心测试。**

  Run: `dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj`

  Expected: pull 禁止条件、成功顺序、失败停止和无回滚测试全部通过。

### Task 7: 实现 B 布局 WinForms 界面

**Files:**
- Create: `<launcher-root>\src\DshPlusPlus\Program.cs`
- Create: `<launcher-root>\src\DshPlusPlus\MainForm.cs`

- [ ] **Step 1: 写 UI 启动/依赖编译测试。**

  先运行 `dotnet build <launcher-root>\src\DshPlusPlus\DshPlusPlus.csproj`，确认 `Program`/`MainForm` 尚不存在而失败；此处不需要为 WinForms 编写脆弱的像素级 UI 测试。

- [ ] **Step 2: 实现程序入口和固定窗口。**

  `Program.Main` 使用 `[STAThread]`、`ApplicationConfiguration.Initialize()` 和 `Application.Run(new MainForm(...))`。窗口标题为 `dsh++`，客户区约 `720x460`，不可最大化，使用系统字体和原生 WinForms 控件，不引入第三方 UI 框架。

- [ ] **Step 3: 实现 B 布局。**

  左栏显示标题、状态灯/状态文本、Web URL、端口、源代码路径、本地 package 版本、短 commit、远程状态；右栏提供启动、关闭、重启、检查更新、拉取更新、打开 WebUI；底部是只读多行日志框。`Pull Update` 初始禁用，只有检查结果为可安全拉取时启用。

- [ ] **Step 4: 接入异步操作锁和按钮行为。**

  使用一个 `SemaphoreSlim` 保护所有按钮操作；进入操作后禁用其它操作按钮，结束、异常或超时后统一恢复。启动/重启调用现有 PowerShell 服务脚本，成功刷新状态并打开默认浏览器到 `http://127.0.0.1:3080`；关闭不打开浏览器；打开 WebUI 只打开固定 URL。窗口退出不发送 Stop。

- [ ] **Step 5: 接入检查和拉取更新。**

  检查按钮只调用 `CheckAsync` 并展示状态。拉取按钮弹出确认框后再次检查，确认后调用 `UpdateCoordinator.PullAsync`；把每个阶段、退出码、stdout/stderr 摘要写入日志；完成后刷新服务、版本、短 commit 和远程状态。任何异常都显示阶段和原因，不泄漏 token、对话内容、完整环境变量或命令行中的敏感数据。

- [ ] **Step 6: 编译 GUI。**

  Run: `dotnet build <launcher-root>\src\DshPlusPlus\DshPlusPlus.csproj -c Release`

  Expected: exit code 0，无编译错误；GUI 依赖核心库且不带第三方 UI 包。

### Task 8: 运行回归、发布单文件并做目标目录验收

**Files:**
- Modify: `<launcher-root>\docs\superpowers\plans\2026-08-16-dsh-plus-plus-implementation-plan.md`
- Create: `<launcher-root>\publish\dsh++.exe`

- [ ] **Step 1: 运行完整核心测试和编译。**

  Run:

  ```powershell
  dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj
  dotnet build <launcher-root>\src\DshPlusPlus\DshPlusPlus.csproj -c Release
  ```

  Expected: 测试退出码 0，所有测试 PASS；Release 编译退出码 0。

- [ ] **Step 2: 发布自包含单文件。**

  Run:

  ```powershell
  dotnet publish .\src\DshPlusPlus\DshPlusPlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
  ```

  Expected: `.\publish\dsh++.exe` 存在；发布目录不把源码、测试运行器或临时文件当作启动器入口。

- [ ] **Step 3: 做发布文件静态验收。**

  检查 EXE 文件大小、PE 文件存在性、目标框架和输出目录；重新读取设计文档与本计划，逐项确认自动探测路径、端口、服务脚本、检查/拉取分离、dirty 阻止、fast-forward-only、无 push/reset/checkout、无 taskkill node 和低开销更新检查均已覆盖。

- [ ] **Step 4: 做 Windows 手工验收。**

  双击 EXE，确认 B 布局；点击启动后 `127.0.0.1:3080` 可访问并打开 WebUI；点击关闭后端口释放；点击重启后可再次访问；检查更新只 fetch；dirty worktree 时拉取按钮禁用；构建失败日志显示阶段且不自动回滚；确认 `.dsh`、`<plugin-root>`、插件目录和现有 BAT 未被改动。

- [ ] **Step 5: 记录验证结果。**

  只在完整测试、Release build、publish 和静态/手工验收均有新鲜命令输出后报告完成；如果外部环境（网络、GitHub、pnpm、端口或现有 DSH 服务）阻断，则明确记录实际阻断阶段和已完成的替代验证，不声称 EXE 已通过未执行的验收。

## 计划自审

- 规格中的 B 布局对应 Task 7；固定路径和 protected objects 对应 Task 2、3、5、7、8。
- 现有 PowerShell 服务脚本复用对应 Task 3；没有新增 `taskkill /IM node.exe` 或复制进程树终止逻辑。
- fetch-only 检查、GitHub remote 校验、upstream/main/master 优先级和 dirty 阻止对应 Task 5；二次检查、Stop/pull/install/build/Start 顺序和不回滚对应 Task 6。
- .NET 9、自包含 win-x64、单文件 EXE 对应 Task 1、7、8。
- 计划中的每个生产行为均先有测试或可验证的编译/手工验收步骤，没有未定义的实施占位任务。
