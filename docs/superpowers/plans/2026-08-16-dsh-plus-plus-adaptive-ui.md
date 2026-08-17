# dsh++ Adaptive UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 dsh++ WinForms 在中文字体、DPI 和窄窗口下的文字裁切问题，并实现 C 方案自适应指挥台与 B 方案图标收缩导航。

**Architecture:** 新增可测试的 UI 度量和字体回退层，统一向主题管理器、导航按钮和页面控件提供安全尺寸；主窗口通过展开/收缩导航和窄屏阈值适配内容宽度；页面移除关键固定高度和固定列宽，改用首选尺寸、换行、Tooltip 与弹性列。保持现有服务控制、托盘、性能限制和六栏页面不变。

**Tech Stack:** .NET 9 WinForms、C#、System.Drawing、现有 `DshPlusPlus.Core.Tests` 控制台测试入口、Windows 单文件发布。

---

## Task 1: 字体回退与自适应度量基础

**Files:**
- Create: `<launcher-root>\src\DshPlusPlus\UI\Theme\UiMetrics.cs`
- Create: `<launcher-root>\src\DshPlusPlus\UI\Theme\UiFontResolver.cs`
- Modify: `<launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj`
- Modify: `<launcher-root>\tests\DshPlusPlus.Core.Tests\Program.cs`

- [ ] **Step 1: 为纯度量函数写失败测试**

在 `Program.Main` 的现有测试前后加入以下断言，测试公开的纯函数，不依赖窗口句柄：

```csharp
Run("ui metrics clamp font scale", () =>
{
    Assert.Equal(80, UiMetrics.ClampFontScale(10));
    Assert.Equal(140, UiMetrics.ClampFontScale(200));
    Assert.Equal(110, UiMetrics.ClampFontScale(110));
});

Run("ui metrics calculate responsive navigation", () =>
{
    Assert.True(UiMetrics.ShouldCollapseNavigation(960));
    Assert.False(UiMetrics.ShouldCollapseNavigation(1120));
    Assert.Equal(78, UiMetrics.NavigationWidth(true));
    Assert.Equal(224, UiMetrics.NavigationWidth(false));
});

Run("ui font resolver uses available fallback", () =>
{
    Assert.Equal("Microsoft YaHei", UiFontResolver.ChooseAvailableFamily(
        ["Segoe UI", "Microsoft YaHei"],
        "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI"));
    Assert.Equal("Segoe UI", UiFontResolver.ChooseAvailableFamily(
        ["Segoe UI"],
        "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI"));
});
```

导入 `DshPlusPlus.UI.Theme`，并把测试工程调整为 Windows Forms 目标后引用启动器工程：

```xml
<TargetFramework>net9.0-windows</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>
<EnableWindowsTargeting>true</EnableWindowsTargeting>
<ProjectReference Include="..\..\src\DshPlusPlus\DshPlusPlus.csproj" />
```

- [ ] **Step 2: 运行测试确认测试先失败**

运行：

```powershell
dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj
```

预期：编译失败，提示 `UiMetrics` 或 `UiFontResolver` 尚不存在；若测试直接通过，先修正测试名称或断言，不进入实现。

- [ ] **Step 3: 实现最小 UI 度量与字体回退 API**

`UiMetrics` 必须提供以下纯方法，并把所有 DIP 转换结果限制为正整数：

```csharp
public static int ClampFontScale(int value) => Math.Clamp(value, 80, 140);
public static int PixelsFromDip(int dip, int dpi, int fontScale = 100);
public static bool ShouldCollapseNavigation(int clientWidth, int threshold = 1040);
public static int NavigationWidth(bool collapsed, int expanded = 224, int compact = 78);
public static int SafeHeight(float fontHeight, int verticalPadding, int minimumDip, int dpi);
```

`PixelsFromDip` 按 `dip * dpi / 96 * fontScale / 100` 四舍五入；`ShouldCollapseNavigation` 使用 `clientWidth < threshold`；`NavigationWidth` 对输入宽度取 `Math.Max(compact, value)`；`SafeHeight` 返回字体高度加上下内边距与最小 DIP 高度中的较大值。

`UiFontResolver` 必须提供：

```csharp
public static string ChooseAvailableFamily(
    IReadOnlyCollection<string> available,
    params string[] candidates);
public static string ResolveUiFamily();
public static string ResolveMonoFamily();
```

`ResolveUiFamily` 使用 `FontFamily.Families` 读取本机字体名称，候选顺序为 `Microsoft YaHei UI`、`Microsoft YaHei`、`Segoe UI`、`Arial`；全部缺失时返回 `FontFamily.GenericSansSerif.Name`。等宽字体候选为 `Cascadia Mono`、`Consolas`、`Courier New`、Generic Monospace。

- [ ] **Step 4: 运行测试确认基础实现通过**

重新运行上面的 `dotnet run`，预期新增 UI 度量测试和原有测试全部通过。

- [ ] **Step 5: 自审并记录文件**

确认纯度量函数不创建窗口、不启动线程、不访问 DSH 文件；字体枚举只在主题初始化或显式应用时发生。

## Task 2: 统一主题字体、动态控件高度与按钮尺寸

**Files:**
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Theme\ThemeManager.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Theme\ThemePalette.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Controls\GlowButton.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Controls\MetricCard.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Controls\StatusChip.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Controls\LogDrawer.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Pages\PageBase.cs`

- [ ] **Step 1: 先为主题缩放边界补失败测试**

增加测试：

```csharp
Run("ui metrics keep scaled heights safe", () =>
{
    Assert.True(UiMetrics.SafeHeight(24, 10, 36, 96) >= 36);
    Assert.True(UiMetrics.SafeHeight(30, 12, 36, 144) > UiMetrics.SafeHeight(30, 12, 36, 96));
});
```

运行测试，预期在 `SafeHeight` 尚未实现完整时失败或编译失败。

- [ ] **Step 2: 添加主题字体层并让 `FontScale` 生效**

`ThemeManager` 创建并持有 UI、半粗体、标题、小字和等宽字体；`Apply(Control)` 根据 `Tag`（`title`、`section`、`small`、`mono`）选择字体，并递归应用颜色与字体。应用新主题时先替换控件字体，再释放旧字体，避免重复更新导致 GDI 句柄增长。所有动态字号使用 `UiMetrics.ClampFontScale(Settings.FontScale)`。

为 `PageBase` 增加 `Tag = "title"`、`Tag = "small"` 等明确标记，并移除标题 34px、副标题固定高度等会遮挡文本的约束；标题区按标题和副标题的 `PreferredSize` 计算安全高度。

- [ ] **Step 3: 修复通用控件的固定高度**

`GlowButton` 使用 `AutoSize = true`、`MinimumSize` 和按当前字体高度计算的 `Height`，保留 `MaximumSize` 防止极长文本撑破布局；按钮文本不得通过截断隐藏。

`MetricCard` 的 caption/value 改为 `AutoSize = true` 和 `MinimumSize`，detail 使用 `AutoEllipsis` + Tooltip；`StatusChip` 增加动态 Padding 和最小高度；`LogDrawer` 标记为 `mono` 并使用可用等宽字体。

- [ ] **Step 4: 运行全部测试和 Release 构建**

运行：

```powershell
dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj
dotnet build <launcher-root>\DshPlusPlus.sln -c Release --no-restore
```

预期全部测试通过，构建 0 警告、0 错误。

## Task 3: 实现图标收缩导航与主窗口自适应

**Files:**
- Create: `<launcher-root>\src\DshPlusPlus\UI\Controls\NavigationButton.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\MainForm.cs`
- Modify: `<launcher-root>\src\DshPlusPlus.Core\Models\LauncherSettings.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Pages\LauncherSettingsPage.cs`

- [ ] **Step 1: 增加导航布局状态失败测试**

加入：

```csharp
Run("navigation compact mode preserves accessible titles", () =>
{
    var item = NavigationItem.Create("系统级设置", "04");
    Assert.Equal("系统级设置", item.AccessibleName);
    Assert.Equal("04", item.Index);
});
```

为 `NavigationItem` 放在 `NavigationButton.cs` 的公开不可变记录中，先运行确认类型不存在。

- [ ] **Step 2: 实现可收缩导航按钮**

`NavigationButton` 负责标题、索引、图标种类、展开/收缩状态和 Tooltip，不再把标题与索引拼接成一段固定字符串。`OnPaint` 使用 `Graphics` 绘制简单几何图标与激活态强调线，避免 emoji/符号字体缺失；收缩态隐藏标题但保留 `AccessibleName`、`AccessibleDescription` 和 Tooltip。

- [ ] **Step 3: 接入 `MainForm` 的展开/收缩和自动响应**

增加 `_navigationCollapsed`、`_navigationToggle`、`ToolTip` 字段和 `ToggleNavigation()`、`ApplyNavigationMode(bool)`、`HandleResponsiveLayout()` 方法：

- 展开宽度使用 `UiMetrics.NavigationWidth(false, settings.Theme.NavigationWidth, 78)`。
- 收缩宽度固定为 `UiMetrics.NavigationWidth(true, ..., 78)`。
- `Resize` 只在阈值跨越时切换，避免每个像素触发布局重算。
- 品牌区按钮使用“收缩/展开” Tooltip；最小窗口自动进入收缩态。
- 页面内容 host 保持 `DockStyle.Fill`，不重新创建页面、不触发扫描。
- `SaveSettingsAsync` 更新展开宽度后重新应用当前导航模式。

- [ ] **Step 4: 将导航状态写入启动器设置**

在 `ThemeSettings` 增加 `NavigationCollapsed` 和 `AutoCollapseNavigation`，旧 JSON 缺失时分别默认 `false`、`true`。启动器设置页增加两个可读的复选框；保存后立即应用，不触发 DSH 扫描。

- [ ] **Step 5: 运行测试、构建并做一次启动冒烟**

运行全部测试和 Release 构建；启动发布前的 Debug/Release exe，只检查主窗口创建、六个导航按钮存在、收缩/展开不抛异常，退出前确保进程关闭或回托盘，不点击系统/插件扫描。

## Task 4: 页面弹性布局与长文本可见性

**Files:**
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Pages\DshManagementPage.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Pages\MaintenancePage.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Pages\DeepSeekApiPage.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Pages\SystemSettingsPage.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Pages\PluginSettingsPage.cs`
- Modify: `<launcher-root>\src\DshPlusPlus\UI\Pages\LauncherSettingsPage.cs`

- [ ] **Step 1: 为弹性列和文本换行补失败测试**

增加纯配置断言，验证页面列策略不允许负宽度，并验证超长字段使用 Tooltip/省略策略的辅助函数（若实现为 `UiText.Truncate`，先写输入为空、短文本和长文本三条断言）。先运行确认辅助 API 不存在或断言失败。

- [ ] **Step 2: 修复 DSH 管理页的标题、状态和操作区**

状态芯片使用 `FlowLayoutPanel` 的内容尺寸；按钮不再固定 112px；操作区在窄宽度下允许换行；日志标题和实时日志控件保持可见。指标卡使用弹性列，长状态通过 Tooltip 展示完整文本。

- [ ] **Step 3: 修复维护/API 页面输入与按钮行**

维护页路径输入列使用百分比宽度，浏览/打开按钮使用最小宽度；API Key、GitHub 和控制台按钮使用可伸缩 FlowLayout，窄屏允许第二行；API 结果卡的标题/余额/延迟文字改为首选高度。

- [ ] **Step 4: 修复系统/插件页面的表格与预览**

系统指令列表设置可见列的 `AutoSizeMode`、最小宽度和长路径 Tooltip；预览文本使用 mono 字体并允许滚动。插件表格开启合理的 `AutoSizeRowsMode`、文本换行和 Fill 列，名称、来源路径、profile 在窄屏下不覆盖开关按钮。

- [ ] **Step 5: 修复启动器设置页并联动 FontScale**

设置页的 FontScale、导航宽度和密度控件使用自适应行高；保存后调用主题管理器重新应用字体，所有页面即时更新，不重启、不扫描。

- [ ] **Step 6: 运行测试和 Release 构建**

预期全部测试通过、构建 0 警告/0 错误；只读检查没有修改 `<dsh-root>`。

## Task 5: 多 DPI GUI 验证、发布与回归

**Files:**
- Modify only if validation exposes a regression: files from Tasks 1–4
- Publish: `<launcher-root>\publish\dsh++.exe`

- [ ] **Step 1: 构建 Debug/Release 并检查产物**

```powershell
dotnet build <launcher-root>\DshPlusPlus.sln -c Release --no-restore
dotnet run --project <launcher-root>\tests\DshPlusPlus.Core.Tests\DshPlusPlus.Core.Tests.csproj
```

- [ ] **Step 2: 做 GUI 尺寸与导航冒烟检查**

用 Windows GUI 检查以下组合：

1. 1120×720 展开态：六栏文字完整、标题/按钮无裁切。
2. 960×640：导航自动收缩为图标，Tooltip 能显示完整栏目名。
3. 125%/150% DPI：标题、按钮、状态芯片和表格行不重叠。
4. 手动收缩/展开：页面不重新扫描，托盘和左下角状态芯片保持可见。
5. 逐页切换六栏，重点检查 API、系统、插件中的长文本。

遇到任何遮挡先记录控件和窗口尺寸，修改单一布局约束后重新验证，不通过增加后台刷新或降低字体可读性解决。

- [ ] **Step 3: 进行最终单文件发布**

```powershell
dotnet publish <launcher-root>\src\DshPlusPlus\DshPlusPlus.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o <launcher-root>\publish
```

- [ ] **Step 4: 验证发布产物和进程卫生**

确认 `<launcher-root>\publish\dsh++.exe` 存在；启动后只出现一个 dsh++ 进程；关闭窗口进入托盘并停止 UI 定时器；通过托盘“退出”后进程退出。不得启动系统/插件全盘扫描作为发布验证。

- [ ] **Step 5: 完成自审与交付**

记录测试、构建、发布结果和 GUI 检查组合；交付 exe、规格和计划文件路径，明确未修改 DSH 源码。
