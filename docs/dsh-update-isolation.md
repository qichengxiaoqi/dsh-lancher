# DSH 更新隔离策略

## 目录职责

`dsh++` 将两类更新完全分开：

```text
DSH 官方源码仓库
  upstream/main       官方只读基线
  dsh++-patches       本地 DSH 核心修复提交

dsh++ 用户数据目录
  %LOCALAPPDATA%\dsh++\patches\dsh   补丁队列和说明
  %LOCALAPPDATA%\dsh++\updates\dsh   更新预演工作区
  %LOCALAPPDATA%\dsh++\settings.json 启动器设置
```

插件、`.dsh`、sessions、凭据和用户配置不属于 DSH 源码 Git 更新范围。自定义服务脚本使用更新前备份、成功后删除、失败后恢复并保留备份的策略。

## 推荐 Git 状态

在 DSH 源码目录中配置官方远程，并把核心修改提交到本地补丁分支：

```powershell
cd <DSH_ROOT>
git remote add upstream https://github.com/deepseek-ai/deepseek-harness.git
git switch -c dsh++-patches
git config rerere.enabled true
```

如果当前已经有本地源码修改，先使用 `git add -p` 只选择源码补丁，确认没有凭据、插件缓存和个人配置后再提交。

## 更新行为

- 干净的官方分支：使用 `git pull --ff-only`。
- 干净的 `dsh++-patches` 分支：使用 `git rebase --rebase-merges` 重放本地补丁。
- 有源码修改或未知未跟踪文件：停止更新，不执行清理和覆盖。
- rebase 冲突：执行 `git rebase --abort`，恢复服务脚本，保留备份并继续使用旧版本。
- 插件、sessions 和 `.dsh` 始终不执行 `git clean`、`reset --hard` 或目录删除。

`dsh++` 自身的 GitHub Release 更新不经过 DSH 源码仓库，也不会向 DSH 官方仓库 push。
