# DSH 更新隔离策略

## 目录职责

`dsh++` 将两类更新完全分开：

```text
DSH 官方源码仓库
  upstream/main       官方只读基线
  dsh++-patches       本地 DSH 核心修复提交

dsh++ 用户数据目录
  %LOCALAPPDATA%\dsh++\patches\dsh   补丁队列和说明
  %LOCALAPPDATA%\dsh++\settings.json 启动器设置
```

插件、`.dsh`、sessions、凭据和用户配置不属于 DSH 源码 Git 检查范围。启动器不会修改、备份、删除或覆盖自定义服务脚本。

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

- dsh++ 只读取本地 Git 状态、package 版本，并通过只读 `git fetch` 刷新远程引用，然后比较本地与官方版本。
- 发现官方更新、源码修改、未知未跟踪文件或补丁分支分叉时，只在界面和日志中提醒。
- dsh++ 不执行 `git pull`、`git switch`、`git rebase`、`git rebase --abort`、`git clean`、`reset --hard`、依赖安装、构建、服务停止/启动或 push。
- `dsh++-patches` 的远程、分支和补丁存储配置保持不变；启动器不会切换或修改该本地补丁分支。
- 如需同步 DSH，请用户在 DSH 仓库外部自行备份插件/源码修改后手动处理，dsh++ 继续使用当前版本。

`dsh++` 自身的 GitHub Release 更新不经过 DSH 源码仓库，也不会向 DSH 官方仓库 push。
