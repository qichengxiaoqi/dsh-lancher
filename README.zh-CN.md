# dsh++——一个适配于 DeepSeek Harness 的轻量启动器工具

[English](README.md) | 简体中文

`dsh++` 是面向 [DeepSeek Harness（DSH）](https://github.com/deepseek-ai/deepseek-harness) 的轻量级启动器与管理控制台。项目保留现有的 .NET 9 WinForms Windows 版本，并新增复用同一 `DshPlusPlus.Core` 的跨平台 Avalonia 版本。

本仓库只包含启动器，不包含 DeepSeek Harness 源码、用户会话、插件目录、凭据或 API Key。

> 本项目使用 Vibe Coding（vibecoding）实现，旨在帮助大家节省 Token，不重复造轮子，并提供一个便捷的启动器入口，让 DSH 更容易使用。如果你愿意且有余力，欢迎帮助优化这个项目，非常感谢！

## 功能

- **DSH 管理**：启动、停止和重启服务，检查 Git 状态，拉取安全更新，打开 Web UI，查看实时日志。
- **安装维护**：查看并验证 DSH、Profile、插件和工具路径，支持自动检测与手动覆盖。
- **DeepSeek API**：安全保存本地 API Key，检测连接，查看模型列表和当前余额；API Key 始终以掩码显示。
- **系统级设置**：扫描 DSH 作用域内的 `AGENTS.md`、`CLAUDE.md`、`settings.yaml` 和 patch 文件，默认只读。
- **插件设置**：读取 Profile 清单、插件 `package.json`、本地 `file:` 依赖和运行时插件状态，支持启用或禁用插件并生成备份。
- **技能导入**：扫描本机 Codex 与 Claude Code 的技能目录，比较 DSH 目标内容，并手动勾选导入 `SKILL.md` 目录技能或平铺技能文件；发生冲突时先生成时间戳备份。
- **启动器设置**：自定义主题、强调色、字体缩放、可收缩导航栏、刷新间隔和启动页。

界面支持 Obsidian 深色主题、浅色主题、高对比度主题、DPI 感知的弹性布局、只显示图标的收缩导航栏以及 Windows 托盘通知。

## 快速开始

### 直接使用 GitHub Release

从 [GitHub Releases](https://github.com/qichengxiaoqi/dsh-lancher/releases) 下载 `dsh++.exe` 后直接运行。首次启动会自动探测本机 DSH 环境；如果环境不完整，可以打开“安装维护”查看缺失项目。

### 从源码运行

在仓库根目录执行：

```powershell
dotnet restore .\DshPlusPlus.sln
dotnet run --project .\src\DshPlusPlus\DshPlusPlus.csproj
```

运行核心回归测试并构建 self-contained Windows 可执行文件：

```powershell
dotnet run --project .\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj
dotnet build .\DshPlusPlus.sln -c Release
dotnet publish .\src\DshPlusPlus\DshPlusPlus.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish
```

发布文件为 `publish\dsh++.exe`。`publish/` 已加入 Git 忽略规则，不会进入提交记录。

### 跨平台 Avalonia 版本

Avalonia 界面是独立项目，不引用 WinForms：

```powershell
dotnet run --project .\src\DshPlusPlus.Avalonia\DshPlusPlus.Avalonia.csproj
dotnet publish .\src\DshPlusPlus.Avalonia\DshPlusPlus.Avalonia.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

macOS 的 DMG 和 Linux 的 `tar.gz` 由 GitHub Actions 对应系统 runner 打包；Windows 本机不需要、也不应承担多平台打包工作。

## 运行环境与所需工具

### 直接运行 Release

- WinForms：Windows 10/11，x64 或 ARM64。
- Avalonia：Windows x64、macOS Intel/Apple Silicon、Linux x64/ARM64。
- 从 GitHub Releases 下载的 self-contained 文件不要求另外安装 .NET。
- 使用 DSH 管理功能需要本机已有 DeepSeek Harness 源码、Profile 和插件目录。
- 使用服务管理、依赖安装和构建相关功能，需要 PowerShell 5.1 或 PowerShell 7、Git 和 pnpm，并确保它们可以从 `PATH` 找到。
- 使用启动器自动更新，需要能够通过 HTTPS 访问 GitHub API 和 Release 资产。

### 从源码构建

需要 .NET 9 SDK 和 Git，并使用目标 RID 对应的 SDK 支持。DSH 源码与 pnpm 不包含在本仓库中，需要另行安装；DMG 由 GitHub Actions 的 macOS runner 使用 `hdiutil` 创建，Linux 压缩包使用 runner 的 `tar` 创建。项目不依赖固定盘符或固定用户信息。

## 自动路径探测

启动器不会把开发者电脑的盘符、用户名或 Codex 运行时路径写入默认配置。路径探测顺序如下：

1. 环境变量指定的路径。
2. 启动器附近的 `Deepseek-dsh`、`DeepSeek-dsh` 或 `deepseek-harness` 目录，并且目录必须同时含有 `.git` 和 `package.json`。
3. 当前用户的 `%USERPROFILE%\.dsh`、Profile 位置以及有限范围内的插件目录。
4. Profile 中声明的本地 `file:` 依赖。
5. `PATH` 中可用的 PowerShell、Git 和 pnpm。

可选环境变量：

| 变量 | 作用 |
| --- | --- |
| `DSH_ROOT` / `DEEPSEEK_DSH_ROOT` | DSH 源码根目录 |
| `DSH_HOME` / `DEEPSEEK_DSH_HOME` | DSH Home 目录 |
| `DSH_PROFILE_DIR` / `DSH_PROFILE` | Profile 目录 |
| `DSH_PROFILE_NAME` | Profile 名称，默认 `web` |
| `DSH_SERVICE_SCRIPT` | 服务 PowerShell 脚本 |
| `DSH_PLUGIN_ROOT` | 插件根目录 |
| `DSH_POWERSHELL` | PowerShell 可执行文件 |
| `DSH_GIT` | Git 可执行文件 |
| `DSH_PNPM` | pnpm 可执行文件 |
| `PNPM_STORE_DIR` / `NPM_CONFIG_STORE_DIR` | 可选 pnpm store；为空时使用 pnpm 自身默认值 |

启动器配置保存于 `%LOCALAPPDATA%\dsh++\settings.json`。默认启用 `AutoDetectPaths`。在“安装维护”中点击“验证并保存”会创建手动覆盖；点击“自动检测并应用”即可恢复跨机器自动探测。

默认 Web UI 地址是 `http://127.0.0.1:3080`，这是 DSH 的运行时默认地址，不依赖某台电脑的文件路径。

## 技能导入

打开“插件设置”，点击“扫描技能”，勾选需要导入的条目，再点击“导入选中项”。启动器会自动探测：

- `%USERPROFILE%\\.codex\\skills`，或 `CODEX_HOME` 指定的 Codex 目录；
- `%USERPROFILE%\\.claude\\skills`，或 `CLAUDE_CONFIG_DIR` 指定的 Claude Code 目录；
- `%USERPROFILE%\\.dsh\\skills` 作为 DSH 默认目标目录。

只扫描直接位于技能根目录下的平铺 `*.md` 文件，以及包含 `SKILL.md` 的一级目录包。无效 frontmatter、嵌套技能目录和目录链接会被忽略或标记为不支持。同内容会跳过；目标内容不同则先创建带时间戳的备份，再执行替换。原始 Codex 与 Claude Code 目录不会被修改。

## 启动器自动更新

启动器默认执行低频更新检查：主窗口显示后延迟检查一次，之后默认每 24 小时检查一次。检查目标是本仓库的稳定 GitHub Release：

`https://github.com/qichengxiaoqi/dsh-lancher`

发现新版本后，启动器只通过托盘通知和“启动器设置”页面提示，不会自动重启或修改 DSH。用户确认后才会下载 `dsh++.exe`，检查文件大小和 SHA-256，并使用一次性的隐藏更新进程替换当前 exe 后重启。

更新过程不会启动 DSH，也不会修改源码、`.dsh`、sessions 或插件目录。可以在“启动器设置”中关闭自动检查、调整 6–168 小时的检查间隔，或手动检查和下载更新。网络错误不会触发高频重试，也不会留下常驻更新进程。

## 安全与性能

- 只处理 DSH 本地凭据文件中的 `DEEPSEEK_API_KEY`；凭据不会提交到 Git，界面和日志使用掩码或脱敏文本。
- 启动器不会上传凭据，也不会默认发送可能产生费用的对话请求。
- 系统指令和插件扫描只在用户打开对应页面并触发刷新时执行，启动时不会进行全盘扫描。
- 后台刷新间隔设有下限，托盘运行时不会维持高频轮询。
- 启动器更新默认每天最多检查一次，下载前需要用户确认，并且只接受 GitHub HTTPS Release 资产。
- 更新操作会保护脏工作区，不执行针对用户目录的 `push`、`reset` 或删除命令。
- 启动器不会修改、迁移或删除 DSH 源码、`.dsh`、sessions、插件目录和现有脚本。

## GitHub Release

仓库包含 `.github/workflows/release.yml`。推送 `v*` 标签后，GitHub Actions 会运行回归测试，分别在 Windows/macOS/Linux runner 上构建和打包两套 UI，生成 `SHA256SUMS.txt`，然后创建 Release：

```powershell
git init
git add .
git commit -m "Initial launcher release"
git branch -M main
git remote add origin https://github.com/qichengxiaoqi/dsh-lancher.git
git push -u origin main

git tag v0.2.1
git push origin v0.2.1
```

发布资产包括：

| 资产 | UI / 平台 |
| --- | --- |
| `dsh++.exe` | 保留的 WinForms Windows x64 版本，兼容现有自更新 |
| `dsh++-win-arm64.exe` | WinForms Windows ARM64 版本 |
| `dsh++-avalonia-win-x64.exe` | Avalonia Windows x64 版本 |
| `dsh++-mac-x64.dmg` | Avalonia macOS Intel 版本 |
| `dsh++-mac-arm64.dmg` | Avalonia macOS Apple Silicon 版本 |
| `dsh++-linux-x64.tar.gz` | Avalonia Linux x64 版本 |
| `dsh++-linux-arm64.tar.gz` | Avalonia Linux ARM64 版本 |
| `SHA256SUMS.txt` | 全部资产的 SHA-256 校验值 |

不要手动提交 `publish/`、`bin/`、`obj/`、本地设置或凭据文件。Release 发布文件由工作流重新生成。

## 项目结构

```text
src/
  DshPlusPlus.Core/       配置、自动探测、API、Git、服务和插件核心服务
  DshPlusPlus/            保留的 .NET 9 WinForms Windows 界面
  DshPlusPlus.Avalonia/   .NET 9 跨平台 Avalonia 界面
tests/
  DshPlusPlus.Core.Tests/ 无第三方测试框架的可执行回归测试
docs/
  configuration.md        自动探测和配置说明
  releases/               GitHub Release 公开说明
.github/workflows/
  release.yml             v* 标签 Release 工作流
```

## 许可证

本项目采用 MIT License，详见 [LICENSE](LICENSE)。
