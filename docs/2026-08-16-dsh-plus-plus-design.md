# dsh++ 启动器设计规格

> 状态：已确认，准备进入实施计划
>
> 确认项：B「状态面板」布局；更新采用“先检查、再单独拉取”；目标框架采用 .NET 9 WinForms。

## 1. 目标与边界

`dsh++` 是一个独立的 Windows 桌面启动器，用于管理自动探测到的 DeepSeek Harness 源码版 Web 服务。启动器源码、测试和发布物位于启动器项目目录，不把任何文件写入 DSH 源码仓库的目录结构。

第一版提供：

1. 启动、关闭、重启 DSH Web 服务；
2. 显示服务状态、源码版本、短 commit、远程更新状态和操作日志；
3. 检查 GitHub 远程更新，但检查阶段只执行 `git fetch origin --prune`，不修改工作区；
4. 用户确认后执行快进拉取、依赖安装、构建和服务重启；
5. 启动成功后用系统默认浏览器打开 `http://127.0.0.1:3080`。

历史初版规格不提供托盘和自动更新；当前实现已补充托盘提示、GitHub Release 更新检查和安全的用户确认更新，不提供自动回滚、git push、强制重置或覆盖用户未提交修改。

## 2. 固定环境与保护对象

| 项目 | 固定值 |
| --- | --- |
| DSH 源码根目录 | `<dsh-root>` |
| DSH 服务脚本 | `<dsh-root>\scripts\windows\DeepSeekHarnessService.ps1` |
| Web 地址 | `http://127.0.0.1:3080` |
| 服务端口 | `3080` |
| pnpm store | `<plugin-root>\.pnpm-store` 或 pnpm 默认 store |
| 对话记录 | `%USERPROFILE%\.dsh` |
| 本地插件目录 | `<plugin-root>\dsh-caveman`、`<plugin-root>\dsh-token-billing` |
| 发布 EXE | `<launcher-root>\publish\dsh++.exe` |

启动器不得删除、迁移、重建或清理 `.dsh`、`<plugin-root>`、对话目录、插件目录、DSH 现有 BAT 和源码文件。路径由自动探测或明确的手动覆盖提供，不接受来自界面的任意命令行输入。

当前机器只安装 .NET 9 SDK，因此项目目标为 `net9.0-windows`。发布使用 `win-x64`、自包含、单文件模式；不要求用户预装 .NET Runtime。

## 3. 用户界面

窗口采用 B「状态面板」布局，初始客户区约为 720×460，使用系统字体和 WinForms 原生控件，不引入第三方 UI 框架。

左栏显示运行事实：

- 标题：`dsh++`；
- 状态灯和状态文字：已停止、启动中、运行中、启动异常、关闭中、更新中、错误、状态未知；
- Web 地址和端口；
- 源码路径；
- 本地 `package.json` 版本与短 commit；
- 远程状态：未检查、最新、发现更新、本地领先、无法连接、工作区有未提交修改。

右栏显示操作：

- 启动服务；
- 关闭服务；
- 重启服务；
- 检查更新；
- 拉取更新（只有检查确认远程领先且工作区干净时启用）；
- 打开 WebUI；
- 底部只读日志框。

同一时间只允许一个操作进入执行阶段。操作开始时禁用其他动作按钮；操作结束、失败或超时后统一恢复按钮状态，并保留失败阶段、退出码和简短原因。

## 4. 组件划分

目录结构如下：

```text
<launcher-root>\
├─ src\
│  ├─ DshPlusPlus.Core\
│  │  ├─ DshPlusPlus.Core.csproj
│  │  ├─ Models\
│  │  │  ├─ ProcessResult.cs
│  │  │  ├─ ServiceState.cs
│  │  │  ├─ UpdateState.cs
│  │  │  └─ RepositorySnapshot.cs
│  │  └─ Services\
│  │     ├─ ProcessRunner.cs
│  │     ├─ DshServiceController.cs
│  │     ├─ ServiceStatusProbe.cs
│  │     ├─ GitRepositoryService.cs
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
│  └─ 2026-08-16-dsh-plus-plus-design.md
└─ publish\
   └─ dsh++.exe
```

职责边界：

- `ProcessRunner`：只负责以隐藏窗口启动固定程序、传递参数数组、捕获 stdout/stderr、退出码、超时和取消；不负责解释业务结果。
- `DshServiceController`：只调用现有 PowerShell 服务脚本的 `Start`、`Stop`、`Restart`，不复制进程树终止逻辑。
- `ServiceStatusProbe`：通过 3080 端口连接和 HTTP 请求区分运行中、已停止、启动异常和状态未知。
- `GitRepositoryService`：读取分支、upstream、工作区、commit、远程领先数和 `package.json` 版本；只允许固定的 fetch、只读 Git 查询和快进 pull。
- `UpdateCoordinator`：编排检查与拉取流程，执行二次安全检查、关闭服务、安装依赖、构建和重新启动。
- `MainForm`：只负责 B 布局、状态呈现、按钮状态、确认提示和日志投递，不直接拼接外部命令。

## 5. 服务操作数据流

启动器调用：

```text
PowerShell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass
  -File <dsh-root>\scripts\windows\DeepSeekHarnessService.ps1
  -Action Start|Stop|Restart
```

PowerShell 进程窗口隐藏，启动器等待脚本退出并检查退出码。脚本退出码非零时，界面只显示失败，不显示成功。

服务状态探测规则：

- 3080 无监听：`已停止`；
- 3080 有监听且 HTTP 返回 2xx–4xx：`运行中`；
- 3080 有监听但 HTTP 请求失败：`启动异常`；
- TCP 或 HTTP 探测本身发生异常：`状态未知`，日志记录原因。

启动和重启成功后打开 WebUI；关闭服务不自动打开或关闭浏览器。启动器退出时不发送 Stop，不影响仍在运行的 DSH 服务。

## 6. 更新数据流

### 6.1 检查更新

检查按钮执行以下顺序：

1. 确认 `<dsh-root>`、`.git` 和 `package.json` 存在；
2. 读取当前分支、当前 HEAD 和工作区状态；
3. 读取 remote URL，要求目标 remote 为 GitHub 地址；
4. 执行 `git fetch origin --prune`；
5. 解析远程分支，优先使用当前分支的 upstream，其次 `origin/main`，最后 `origin/master`；
6. 执行只读比较，得到 behind、ahead、远程短 commit 和远程 `package.json` 版本；
7. 工作区含已修改或未跟踪文件时显示警告并禁用“拉取更新”；
8. behind 大于零且工作区干净时启用“拉取更新”。

远程领先时显示“发现可用更新”；behind 为零时显示“已是最新”；ahead 大于零时显示“本地有未推送提交”，但启动器没有任何 push 功能。无 upstream、远程非 GitHub、网络失败或 Git 非零退出时，界面显示明确阶段和原因，不修改本地源码。

### 6.2 拉取更新

拉取按钮只在安全条件满足时启用，点击后再次执行工作区检查、远程分支解析和 fetch，防止检查结果过期。之后按顺序执行：

1. 调用服务脚本 `Stop`；
2. `git pull --ff-only`；
3. 使用已解析的 pnpm 可执行文件执行 `pnpm install`，仅在配置了 store 时追加 `--store-dir <plugin-root>\.pnpm-store`；
4. 执行 `pnpm run build`；
5. 调用服务脚本 `Start`；
6. 刷新本地版本、commit 和服务状态。

任何阶段失败都停止后续阶段，显示失败阶段和错误信息，不自动回滚源码；用户仍可重新点击“启动服务”。流程中绝不执行 `git push`、`git reset --hard`、`git checkout --` 或删除/覆盖用户文件。

## 7. 安全、并发和超时

- 外部进程统一使用固定可执行文件和参数数组；`.cmd` 运行时只使用固定的 `cmd.exe /d /c` 包装，不拼接用户输入。
- 启动、关闭、重启调用现有脚本；启动等待上限为 90 秒，关闭和重启额外保留进程结束等待窗口。
- Git 检查和依赖安装/构建均有独立超时；超时结果显示为失败并允许再次启动服务。
- 使用一个异步操作锁和 UI 状态机避免重复点击、并行拉取或重复启动。
- 日志包含时间、阶段、退出码和截断后的错误文本；不写入 token、对话正文、插件正文或环境变量全集。
- 默认不请求管理员权限。
- 服务关闭逻辑由现有脚本完成；启动器不执行 `taskkill /IM node.exe`，避免影响其他 Node 应用。

## 8. 测试与验收

核心测试覆盖：

- Git 版本比较：最新、落后、领先、无 upstream；
- 工作区：干净、已修改、未跟踪文件；
- GitHub remote 校验、网络失败和 Git 非零退出码；
- 进程退出码、标准错误、超时和取消；
- 本地/远程版本和短 commit 展示；
- 拉取流程禁止 dirty worktree、禁止 push、允许 fast-forward pull；
- 现有 PowerShell 与 BAT 回归检查仍通过。

Windows 验收覆盖：双击 EXE、B 布局、启动并打开 WebUI、关闭后释放 3080、重启后重新可访问、最新状态、远程领先状态、dirty worktree 阻止拉取、更新失败阶段日志、自包含单文件发布，以及 `.dsh`、`<plugin-root>`、插件和现有 BAT 未被触碰。

发布命令：

```powershell
dotnet publish <launcher-root>\src\DshPlusPlus\DshPlusPlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o <launcher-root>\publish
```

## 9. 明确决策

- 采用 .NET 9 WinForms，不引入第三方 UI 框架；
- 采用 B「状态面板」布局；
- 检查更新与拉取更新分离；
- 复用现有服务 PowerShell 脚本，不复制进程树清理逻辑；
- 启动器项目和 EXE 放在独立的 `<launcher-root>`，不放入 `<dsh-root>`；
- 当前实现支持托盘提示和 GitHub Release 更新检查；更新下载和重启仍需用户确认，不执行自动回滚。
