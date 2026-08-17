# dsh++ 配置与自动探测

## 配置文件

启动器设置位于：

```text
%LOCALAPPDATA%\dsh++\settings.json
```

该文件只属于当前用户，不应提交到 Git。设置中的 `AutoDetectPaths` 默认值为 `true`：每次启动会重新计算 DSH 和工具路径，不把某台电脑的绝对路径固化到仓库。

在“安装维护”页：

- **自动检测并应用**：保存探测结果，并继续在下次启动时自动探测。
- **验证并保存**：验证当前填写的路径，并将它们保存为手动覆盖。
- **重新载入**：放弃当前编辑框中的未保存修改。

## 探测顺序

探测器只访问以下有限范围，不执行全盘搜索：

1. `DSH_ROOT`、`DSH_HOME` 等环境变量。
2. 启动器目录及最多五层父目录的相邻 DSH 目录。
3. 用户目录下的 `.dsh` 和 `profiles` 一级子目录。
4. Profile `package.json` 中的 `file:` 依赖。
5. PATH 中的命令文件。

DSH 根目录只有在同时存在 `.git` 和 `package.json` 时才会被识别。服务脚本优先使用标准路径 `scripts/windows/DeepSeekHarnessService.ps1`，找不到时只在同一目录查找名称匹配的脚本。

## 环境变量

| 变量 | 默认/用途 |
| --- | --- |
| `DSH_ROOT`、`DEEPSEEK_DSH_ROOT` | 覆盖 DSH 根目录 |
| `DSH_HOME`、`DEEPSEEK_DSH_HOME` | 覆盖 DSH Home |
| `DSH_PROFILE_DIR`、`DSH_PROFILE` | 覆盖 Profile 目录 |
| `DSH_PROFILE_NAME` | Profile 名称，默认 `web` |
| `DSH_SERVICE_SCRIPT` | 覆盖服务脚本 |
| `DSH_PLUGIN_ROOT` | 覆盖插件根目录 |
| `DSH_POWERSHELL`、`POWERSHELL_PATH` | 覆盖 PowerShell |
| `DSH_GIT`、`GIT_EXECUTABLE` | 覆盖 Git |
| `DSH_PNPM`、`PNPM_EXECUTABLE` | 覆盖 pnpm |
| `PNPM_STORE_DIR`、`NPM_CONFIG_STORE_DIR` | 指定 pnpm store；不设置时不传 `--store-dir` |

工具找不到时，启动器会在维护页给出警告；它不会为了探测而启动 pnpm、Git 或 PowerShell。

## 凭据和敏感数据

DeepSeek API Key 继续使用 DSH Home 下的 `.credentials.yaml`。启动器只更新 `DEEPSEEK_API_KEY`，保留其他字段和注释；文件、API Key、Bearer Token 和 Authorization 标头都不应进入 Git。

## 清理旧的本地设置

如果要完全重新探测，可以在启动器关闭后备份并删除当前用户的：

```text
%LOCALAPPDATA%\dsh++\settings.json
```

也可以直接打开“安装维护”并点击“自动检测并应用”。不要删除 `%USERPROFILE%\.dsh`、sessions、插件目录或 DSH 源码。

## 启动器自动更新

启动器更新只面向固定仓库 `qichengxiaoqi/dsh-lancher` 的稳定 GitHub Release，不使用 DSH 源码仓库的 Git 拉取逻辑。默认行为：

- 首次显示窗口后延迟检查一次，默认每 24 小时最多检查一次；
- 只访问 GitHub HTTPS API 和 Release 资产，不上传 API Key、凭据、环境变量或 DSH 内容；
- 只接受名为 `dsh++.exe` 的资产，下载大小上限为 250 MB，并校验 GitHub digest 或本地 SHA-256；
- 发现更新只通知，不自动重启；用户在“启动器设置”确认后才下载并重启；
- 更新过程只替换当前启动器 exe，不修改 DSH 源码、`.dsh`、sessions、插件或用户配置；
- 下载和替换只使用一次性进程，检查失败不会循环重试或留下常驻后台服务。

可以在“启动器设置”关闭自动检查，或将检查间隔调整为 6–168 小时。Release 用户不需要安装 .NET；从源码构建则需要 .NET 9 SDK、Git、PowerShell 和与 DSH 环境匹配的 pnpm。
