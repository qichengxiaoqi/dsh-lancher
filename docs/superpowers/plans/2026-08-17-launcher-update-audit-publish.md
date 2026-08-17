# dsh++ 启动器更新与 GitHub 发布实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 dsh++ 增加低开销的 GitHub Release 自动更新能力，清理公开仓库中的本机路径与疑似凭据残留，补齐运行环境说明，并将安全审计后的源码推送到 `qichengxiaoqi/dsh-lancher`。

**Architecture:** 启动器仅访问固定仓库的 GitHub Releases API，每次启动最多后台检查一次并遵守设置的时间间隔；下载前验证 Release 资产名称、大小和 SHA-256 digest，使用一次性隐藏更新进程在主程序退出后替换 exe，不常驻后台进程。更新检查和安装均可取消、串行且有超时，不调用 DSH Git 更新流程。

**Tech Stack:** .NET 9 WinForms、System.Text.Json、HttpClient、SHA-256、GitHub REST Releases API、Windows 单文件发布、Git/gh CLI。

---

### Task 1: 更新领域模型、GitHub Release 客户端与安全安装器

**Files:**
- Create: `src/DshPlusPlus.Core/Models/LauncherUpdateModels.cs`
- Create: `src/DshPlusPlus.Core/Services/LauncherUpdateService.cs`
- Modify: `src/DshPlusPlus.Core/Models/LauncherSettings.cs`
- Modify: `src/DshPlusPlus.Core/DshPlusPlus.Core.csproj`
- Test: `tests/DshPlusPlus.Core.Tests/Program.cs`

- [x] **Step 1: 写失败测试**

增加可注入 `HttpMessageHandler` 的测试，覆盖：Release JSON 解析、忽略草稿/预发布、`v0.2.0` 高于 `v0.1.0`、未知 tag 不触发更新、GitHub API 401/403/500/超时返回可展示错误；增加下载资产的 SHA-256 不匹配时拒绝安装测试。测试只使用 `test-api-key` 等非 token 形态的占位值。

- [x] **Step 2: 运行测试确认 RED**

运行：

```powershell
dotnet run --project .\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj
```

预期：因更新模型和服务尚未存在而编译失败。

- [x] **Step 3: 实现 Release 查询**

固定仓库为 `qichengxiaoqi/dsh-lancher`，请求 `https://api.github.com/repos/qichengxiaoqi/dsh-lancher/releases/latest`，使用 `User-Agent: dsh++`、10 秒超时、取消令牌和不保存响应正文的错误处理。只接受非草稿、非预发布且包含 `dsh++.exe` 的 Release；从 tag 解析 `v` 前缀版本，使用 `Version` 比较。

- [x] **Step 4: 实现资产下载与安全替换**

下载到 `%TEMP%\dsh++-update\<random>\dsh++.exe.download`，限制最大尺寸 250 MB，检查 HTTPS、Content-Length、实际文件大小和 API 返回的 `sha256:<hex>` digest（缺少 digest 时仍必须做本地 SHA-256，并在 UI 标记来源）；下载完成后写入一次性隐藏 `cmd.exe`/PowerShell 更新器，等待当前进程退出、原子替换当前 exe、启动新 exe 并清理临时目录。安装器不得覆盖非当前 exe，不删除用户数据，不使用 `taskkill`，失败保留下载文件并返回错误。

- [x] **Step 5: 运行核心测试确认 GREEN**

运行上述测试，预期全部通过；重点确认 API Key、Bearer、Authorization 和下载 URL 不会被写入日志或异常消息。

### Task 2: 接入设置页、托盘提示与低频自动检查

**Files:**
- Modify: `src/DshPlusPlus.Core/Models/LauncherSettings.cs`
- Modify: `src/DshPlusPlus/MainForm.cs`
- Modify: `src/DshPlusPlus/Program.cs`
- Modify: `src/DshPlusPlus/UI/Pages/LauncherSettingsPage.cs`
- Create or modify: `src/DshPlusPlus/UI/Pages/LauncherSettingsPage.cs`
- Test: `tests/DshPlusPlus.Core.Tests/Program.cs`

- [x] **Step 1: 增加更新设置和服务构造**

给设置加入 `AutoUpdateEnabled=true`、`UpdateCheckIntervalHours=24`、`LastUpdateCheckUtc`；限制间隔为 6–168 小时。`Program` 创建共享 `HttpClient` 和更新服务，应用退出时释放；任何异常只写脱敏摘要。

- [x] **Step 2: 接入设置页手动操作**

在“启动器设置”页增加当前版本、更新状态、“立即检查”和“下载并重启”按钮；检查按钮只执行一次请求，下载按钮二次确认并显示进度，安装完成后请求退出。按钮和文字使用现有自适应布局，不能造成遮挡。

- [x] **Step 3: 接入托盘和静默后台检查**

启动后延迟约 3 秒、且只在首次启动执行一次后台检查；设置关闭时不请求网络。成功发现新版本时只通过托盘通知和设置页状态提示，不自动重启、不启动 DSH、不打开网页；网络失败静默记录，不反复重试。检查使用独立 `CancellationTokenSource`，窗口关闭时取消。

- [x] **Step 4: 运行 UI/核心编译检查**

运行测试和 Release 编译，预期 0 警告、0 错误。

### Task 3: README、运行环境和隐私清理

**Files:**
- Modify: `README.md`
- Modify: `docs/configuration.md`
- Modify: `docs/2026-08-16-dsh-plus-plus-design.md`
- Modify: `DSH-Launcher-Handoff.md`
- Modify: `2026-08-16-dsh-launcher-design.md`
- Modify: `docs/superpowers/plans/*.md`
- Modify: `docs/superpowers/specs/*.md`
- Modify: `tests/DshPlusPlus.Core.Tests/Program.cs`
- Modify: `.gitignore`

- [x] **Step 1: 脱敏历史文档和测试占位符**

将公开文档中的当前电脑绝对路径替换为 `<dsh-root>`、`<launcher-root>`、`%USERPROFILE%\\.dsh`、`<plugin-root>` 等通用写法；保留行为要求但删除用户名、盘符、真实插件地址和本机运行时目录。将测试中的 `sk-test...` 改为 `test-api-key`，不改变测试语义。

- [x] **Step 2: 增加完整隐私审计规则**

README 明确说明不提交 API Key、credentials、sessions、`.dsh`、插件目录、日志和发布物；`.gitignore` 增加可能的本地凭据与运行时文件。静态扫描覆盖用户目录、特定本机盘符、邮箱、局域网地址、`sk-`、`ghp_`、`github_pat_`、`AKIA`、Bearer 值和 credentials 文件名，仅允许测试中的明确占位符。

- [x] **Step 3: 补齐 GitHub 运行环境说明**

README 增加支持系统 Windows 10/11 x64、.NET 9 SDK（源码构建）、Git、pnpm、PowerShell 5.1 或 PowerShell 7、可选 DSH 源码/Profile 环境；Release 用户不需要安装 .NET。说明自动更新需要 HTTPS 访问 GitHub Releases，更新不会修改 DSH 源码、`.dsh` 或插件目录。

### Task 4: 测试、发布、Git 初始化和推送

**Files:**
- Modify: `.github/workflows/release.yml`
- Add only intended project files to Git

- [x] **Step 1: 运行最终审计与测试**

执行敏感信息扫描、`dotnet run`、`dotnet build -c Release` 和 `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true`；确认 `publish/dsh++.exe` 被忽略且不进入暂存区。

- [x] **Step 2: 检查 GitHub CLI 和远程仓库**

只读执行 `gh --version`、`gh auth status`、`git ls-remote https://github.com/qichengxiaoqi/dsh-lancher.git`。若身份验证缺失或远程仓库已有不可覆盖内容，停止推送并报告，不覆盖远程历史。

- [x] **Step 3: 初始化并提交本地仓库**

在确认工作区只包含本项目文件后执行 `git init`、设置 `user.name`/`user.email`（若本机已有配置则保留）、创建 `main` 分支、添加远程 `origin`，只提交被审计且被 `.gitignore` 排除发布物的文件。提交信息为 `feat: add GitHub updater and privacy-safe release setup`。

- [x] **Step 4: 推送并验证**

若远程为空，执行 `git push -u origin main`；若远程已有同名分支或文件，先停止并请求用户确认合并策略。推送后执行 `git ls-remote --heads origin main` 和 `gh repo view qichengxiaoqi/dsh-lancher`，确认仓库可见且没有发布物、凭据或个人路径进入提交。
