# 预览窗位置模式 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 预览窗支持三种可选位置（右侧上下两行 / 右侧标签页 / 底部通栏），位置按钮放在预览区内，窗格与详情尺寸可拖、可记忆。

**架构：** `PreviewPaneLayout` 设置驱动主窗口状态与 InfoPane 朝向；两条分隔条叠放在现有详情区的边缘（不新增行列，避免动所有跨度）；非标签页模式强制详情可见并走真实预览。

**技术栈：** WinUI 3 / C# / XAML / 现有设置服务与 VisualState

**规格：** `docs/superpowers/specs/2026-11-10-preview-pane-placement-design.md`（连同 `docs/superpowers/specs/assets/preview-pane/prototype.html` 一起读）

## 全局约束

- 平台/配置：`x64` + `Debug`，构建命令加 `-v:quiet -clp:ErrorsOnly`；msbuild 不在 PATH 时用 AGENTS.md 里的 DevShell 包装命令。
- 本仓库没有适合 AI 代理的 UI 测试：**验证 = 编译通过 + 手工冒烟**；不要引入测试框架或控件层测试。
- 文本文件 CRLF；`git diff --check` 干净；用户可见字符串走 `helpers:ResourceString`。
- 不新增 P/Invoke / COM / CsWin32 声明。
- 三个检查点各一个 commit（见任务 1–3）；提交前 `git status --short` 与 `git diff --check`。
- 工作分支：`preview-pane-placement`。
- 版本号不要自行提升（内部迭代需维护者确认）。

## 审查重点（Review Focus）

| # | 输入 / 条件 | 期望行为 | 钉住它的检查 |
| --- | --- | --- | --- |
| 1 | 标签页模式选过"预览"页后切到上下两行 / 底部，或带着该设置重启 | 详情仍然可见（不被页签状态设成 0 高） | 任务 1 步骤 9 |
| 2 | 快速连续切换选中项 | 预览不串台，最终显示最后一个文件 | 任务 2 步骤 6 |
| 3 | 无选中 / 多选 / 文件夹 | 空状态与现有行为一致，不崩溃 | 任务 2 步骤 6 |
| 4 | .lnk、可浏览 zip、回收站非文件夹项、无处理器文件 | 仍走现有基础预览，不报错 | 任务 2 步骤 6 |
| 5 | 详情列拖到 132 px | 内容换行、不横向溢出；再拖不突破 132 | 任务 2 步骤 6 |
| 6 | 上下两行 ↔ 底部来回切 | 都是真预览，**不重复加载** | 任务 2 步骤 6 |

## 文件结构

| 文件 | 职责 |
| --- | --- |
| `src/Files.App/Data/Enums/PreviewPaneLayout.cs`（新建） | 三种位置的枚举 |
| `src/Files.App/Data/Contracts/IInfoPaneSettingsService.cs` | 三个新设置项 |
| `src/Files.App/Services/Settings/InfoPaneSettingsService.cs` | 默认值/范围；`VerticalSizePx` 默认值 250→300 |
| `src/Files.App/Services/Settings/UserSettingsService.cs` | 枚举的 JSON 序列化注册 |
| `src/Files.App.Controls/ThemedIcon/Styles/Icons.Panel.xaml` | "标签页"位置图标 |
| `src/Files.App/Strings/en-US/Resources.resw` | 三个按钮文案 |
| `src/Files.App/UserControls/Pane/InfoPane.xaml` | 顶栏按钮、两条叠放式分隔条、详情列 `MinWidth=132`、`LoadingPreview` 不再藏详情、笔误修正 |
| `src/Files.App/UserControls/Pane/InfoPane.xaml.cs` | `ApplyLayout`（位置、尺寸、分隔条、光标）、分隔条写回、删除 `PreviewPanePositions`/`Position` |
| `src/Files.App/ViewModels/UserControls/InfoPaneViewModel.cs` | 真实预览条件、Layout 变化时只在"基础预览 ↔ 真预览"切换时刷新 |
| `src/Files.App/Views/MainPage.xaml.cs` | 按 `Layout` 切状态、右侧宽度 `Max(300, VerticalSizePx)`、监听 `Layout` |
| `src/Files.App/Actions/Show/TogglePreviewPaneAction.cs`、`ToggleDetailsPaneAction.cs` | 非标签页模式下不响应（防止把详情切走） |

---

### 任务 1：位置设置、顶栏按钮与模式切换

**文件：**
- 创建：`src/Files.App/Data/Enums/PreviewPaneLayout.cs`
- 修改：`src/Files.App/Data/Contracts/IInfoPaneSettingsService.cs`
- 修改：`src/Files.App/Services/Settings/InfoPaneSettingsService.cs`
- 修改：`src/Files.App/Services/Settings/UserSettingsService.cs`
- 修改：`src/Files.App.Controls/ThemedIcon/Styles/Icons.Panel.xaml`
- 修改：`src/Files.App/Strings/en-US/Resources.resw`
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml`
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml.cs`
- 修改：`src/Files.App/Views/MainPage.xaml.cs`

- [ ] **步骤 1：枚举**

```csharp
namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines constants that specify where the Info Pane is placed.
	/// </summary>
	public enum PreviewPaneLayout
	{
		RightSplit,
		RightTabs,
		Bottom,
	}
}
```

- [ ] **步骤 2：设置契约与实现**

`IInfoPaneSettingsService` 加 `PreviewPaneLayout Layout`、`double SplitRatio`、`double BottomDetailsWidthPx`；`InfoPaneSettingsService` 实现：

```csharp
public PreviewPaneLayout Layout { get => Get(PreviewPaneLayout.RightSplit); set => Set(value); }
public double SplitRatio { get => Math.Clamp(Get(0.42d), 0.22d, 0.72d); set => Set(Math.Clamp(value, 0.22d, 0.72d)); }
public double BottomDetailsWidthPx { get => Math.Max(132d, Get(300d)); set => Set(Math.Max(132d, value)); }
```

同时把 `VerticalSizePx` 默认值从 `250d` 改成 `300d`。`UserSettingsService` 的 `[JsonSerializable]` 列表加 `[JsonSerializable(typeof(PreviewPaneLayout))]`。

- [ ] **步骤 3：三条文案**

`en-US/Resources.resw` 加 `PreviewPaneLayoutRightSplit` / `PreviewPaneLayoutBottom` / `PreviewPaneLayoutRightTabs`（英文：`Right, preview above details` / `Below the file list` / `Right, as tabs`）。任务内就要能编译，所以文案与按钮同任务。

- [ ] **步骤 4：标签页位置图标**

`Icons.Panel.xaml` 加 `App.ThemedIcons.PanelTabs`（照 `PanelRight` 的写法：`IconSize=16`、`OutlineIconData`、`FilledIconData`；一个带两个页签的矩形即可，不需要 Layers）。

- [ ] **步骤 5：顶栏按钮**

`InfoPane.xaml` 顶栏（`Grid.Row=0`）在胶囊之后加一个右对齐横向 `StackPanel`，三个 `ToggleButton`：

- `Width=24 Height=24 Padding=0`，内容 `controls:ThemedIcon`，图标分别用 `PanelRight` / `PanelBottom` / `PanelTabs`。
- 顺序：上下两行、底部、标签页；`Tag` 存 `RightSplit` / `Bottom` / `RightTabs`。
- `AutomationProperties.Name` 与 `ToolTipService.ToolTip` 用 `helpers:ResourceString`。
- 三个按钮共用一个 `Click` 处理方法：写 `PaneSettingsService.Layout`。

- [ ] **步骤 6：InfoPane 应用布局**

`InfoPane.xaml.cs`：

- 删除 `PreviewPanePositions` 枚举与 `Position` 属性；`UpdatePosition` 改为 `ApplyLayout(PreviewPaneLayout layout)`。
- 进入前先兜住页签状态：`if (layout is not PreviewPaneLayout.RightTabs && ViewModel.SelectedTab != InfoPaneTabs.Details) ViewModel.SelectedTab = InfoPaneTabs.Details;`（否则页签视觉状态会把详情设成 0 高）。
- `RightSplit`：`MinWidth=300`、`MinHeight=0`、`GoToState("Vertical")`、胶囊 `Collapsed`。
- `RightTabs`：`MinWidth=300`、`MinHeight=0`、`Vertical`、胶囊 `Visible`。
- `Bottom`：`MinWidth=0`、`MinHeight=140`、`GoToState("Horizontal")`、胶囊 `Collapsed`。
- 同步三个 `ToggleButton` 的 `IsChecked`（只有当前模式为 true）。

- [ ] **步骤 7：MainPage 按设置切状态**

`MainPage.UpdatePositioning()` 改为读 `Layout` 调用 `ApplyLayout`，`RightSplit`/`RightTabs` 时 `InfoPaneColumnDefinition.Width = new(Math.Max(300d, VerticalSizePx))`，`Bottom` 时沿用 `HorizontalSizePx`。`Page_SizeChanged` 里凡是引用 `InfoPane.Position` 的地方改成判断 `Layout`（不要改 `ContentColumn`/`ContentRow` 的比较对象，那两处是主内容区的最小宽高）。`UserSettingsService_OnSettingChangedEvent` 增加 `case nameof(IInfoPaneSettingsService.Layout)` → `UpdatePositioning()`。

- [ ] **步骤 8：编译**

```powershell
pwsh.exe -NoProfile -Command "& { Import-Module 'C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\Tools\Microsoft.VisualStudio.DevShell.dll'; Enter-VsDevShell 1ba2cc4e -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'; msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly }"
```

预期：无错误无警告。

- [ ] **步骤 9：手工冒烟（对应审查重点 1）**

打开预览窗，切三种模式：右侧上下两行 / 右侧标签页（出现胶囊）/ 底部；高亮跟随；关掉重开、重启应用后保留。**先在标签页模式切到"预览"页，再切到上下两行**，确认详情仍然可见。

- [ ] **步骤 10：Commit**

```powershell
git add src/Files.App/Data/Enums/PreviewPaneLayout.cs src/Files.App/Data/Contracts/IInfoPaneSettingsService.cs src/Files.App/Services/Settings/InfoPaneSettingsService.cs src/Files.App/Services/Settings/UserSettingsService.cs src/Files.App.Controls/ThemedIcon/Styles/Icons.Panel.xaml src/Files.App/Strings/en-US/Resources.resw src/Files.App/UserControls/Pane/InfoPane.xaml src/Files.App/UserControls/Pane/InfoPane.xaml.cs src/Files.App/Views/MainPage.xaml.cs
git commit -m "Add preview pane layout buttons and mode switching"
```

---

### 任务 2：分隔条、详情可见性与预览刷新

**文件：**
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml`
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml.cs`
- 修改：`src/Files.App/ViewModels/UserControls/InfoPaneViewModel.cs`
- 修改：`src/Files.App/Actions/Show/TogglePreviewPaneAction.cs`
- 修改：`src/Files.App/Actions/Show/ToggleDetailsPaneAction.cs`

- [ ] **步骤 1：引入分隔条样式与两条分隔条（叠放式，不新增行列）**

`InfoPane.xaml` 的 `UserControl.Resources` 合并 `ms-appx:///Styles/DefaultGridSplitterStyle.xaml`，然后加两条分隔条（均 `Style="{StaticResource InvisibleGridSplitterStyle}"`，初始 `Collapsed`，`IsTabStop="False"`）：

- 行分隔条 `RowSplitter`：放在详情区的行、列跨 2、`VerticalAlignment="Top"`、`Height=8`、`ResizeDirection="Rows"`、`ResizeBehavior="PreviousAndCurrent"`。
- 列分隔条 `ColumnSplitter`：`Grid.Row=1 Grid.RowSpan=2 Grid.Column=1`、`HorizontalAlignment="Left"`、`Width=8`、`ResizeDirection="Columns"`、`ResizeBehavior="PreviousAndCurrent"`。

- [ ] **步骤 2：详情列的 MinWidth 与内边距**

`PropertiesColumn` 加 `MinWidth="132"`。行分隔条出现时详情区 `Margin="0,8,0,0"`，列分隔条出现时 `Margin="8,0,0,0"`——由 `ApplyLayout` 按模式设置，不写进状态。

- [ ] **步骤 3：LoadingPreview 不再藏详情**

删除 `LoadingPreview` 视觉状态里隐藏 `FileDetailsRepeater`、`DetailsTagsList`、`DetailsOpenProperties` 的三个 Setter。其它状态保持原样。

- [ ] **步骤 4：ApplyLayout 管理分隔条与尺寸**

`ApplyLayout` 增加：

- `RightSplit`：`RowSplitter.Visible`、`ColumnSplitter.Collapsed`；`PreviewRow.Height=SplitRatio`、`PropertiesRow.Height=1-SplitRatio`（星号），详情 `Margin="0,8,0,0"`；行分隔条光标 `SizeNorthSouth`。
- `RightTabs`：两条都 `Collapsed`；`PreviewRow=2*`、`PropertiesRow=3*`，详情 `Margin="0"`。
- `Bottom`：`ColumnSplitter.Visible`、`RowSplitter.Collapsed`；`PropertiesColumn.Width = new(Math.Max(132d, PaneSettingsService.BottomDetailsWidthPx))`（不做百分比截断），详情 `Margin="8,0,0,0"`；列分隔条光标 `SizeWestEast`。
- 两条分隔条的 `ManipulationCompleted` 分别写回：行分隔条按 `PreviewRow.ActualHeight / (PreviewRow+PropertiesRow)` 算比例、夹紧后再设回星号；列分隔条把 `Math.Max(132, PropertiesColumn.ActualWidth)` 写回设置并重新设列宽。

- [ ] **步骤 5：预览加载规则与刷新策略**

`InfoPaneViewModel`：

- `UpdateSelectedItemPreviewCoreAsync` 的真实预览条件加上 `infoPaneSettingsService.Layout is not PreviewPaneLayout.RightTabs`。
- 在设置变化处理里比较"是否使用基础预览"：`layout == RightTabs && SelectedTab == Details`。只有这个布尔翻转时才调用 `UpdateSelectedItemPreviewAsync()`；上下两行 ↔ 底部之间不刷新。
- 保留现有取消与版本号机制。

- [ ] **步骤 6：非标签页模式屏蔽页签切换**

`TogglePreviewPaneAction` 与 `ToggleDetailsPaneAction` 的 `ExecuteAsync` 开头，如果 `Layout is not RightTabs` 直接返回（避免把详情切走）。两个 Action 通过 `Ioc.Default.GetRequiredService<IInfoPaneSettingsService>()` 取设置。

- [ ] **步骤 7：编译**

同任务 1 步骤 8 的命令。

- [ ] **步骤 8：手工冒烟（对应审查重点 2–6）**

- 上下两行拖分隔条：比例变化并保存，重启保留；范围停在 0.22–0.72。
- 底部：拖上沿改高度、拖竖条改详情列宽，都能保存；拖到 132 停住、内容换行不横向溢出。
- 标签页：无分隔条，详情/预览 2:3 不受上一次比例影响。
- 上下两行 ↔ 底部来回切：预览不闪、不重复加载。
- 图片 / PDF / txt / 文件夹 / 多选 / 无选中 / .lnk / 可浏览 zip / 回收站项：显示与改动前一致（.lnk 等仍为基础预览）。
- 快速连点多个文件：预览最终显示最后一个。

- [ ] **步骤 9：Commit**

```powershell
git add src/Files.App/UserControls/Pane/InfoPane.xaml src/Files.App/UserControls/Pane/InfoPane.xaml.cs src/Files.App/ViewModels/UserControls/InfoPaneViewModel.cs src/Files.App/Actions/Show/TogglePreviewPaneAction.cs src/Files.App/Actions/Show/ToggleDetailsPaneAction.cs
git commit -m "Add resizable splitters and preview loading rules"
```

---

### 任务 3：整体验证与打包

- [ ] **步骤 1：修掉 XAML 笔误**

`InfoPane.xaml` 的 `Vertical` 状态里 `Target="RootPropertiesScrollViewer.Column"` 改成 `Target="RootPropertiesScrollViewer.(Grid.Column)"`（只此一处，任务 1/2 不要重复修）。

- [ ] **步骤 2：整份解决方案构建**

```powershell
pwsh.exe -NoProfile -Command "& { Import-Module 'C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\Tools\Microsoft.VisualStudio.DevShell.dll'; Enter-VsDevShell 1ba2cc4e -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'; msbuild -restore Files.slnx -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly }"
```

- [ ] **步骤 3：工作树干净**

```powershell
git status --short
git diff --check
```

预期：无未提交改动（打包前必须干净，否则 `RELEASE-INFO.txt` 记录不到本次改动）。

- [ ] **步骤 4：打包**

用 `.github/scripts/Build-Internal.ps1 -PortableOnly`（本次不需要签名 MSIX；如需签名包去掉该开关）出包。脚本会跑完整单元测试，失败即中止——这是预期行为，不要绕过。报告产物路径，版本号保持不变。

- [ ] **步骤 5：打包产物冒烟**

打开便携包的可执行文件，只走关键几条：三种模式切换、上下两行拖比例、底部拖详情列宽、标签页详情/预览切换、重启后保留。逐条记录结果，未验证项写明原因。
