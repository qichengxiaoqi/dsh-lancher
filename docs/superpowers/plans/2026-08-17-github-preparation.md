# dsh++ GitHub 开源准备实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 移除 dsh++ 对当前电脑路径的依赖，补齐项目说明和 GitHub Release 自动发布配置，使项目可以安全初始化 Git 并在不同 Windows 机器上自动发现 DSH 环境。

**Architecture:** 使用一个可注入、低开销的 `LauncherPathDiscovery` 只检查环境变量、应用目录邻近目录和用户目录，不扫描整盘；设置默认使用自动探测，维护页保存的路径作为明确的手动覆盖。发布产物只由 GitHub Actions 在 `v*` 标签下生成，不进入 Git 历史。

**Tech Stack:** .NET 9 WinForms、C#、System.Text.Json、GitHub Actions、Windows `dotnet publish`。

---

### Task 1: 移除硬编码路径并实现自动探测

**Files:**
- Create: `<launcher-root>\src\DshPlusPlus.Core\Services\LauncherPathDiscovery.cs`
- Modify: `<launcher-root>\src\DshPlusPlus.Core\Models\LauncherPaths.cs`
- Modify: `<launcher-root>\src\DshPlusPlus.Core\Models\DshPaths.cs`
- Modify: `<launcher-root>\src\DshPlusPlus.Core\Models\LauncherSettings.cs`
- Modify: `<launcher-root>\src\DshPlusPlus.Core\Services\LauncherSettingsStore.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\Program.cs`

- [x] **Step 1: 先写自动探测失败测试**

在 `Program.cs` 测试入口中增加临时沙盒测试：创建 `workspace\dsh++\publish`、`workspace\Deepseek-dsh\.git`、`workspace\Deepseek-dsh\package.json`、服务脚本、`home\.dsh\profiles\web\package.json`、`workspace\dsp` 和一个临时 `tools` 目录；通过注入的环境变量读取器和 PATH 验证探测结果。增加断言：

```csharp
var discovered = new LauncherPathDiscovery(appBase, userProfile, ReadEnvironment).Discover();
Assert.Equal(dshRoot, discovered.DshRoot);
Assert.Equal(dshHome, discovered.DshHome);
Assert.Equal(profile, discovered.ProfileDirectory);
Assert.Equal(Path.Combine(workspace, "dsp"), discovered.PluginRoot);
Assert.Equal(Path.Combine(dshRoot, "scripts", "windows", "DeepSeekHarnessService.ps1"), discovered.ServiceScript);
Assert.Equal(gitPath, discovered.GitExecutable);
Assert.Equal(pnpmPath, discovered.PnpmExecutable);
```

同时断言 `LauncherPaths.CreateDefault()` 和 `DshPaths.CreateDefault()` 不包含固定盘符、用户目录或 Codex runtime 路径。

- [x] **Step 2: 运行测试确认 RED**

运行：

```powershell
dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj
```

预期：因 `LauncherPathDiscovery` 尚不存在或默认值仍为本机路径而失败。

- [x] **Step 3: 实现最小自动探测器**

`LauncherPathDiscovery` 必须按以下优先级执行：

1. `DSH_ROOT`/`DEEPSEEK_DSH_ROOT`；否则检查应用目录、其最多 5 层父目录及相邻的 `Deepseek-dsh`、`DeepSeek-dsh`、`deepseek-harness`；只有同时存在 `.git` 和 `package.json` 才接受。
2. `DSH_HOME`/`DEEPSEEK_DSH_HOME`；否则使用当前用户的 `%USERPROFILE%\.dsh`，不读取固定用户名。
3. `DSH_PROFILE_DIR`/`DSH_PROFILE`；否则优先使用 `<DshHome>\profiles\web`，再检查 `profiles` 的一层子目录中包含 `package.json` 的目录。
4. `DSH_SERVICE_SCRIPT`；否则检查 `<DshRoot>\scripts\windows\DeepSeekHarnessService.ps1`，再只在该 `scripts\windows` 目录查找 `*Harness*Service*.ps1`。
5. `DSH_PLUGIN_ROOT`；否则优先使用 DSH 根目录父目录下存在的 `dsp`，再使用 `<DshHome>\plugins`，再从 Profile `package.json` 的 `file:` 依赖解析插件父目录。
6. `DSH_POWERSHELL`、`DSH_GIT`、`DSH_PNPM` 覆盖工具路径；否则从 PATH 查找 `powershell.exe`/`pwsh.exe`、`git.exe`、`pnpm.cmd`/`pnpm.exe`。找不到时保留可执行命令名，让验证页给出警告。
7. `PNPM_STORE_DIR`/`NPM_CONFIG_STORE_DIR` 有值时使用它；没有时保持空字符串，让 pnpm 使用自身默认 store，不在启动器中猜测或创建 store。

探测器只做有限的 `File.Exists`、`Directory.Exists`、一层目录枚举和 Profile JSON 读取，不启动 pnpm/git/PowerShell，不扫描整盘。

- [x] **Step 4: 让设置存储应用自动探测**

给 `LauncherSettings` 增加 `AutoDetectPaths`，默认 `true`，SchemaVersion 提升到 `2`。`LauncherSettingsStore` 注入 `LauncherPathDiscovery`；读取不存在、损坏或 `AutoDetectPaths=true` 的设置时，将 `Paths` 替换为探测结果；`AutoDetectPaths=false` 时完整保留用户手动路径。`Program.Main` 创建同一个 discovery 实例并传给 settings store 和维护页。

- [x] **Step 5: 运行测试确认 GREEN**

再次运行上述测试命令，预期自动探测、默认值清理和设置存储测试全部通过。

### Task 2: 保留手动覆盖并修正依赖命令

**Files:**
- Modify: `<launcher-root>\src\DshPlusPlus.Core\Services\PathValidator.cs`
- Modify: `<launcher-root>\src\DshPlusPlus.Core\Services\ProjectCommandService.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Pages\MaintenancePage.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\MainForm.cs`
- Modify: `<launcher-root>\tests\DshPlusPlus.Core.Tests\Program.cs`

- [x] **Step 1: 写回归测试**

增加两项测试：空 `PnpmStore` 时安装参数严格为 `install`；显式设置 store 时参数为 `install --store-dir <store>`。增加设置存储测试：手动保存 `AutoDetectPaths=false` 后重新加载，路径必须保持不变。

- [x] **Step 2: 修改 pnpm 命令构造**

`ProjectCommandService.InstallDependenciesAsync` 只在 `PnpmStore` 非空时追加 `--store-dir`；不再把机器特定的 store 路径写进命令。`PathValidator` 对空工具路径给出警告而非异常，对空 pnpm store 视为使用 pnpm 默认值。

- [x] **Step 3: 更新维护页交互**

新增“自动检测并应用”按钮：调用 discovery，写入探测结果并保存 `AutoDetectPaths=true`；现有“验证并保存”保存用户填写的路径并设置 `AutoDetectPaths=false`。界面显示当前是“自动探测”还是“手动覆盖”，不修改 DSH 源码和用户数据。

- [x] **Step 4: 运行测试和 Release 构建**

运行：

```powershell
dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj
dotnet build <launcher-root>\DshPlusPlus.sln -c Release --no-restore
```

预期：测试全部 PASS，构建 0 警告、0 错误。

### Task 3: GitHub 项目文档与仓库卫生

**Files:**
- Create: `<launcher-root>\README.md`
- Create: `<launcher-root>\.gitignore`
- Create: `<launcher-root>\.gitattributes`
- Create: `<launcher-root>\.github\workflows\release.yml`
- Create: `<launcher-root>\docs\configuration.md`

- [x] **Step 1: 编写 README**

README 使用中文介绍 dsh++、六个功能页、托盘/低占用策略、自动路径探测、手动覆盖、API Key 安全、构建测试命令和 GitHub Release 流程；不写入任何当前用户路径、密钥、会话内容或本机插件地址。

- [x] **Step 2: 添加忽略规则**

`.gitignore` 忽略 `.vs/`、`**/bin/`、`**/obj/`、`publish/`、`artifacts/`、`*.user`、`*.suo`、本地 `settings.json`、凭据文件、日志和临时目录；不忽略源码、测试、README、文档或 GitHub workflow。

- [x] **Step 3: 添加 Release workflow**

`release.yml` 使用 `windows-latest`、.NET 9，执行核心测试、win-x64 self-contained single-file publish，并仅在推送 `v*` 标签时用 GitHub token 创建 Release，上传 `artifacts/dsh++.exe`。工作流不把 `publish/` 目录提交到仓库。

- [x] **Step 4: 写配置说明**

`docs/configuration.md` 记录自动探测优先级、可选环境变量、配置文件 `%LOCALAPPDATA%\\dsh++\\settings.json`、手动覆盖语义和安全边界。

### Task 4: 最终静态检查与 Git 初始化准备

- [x] **Step 1: 清理旧发布目录中的可提交文件**

保留本地 `<launcher-root>\publish` 供用户运行，但依靠 `.gitignore` 使其不进入 Git；不删除用户源码、`.dsh`、插件、sessions 或凭据。确认发布目录不在 `git add` 候选中。

- [x] **Step 2: 静态检查本机路径泄漏**

运行：

```powershell
rg -n --hidden -g '!**/bin/**' -g '!**/obj/**' -g '!publish/**' '[A-Za-z]:\\\\' src tests README.md docs\configuration.md
```

预期无输出；历史设计/交接文档可保留原始需求路径，但不参与运行时配置。

- [x] **Step 3: 最终验证**

运行：

```powershell
dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj
dotnet build <launcher-root>\DshPlusPlus.sln -c Release --no-restore
dotnet publish <launcher-root>\src\DshPlusPlus\DshPlusPlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore -o <launcher-root>\publish
```

只读确认 `<launcher-root>\publish\dsh++.exe` 存在，`git check-ignore <launcher-root>\publish\dsh++.exe` 返回该文件被忽略；不执行 `git init`、`git commit` 或 `git push`，待用户确认 GitHub 仓库名称和远程地址后再做。
