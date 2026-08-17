# dsh++ — A Lightweight Launcher for DeepSeek Harness

English | [简体中文](README.zh-CN.md)

`dsh++` is a lightweight Windows launcher and management console for [DeepSeek Harness (DSH)](https://github.com/deepseek-ai/deepseek-harness). It brings service control, Git maintenance, API settings, system instructions, plugin inventory, and launcher customization into one responsive .NET 9 WinForms application.

The repository contains the launcher only. It does not include DeepSeek Harness source code, user sessions, plugin folders, credentials, or API keys.

> This project was created with Vibe Coding. Its goal is to provide a convenient launcher entry point for DSH while helping everyone save tokens. If you have the time and energy, feel free to improve the project—thank you very much!

## Features

- **DSH management**: start, stop, and restart the service; inspect Git status; pull safe updates; open the Web UI; and view live logs.
- **Installation and maintenance**: inspect and validate DSH, Profile, plugin, and tool paths with automatic discovery and manual overrides.
- **DeepSeek API**: save the local API key securely, test connectivity, list models, and query the current balance. API keys are always masked in the UI.
- **System settings**: scan DSH-scoped `AGENTS.md`, `CLAUDE.md`, `settings.yaml`, and patch files in read-only mode by default.
- **Plugin settings**: inspect Profile manifests, plugin `package.json` files, local `file:` dependencies, and runtime plugin state; enable or disable plugins with backups.
- **Launcher settings**: customize theme, accent color, font scaling, collapsible navigation, refresh intervals, and the startup page.

The interface includes Obsidian dark, light, and high-contrast themes, DPI-aware responsive layout, a collapsible icon navigation rail, and Windows tray notifications.

## Quick start

### Use a GitHub Release

Download `dsh++.exe` from [GitHub Releases](https://github.com/qichengxiaoqi/dsh-lancher/releases) and run it. On first launch, the application automatically discovers the local DSH environment. If the environment is incomplete, open **Installation and maintenance** to see the missing items.

### Build from source

From the repository root:

```powershell
dotnet restore .\DshPlusPlus.sln
dotnet run --project .\src\DshPlusPlus\DshPlusPlus.csproj
```

Run the core regression tests and build a self-contained Windows executable:

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

The published file is `publish\dsh++.exe`. The `publish/` directory is ignored by Git and is not included in commits.

## Runtime requirements and tools

### Running a Release build

- Windows 10 or Windows 11, x64.
- The self-contained executable downloaded from GitHub Releases does not require a separate .NET installation.
- A local DeepSeek Harness source tree, Profile, and plugin directory are required for DSH management features.
- PowerShell 5.1 or PowerShell 7, Git, and pnpm must be available on `PATH` for service management, dependency installation, and build-related operations.
- HTTPS access to the GitHub API and release assets is required for launcher self-update checks.

### Building from source

Windows 10/11 x64, the .NET 9 SDK, and Git are required. The DSH source tree and pnpm are not included in this repository and must be installed separately. The project does not depend on a fixed drive letter or a fixed Windows user name.

## Automatic path discovery

The launcher does not write a developer's drive letter, user name, or Codex runtime path into its default configuration. Discovery uses the following order:

1. Paths supplied through environment variables.
2. `Deepseek-dsh`, `DeepSeek-dsh`, or `deepseek-harness` folders near the launcher, provided that the folder contains both `.git` and `package.json`.
3. The current user's `%USERPROFILE%\.dsh`, Profile locations, and a limited plugin search scope.
4. Local `file:` dependencies declared by the Profile.
5. PowerShell, Git, and pnpm available on `PATH`.

Optional environment variables:

| Variable | Purpose |
| --- | --- |
| `DSH_ROOT` / `DEEPSEEK_DSH_ROOT` | DSH source root |
| `DSH_HOME` / `DEEPSEEK_DSH_HOME` | DSH home directory |
| `DSH_PROFILE_DIR` / `DSH_PROFILE` | Profile directory |
| `DSH_PROFILE_NAME` | Profile name; defaults to `web` |
| `DSH_SERVICE_SCRIPT` | Service PowerShell script |
| `DSH_PLUGIN_ROOT` | Plugin root directory |
| `DSH_POWERSHELL` | PowerShell executable |
| `DSH_GIT` | Git executable |
| `DSH_PNPM` | pnpm executable |
| `PNPM_STORE_DIR` / `NPM_CONFIG_STORE_DIR` | Optional pnpm store; empty means pnpm's own default |

Launcher settings are stored at `%LOCALAPPDATA%\dsh++\settings.json`. `AutoDetectPaths` is enabled by default. Clicking **Validate and save** in **Installation and maintenance** creates a manual override; clicking **Detect and apply automatically** restores portable discovery.

The default Web UI address is `http://127.0.0.1:3080`, which is the DSH runtime default and is not tied to a local file path.

## Launcher self-update

The launcher performs a low-frequency update check by default: one delayed check after the main window is shown, then once every 24 hours. It checks stable GitHub Releases from:

`https://github.com/qichengxiaoqi/dsh-lancher`

When a new version is found, the launcher only reports it through a tray notification and the **Launcher settings** page. It never restarts or modifies DSH automatically. After the user confirms, it downloads `dsh++.exe`, validates the file size and SHA-256 digest, and uses a one-time hidden updater process to replace and restart the launcher.

The update process does not start DSH and does not modify the source tree, `.dsh`, sessions, or plugin directories. Automatic checks can be disabled, the interval can be changed from 6 to 168 hours, and checks or downloads can be started manually from **Launcher settings**. Network errors do not trigger tight retry loops or leave a permanent updater process running.

## Security and performance

- Only `DEEPSEEK_API_KEY` is handled from the DSH local credential file. Credentials are not committed to Git, and the UI and logs use masking or redaction.
- The launcher does not upload credentials and does not send billable conversation requests by default.
- System instruction and plugin scans run only when the corresponding page is opened and refreshed; the launcher does not perform a full-disk scan at startup.
- Background refresh intervals have lower bounds, and tray mode does not maintain high-frequency polling.
- Launcher update checks run at most once per day by default, require confirmation before downloading, and accept only GitHub HTTPS release assets.
- Update operations protect dirty worktrees and do not run `push`, `reset`, or deletion commands against user directories.
- The launcher does not modify, migrate, or delete the DSH source tree, `.dsh`, sessions, plugin directories, or existing scripts.

## GitHub Releases

`.github/workflows/release.yml` builds releases from `v*` tags. GitHub Actions runs the tests, builds a self-contained single-file executable on a Windows runner, and creates a Release:

```powershell
git init
git add .
git commit -m "Initial launcher release"
git branch -M main
git remote add origin https://github.com/qichengxiaoqi/dsh-lancher.git
git push -u origin main

git tag v0.1.0
git push origin v0.1.0
```

Do not commit `publish/`, `bin/`, `obj/`, local settings, or credential files. Release artifacts are rebuilt by the workflow.

## Project structure

```text
src/
  DshPlusPlus.Core/       Configuration, discovery, API, Git, service, and plugin services
  DshPlusPlus/            .NET 9 WinForms user interface
tests/
  DshPlusPlus.Core.Tests/ Executable regression tests without a third-party test framework
docs/
  configuration.md        Automatic discovery and configuration notes
  releases/               Public GitHub Release notes
.github/workflows/
  release.yml             Release workflow for v* tags
```

## License

This repository does not currently specify a license. Add a `LICENSE` file before public distribution according to your intended terms.
