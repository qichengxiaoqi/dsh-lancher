# dsh++

`dsh++` 是 DeepSeek Harness 的 Windows 控制台启动器。它把服务启动、停止、重启、Git 更新检查、API 设置、系统指令和插件管理集中到一个低占用的 WinForms 界面中，并支持从 GitHub Release 检查和更新启动器自身。

项目只包含启动器，不包含 DeepSeek Harness 源码、用户会话、插件目录或 API Key。

## 功能

- **DSH 管理**：启动、停止、重启服务，检查 Git 状态，拉取安全更新，打开 Web UI，查看实时日志。
- **安装维护**：查看并验证 DSH、Profile、插件和工具路径；支持自动检测与手动覆盖。
- **DeepSeek API**：安全保存本地 API Key，连接检测，模型列表和余额查询，API Key 始终掩码显示。
- **系统级设置**：扫描 DSH 作用域内的 `AGENTS.md`、`CLAUDE.md`、`settings.yaml` 和 patch 文件，默认只读。
- **插件设置**：读取 Profile、插件 `package.json`、本地 `file:` 依赖和运行时插件状态，支持启用/禁用并生成备份。
- **启动器设置**：主题、强调色、字号缩放、导航栏收缩、刷新间隔和启动页设置。

界面支持 Obsidian 深色主题、浅色主题、高对比度主题、DPI 缩放、弹性布局、图标收缩导航和 Windows 托盘提示。

## 快速开始

### 直接使用 Release

从 GitHub Releases 下载 `dsh++.exe`，双击运行即可。首次启动会自动探测本机 DSH 环境；没有找到完整环境时，打开“安装维护”查看缺失项即可。

## 运行环境与所需工具

### 直接运行 Release

- Windows 10 或 Windows 11，x64
- GitHub Release 下载的 self-contained 单文件不要求安装 .NET
- 若要管理 DSH，需要本机已有 DeepSeek Harness 源码、Profile 和插件目录
- 若要使用服务启动、依赖安装和构建，需要 PowerShell 5.1 或 PowerShell 7、Git 和 pnpm，并确保它们可从 PATH 找到
- 若要使用启动器自动更新，需要能够通过 HTTPS 访问 GitHub API 和 Release 资产

### 从源码构建

要求：Windows 10/11 x64、.NET 9 SDK、Git。DSH 源码和 pnpm 不会被仓库包含，需要在本机另行安装；源码构建不要求固定盘符或固定用户名。

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

发布文件为 `publish\dsh++.exe`。`publish/` 被 Git 忽略，不会进入提交记录。

## 自动路径探测

启动器不会把开发者电脑的盘符、用户名或 Codex 运行时路径写入默认配置。探测顺序如下：

1. 环境变量指定的路径。
2. 启动器目录附近的 `Deepseek-dsh`、`DeepSeek-dsh` 或 `deepseek-harness` 目录，且目录必须同时含 `.git` 和 `package.json`。
3. 当前用户的 `%USERPROFILE%\.dsh`、Profile 和有限范围内的插件目录。
4. Profile 中的本地 `file:` 依赖。
5. PATH 中的 PowerShell、Git 和 pnpm。

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

启动器配置保存于 `%LOCALAPPDATA%\dsh++\settings.json`。默认 `AutoDetectPaths=true`；在“安装维护”中点击“验证并保存”后，当前路径会成为手动覆盖。点击“自动检测并应用”即可恢复跨机器探测。

默认 Web UI 地址是 `http://127.0.0.1:3080`，这是 DeepSeek Harness 的运行时默认地址，不依赖某台电脑的文件路径。

## 启动器自动更新

启动器默认启用低频更新检查：首次显示窗口后延迟检查一次，默认间隔为 24 小时。检查目标是本仓库的稳定 GitHub Release：

`https://github.com/qichengxiaoqi/dsh-lancher`

发现新版本后，启动器只通过托盘通知和“启动器设置”页面提示，不会自动重启或修改 DSH。用户确认后才会下载 `dsh++.exe`，检查文件大小和 SHA-256，并使用一次性的隐藏更新进程替换当前 exe 后重启。更新过程不启动 DSH、不修改源码、`.dsh`、sessions 或插件目录。

可在“启动器设置”中关闭自动检查、调整 6–168 小时的检查间隔，或手动检查和下载更新。网络错误不会循环重试，也不会常驻更新进程。

## 安全与性能

- API Key 只处理 DSH 本地凭据中的 `DEEPSEEK_API_KEY`，不进入 Git；界面和日志使用掩码或脱敏文本。
- 启动器不上传凭据，不默认发送会产生费用的对话请求。
- 系统指令和插件扫描只在用户打开对应页面并触发刷新时执行，不启动时全盘扫描。
- 后台刷新间隔有下限，托盘运行时不维持高频轮询。
- 启动器更新检查每天最多一次，下载前需要用户确认，并限制为 GitHub HTTPS 资产。
- 更新操作保护脏工作区，不执行 push、reset 或删除用户目录。
- 启动器不会修改、迁移或删除 DSH 源码、`.dsh`、sessions、插件目录和现有脚本。

## GitHub Release

仓库包含 `.github/workflows/release.yml`。推送 `v*` 标签后，GitHub Actions 会在 Windows runner 上运行测试、构建 self-contained 单文件并创建 Release：

```powershell
git init
git add .
git commit -m "feat: add GitHub updater and privacy-safe release setup"
git branch -M main
git remote add origin https://github.com/qichengxiaoqi/dsh-lancher.git
git push -u origin main

git tag v0.1.0
git push origin v0.1.0
```

不要手动提交 `publish/`、`bin/`、`obj/`、本地设置或凭据文件。Release 的发布文件由工作流重新生成。

## 项目结构

```text
src/
  DshPlusPlus.Core/       配置、自动探测、API、Git、服务和插件核心服务
  DshPlusPlus/            .NET 9 WinForms 界面
tests/
  DshPlusPlus.Core.Tests/ 无第三方测试框架的可执行回归测试
docs/
  configuration.md        自动探测和配置说明
  superpowers/            设计与实施记录
.github/workflows/
  release.yml             v* 标签 Release 工作流
```

## 许可证

当前仓库尚未指定许可证。公开发布前请根据你的授权意图补充 `LICENSE` 文件。
