# DSH 启动器设计与开发交接文档

> 状态：设计交接，尚未开始实现
>
> 用户已确认：B「状态面板」布局；更新采用 A「先检查、再单独拉取」。

## 1. 目标

新增一个独立的 Windows EXE 应用，名称为「dsh++」。它用于管理自动探测到的源码版 DeepSeek Harness：

1. 启动 DSH Web 服务；
2. 关闭 DSH Web 服务；
3. 重启 DSH Web 服务；
4. 检查 GitHub 远程仓库是否有新提交或新版本；
5. 用户确认后执行拉取更新、安装依赖、构建并重启。

更新流程只能拉取，禁止执行 git push、强制重置或覆盖用户文件。

## 2. 当前环境与必须保留的内容

- 源码仓库：`<dsh-root>`
- Web 地址：http://127.0.0.1:3080
- 现有服务脚本：`<dsh-root>\scripts\windows\DeepSeekHarnessService.ps1`
- 现有 BAT：
  - `<dsh-root>\启动DeepSeekHarness.bat`
  - `<dsh-root>\关闭DeepSeekHarness.bat`
  - `<dsh-root>\重启DeepSeekHarness.bat`
  - `<dsh-root>\更新DeepSeekHarness.bat`
- 对话记录：`%USERPROFILE%\.dsh`
- 本地插件：`<plugin-root>\dsh-caveman`、`<plugin-root>\dsh-token-billing`
- 插件依赖配置位于：`%USERPROFILE%\.dsh\profiles\web\package.json`

启动器不得删除、迁移或重建 `.dsh`、`<plugin-root>`、会话目录和插件目录。

## 3. 已确认的界面方案：B「状态面板」

窗口建议尺寸约为 720×460，采用左右两栏。

### 左栏：运行状态

- 标题：DSH 启动器
- 状态灯与文字：运行中、已停止、启动中、关闭中、更新中、错误
- 服务地址：http://127.0.0.1:3080
- 端口：3080
- 源码路径：`<dsh-root>`
- 当前源码版本：本地 package.json 版本和短 commit
- 远程状态：最新、可更新、无法连接、工作区有未提交修改

### 右栏：操作区

- 启动服务
- 关闭服务
- 重启服务
- 检查更新
- 检查到更新后，再显示或启用：拉取更新
- 可选的小按钮：打开 WebUI
- 底部日志框：显示当前操作、命令阶段和错误原因

同一时间只能执行一个操作。操作期间禁用其他操作按钮，避免重复点击导致多个服务进程或多个更新进程。

启动成功后保留原有行为：自动打开默认浏览器访问 http://127.0.0.1:3080。第一版不实现托盘、开机自启动和自动更新。

## 4. 实现方案比较

### 方案一：.NET WinForms 单文件 EXE（推荐）

- 使用 .NET 8 Windows Forms；
- 不引入第三方 UI 框架；
- 发布为 win-x64、自包含、单文件 EXE；
- 界面简单，启动快，适合本机工具；
- 可直接调用现有 PowerShell 服务脚本和 Git 命令。

### 方案二：WPF

- 界面定制能力更强；
- 需要更多 XAML 和绑定代码；
- 对当前四个操作来说偏重。

### 方案三：Electron/Tauri

- 更容易制作网页风格界面；
- Electron 体积大，Tauri 需要额外 Rust 工具链；
- 不适合作为当前这个轻量本地控制器的第一版。

采用方案一。

## 5. 服务操作设计

启动器优先复用现有脚本，不重复实现进程树杀进程逻辑：

~~~text
PowerShell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass
  -File <dsh-root>\scripts\windows\DeepSeekHarnessService.ps1
  -Action Start|Stop|Restart|Update
~~~

要求：

- PowerShell 窗口隐藏；
- EXE 捕获退出码、标准输出和标准错误；
- 脚本退出码非零时，界面显示失败，不显示“成功”；
- 关闭和重启必须等待旧进程、端口 3080 和相关进程树真正结束；
- 不允许直接执行 taskkill /IM node.exe，避免误杀其他 Node 应用；
- 启动器退出不应自动关闭正在运行的 DSH 服务。

状态检测可以使用 HTTP 请求和端口检测：

- 端口 3080 有监听且 Web 请求成功：显示“运行中”；
- 无监听：显示“已停止”；
- 有监听但 Web 请求失败：显示“启动异常”；
- 检测过程失败：显示“状态未知”，并把原因写入日志框。

## 6. GitHub 更新设计

采用“检查”和“拉取”分离的流程。

### 6.1 检查更新

点击“检查更新”时：

1. 读取当前分支和 upstream 分支；
2. 执行只更新远程引用的操作：git fetch origin --prune；
3. 不修改工作区文件，不执行 git pull；
4. 比较本地 HEAD 与远程 upstream：
   - behind > 0：显示“发现可用更新”，启用“拉取更新”；
   - behind = 0：显示“已是最新”；
   - ahead > 0：显示“本地有未推送提交”，但仍禁止启动器推送；
   - 工作区有修改：显示“有未提交修改”，暂不允许拉取；
5. 同时显示本地版本、远程版本（从远程 package.json 读取）和短 commit。

远程分支解析顺序：

1. 当前分支的 upstream；
2. origin/main；
3. origin/master。

如果远程地址不是 GitHub、网络失败或仓库状态异常，要显示明确错误，不改变本地源码。

### 6.2 拉取更新

只有用户点击“拉取更新”后执行：

1. 再次检查工作区是否干净；
2. 再次确认远程确实领先；
3. 关闭 DSH 服务；
4. 执行 git pull --ff-only；
5. 执行 pnpm install --store-dir `<plugin-root>\.pnpm-store`；
6. 执行 pnpm run build；
7. 构建成功后启动或重启 DSH 服务；
8. 成功后刷新本地版本和服务状态。

禁止：

- git push；
- git reset --hard；
- git checkout --；
- 自动覆盖用户未提交的源码修改；
- 更新过程中删除 `.dsh`、`<plugin-root>` 或对话记录。

如果拉取、安装或构建失败，应显示失败阶段和错误信息。第一版不自动回滚源码，但必须允许用户重新点击“启动服务”，并保留日志以便排查。

## 7. 建议的项目结构

启动器源码和发布目录放在源码仓库下的独立目录，不与 DSH 的应用包混合：

~~~text
<launcher-root>\
├─ DSH启动器.csproj
├─ Program.cs
├─ MainForm.cs
├─ MainForm.Designer.cs 或等价布局代码
├─ Services\
│  ├─ DshServiceController.cs
│  ├─ GitUpdateChecker.cs
│  └─ ProcessRunner.cs
├─ Models\
│  ├─ DshStatus.cs
│  └─ UpdateStatus.cs
└─ publish\
   └─ DSH启动器.exe
~~~

推荐发布命令：

~~~powershell
dotnet publish <launcher-root>\DshPlusPlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o <launcher-root>\publish
~~~

最终用户入口：

~~~text
<launcher-root>\publish\dsh++.exe
~~~

## 8. 错误处理与安全要求

- 所有外部命令使用固定可执行文件路径和参数数组，避免拼接用户输入；
- 每个操作都有超时，默认服务启动等待不超过 90 秒；
- 操作日志显示时间、阶段、退出码和简短错误；
- 不把 token、对话内容或插件内容写入日志；
- EXE 默认不要求管理员权限；
- F 盘路径不存在时，界面显示路径错误并停止操作；
- 更新前检测 Git 工作区，发现修改时禁止自动覆盖；
- 关闭服务时只匹配 DSH 相关进程和 3080 的 DSH 监听者。

## 9. 测试与验收标准

### 自动化测试

- Git 版本比较：最新、落后、领先、无 upstream；
- 工作区检查：干净、已修改、未跟踪文件；
- GitHub 网络失败和 Git 命令非零退出码；
- 服务操作退出码和超时处理；
- 版本号和短 commit 展示；
- BAT/PowerShell 脚本仍然通过现有回归检查。

### Windows 手动验收

1. 双击 EXE，窗口显示 B 布局；
2. 点击“启动服务”，服务启动，浏览器打开 http://127.0.0.1:3080；
3. 点击“关闭服务”，确认 3080 端口释放且旧 DSH 进程树退出；
4. 点击“重启服务”，确认旧进程被清理、新服务可访问；
5. 点击“检查更新”，最新时显示“已是最新”；
6. 模拟远程领先时，检查只 fetch、不修改工作区，并启用“拉取更新”；
7. 点击“拉取更新”，确认只执行 pull/install/build/restart，不执行 push；
8. 在工作区制造未提交修改，确认拉取按钮被阻止；
9. 确认 `.dsh` 对话记录、`<plugin-root>` 插件和现有 BAT 未被删除或覆盖；
10. 发布目录有可直接双击运行的自包含 EXE。

## 10. 新对话执行提示

新建对话后，将下面内容作为第一条消息，并附上本文件：

~~~text
请读取本文件，按照文档实现 DSH 启动器。先检查 `<dsh-root>` 和现有脚本，再创建实施计划；严格采用 .NET WinForms 单文件 EXE、B 状态面板布局、检查更新与拉取更新分离的设计。执行前不要删除或覆盖 `%USERPROFILE%\.dsh`、`<plugin-root>` 和现有 BAT。实现后进行自动化测试、dotnet publish 和 Windows 手动验收，并把最终 EXE 放到 `<launcher-root>\publish\dsh++.exe`。
~~~

注意：本文件仅用于交接；实际源码和 EXE 位置由项目配置和自动探测结果决定。
