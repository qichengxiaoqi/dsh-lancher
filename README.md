# dsh++ — A Lightweight Launcher for DeepSeek Harness

English | [简体中文](README.zh-CN.md)

`dsh++` is a lightweight launcher and management console for [DeepSeek Harness (DSH)](https://github.com/deepseek-ai/deepseek-harness). It keeps the existing .NET 9 WinForms edition for Windows and adds a cross-platform Avalonia edition that reuses the same `DshPlusPlus.Core` services.

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

### Cross-platform Avalonia edition

The Avalonia UI is a separate project and does not reference WinForms:

```powershell
dotnet run --project .\src\DshPlusPlus.Avalonia\DshPlusPlus.Avalonia.csproj
dotnet publish .\src\DshPlusPlus.Avalonia\DshPlusPlus.Avalonia.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

For macOS DMG and Linux `tar.gz` packages, use the GitHub Actions workflow. The hosted runners perform the platform-specific publish and packaging steps; a local Windows machine is not expected to build DMG files.

## Runtime requirements and tools

### Running a Release build

- WinForms: Windows 10 or Windows 11, x64 or ARM64.
- Avalonia: Windows x64, macOS Intel/Apple Silicon, or Linux x64/ARM64.
- The self-contained assets downloaded from GitHub Releases do not require a separate .NET installation.
- A local DeepSeek Harness source tree, Profile, and plugin directory are required for DSH management features.
- PowerShell 5.1 or PowerShell 7, Git, and pnpm must be available on `PATH` for service management, dependency installation, and build-related operations.
- HTTPS access to the GitHub API and release assets is required for launcher self-update checks.

### Building from source

The .NET 9 SDK and Git are required. Use the .NET SDK supported by the selected target RID. The DSH source tree and pnpm are not included in this repository and must be installed separately. DMG creation additionally runs on a macOS GitHub Actions runner with `hdiutil`; Linux archives use the runner's `tar` tool. The project does not depend on a fixed drive letter or a fixed user name.

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

`.github/workflows/release.yml` builds releases from `v*` tags. GitHub Actions runs the regression suite, builds both UI editions, packages platform assets on the appropriate hosted runners, creates `SHA256SUMS.txt`, and creates a GitHub Release:

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

The release asset set is:

| Asset | UI / platform |
| --- | --- |
| `dsh++.exe` | Existing WinForms, Windows x64; kept for self-update compatibility |
| `dsh++-win-arm64.exe` | Existing WinForms, Windows ARM64 |
| `dsh++-avalonia-win-x64.exe` | Avalonia, Windows x64 |
| `dsh++-mac-x64.dmg` | Avalonia, macOS Intel |
| `dsh++-mac-arm64.dmg` | Avalonia, macOS Apple Silicon |
| `dsh++-linux-x64.tar.gz` | Avalonia, Linux x64 |
| `dsh++-linux-arm64.tar.gz` | Avalonia, Linux ARM64 |
| `SHA256SUMS.txt` | SHA-256 checksums for all assets |

Do not commit `publish/`, `bin/`, `obj/`, local settings, or credential files. Release artifacts are rebuilt by the workflow.

## Project structure

```text
src/
  DshPlusPlus.Core/       Configuration, discovery, API, Git, service, and plugin services
  DshPlusPlus/            .NET 9 WinForms user interface (preserved Windows edition)
  DshPlusPlus.Avalonia/   .NET 9 cross-platform Avalonia user interface
tests/
  DshPlusPlus.Core.Tests/ Executable regression tests without a third-party test framework
docs/
  configuration.md        Automatic discovery and configuration notes
  releases/               Public GitHub Release notes
.github/workflows/
  release.yml             Release workflow for v* tags
```

## License

Released under the MIT License. See [LICENSE](LICENSE).
