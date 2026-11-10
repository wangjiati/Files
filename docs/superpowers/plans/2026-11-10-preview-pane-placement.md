# 预览窗位置模式 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把预览窗的位置做成可选的三种模式（右侧上下两行 / 右侧标签页 / 底部通栏），并让窗格与详情尺寸可拖、可记忆。

**架构：** 新增 `PreviewPaneLayout` 设置驱动主窗口的位置状态与 InfoPane 的内部朝向；InfoPane 增加顶栏位置按钮与两条分隔条；预览加载按模式选择"真预览"或沿用现有的基础预览。

**技术栈：** WinUI 3 / C# / XAML / 现有 MVVM 与设置服务

**规格：** `docs/superpowers/specs/2026-11-10-preview-pane-placement-design.md`（连同 `docs/superpowers/specs/assets/preview-pane/prototype.html` 一起读）

## 全局约束

- 平台/配置：`x64` + `Debug`；构建命令加 `-v:quiet -clp:ErrorsOnly`。
- 本仓库没有适合 AI 代理的 UI 测试：**验证 = 编译通过 + 手工冒烟**。不要为此引入测试框架或控件层测试。
- 文本文件 CRLF；遵守 `.editorconfig`；用户可见字符串走 `Strings.*` / `helpers:ResourceString`。
- 不新增 P/Invoke、COM、CsWin32 声明；不重写预览加载管线。
- 每个任务一个 commit；提交前 `git status --short` 与 `git diff --check`。
- 工作分支：`preview-pane-placement`。
- 版本号不要自行提升（内部迭代需维护者确认）。

## 审查重点（Review Focus）

| # | 输入 / 条件 | 期望行为 | 钉住它的检查（任务号） |
| --- | --- | --- | --- |
| 1 | 快速连续切换选中项 | 预览不串台，旧请求被取消 | 任务 5 步骤 4 |
| 2 | 没有选中项 / 多选 / 选中文件夹 | 各模式下空状态正确，详情不消失 | 任务 5 步骤 4 |
| 3 | 仅在线云文件、无处理器文件（.lnk、可浏览 zip、回收站项） | 仍按现有基础预览显示，不报错 | 任务 5 步骤 4 |
| 4 | 详情列拖到 132 px | 详情换行不溢出，标签与值都可见 | 任务 4 步骤 4 |
| 5 | 切换模式时预览已加载 / 未加载 | 不重复加载、不残留上一个模式的内容 | 任务 5 步骤 4 |

## 文件结构

| 文件 | 职责 |
| --- | --- |
| `src/Files.App/Data/Enums/PreviewPaneLayout.cs`（新建） | 三种位置的枚举 |
| `src/Files.App/Data/Contracts/IInfoPaneSettingsService.cs` | 三个新设置项契约 |
| `src/Files.App/Services/Settings/InfoPaneSettingsService.cs` | 默认值与范围约束 |
| `src/Files.App/Services/Settings/UserSettingsService.cs` | 枚举的 JSON 序列化注册 |
| `src/Files.App.Controls/ThemedIcon/Styles/Icons.Panel.xaml` | 新增"标签页"位置图标（可选） |
| `src/Files.App/UserControls/Pane/InfoPane.xaml` | 顶栏按钮、分隔条行/列、各模式视觉状态 |
| `src/Files.App/UserControls/Pane/InfoPane.xaml.cs` | 位置应用、分隔条拖动与写回 |
| `src/Files.App/ViewModels/UserControls/InfoPaneViewModel.cs` | 按模式决定真实预览 / 基础预览 |
| `src/Files.App/Views/MainPage.xaml.cs` | 位置状态切换、宽度下限、监听设置 |
| `src/Files.App/Strings/en-US/Resources.resw` | 三个按钮文案 |

---

### 任务 1：枚举与设置项

**文件：**
- 创建：`src/Files.App/Data/Enums/PreviewPaneLayout.cs`
- 修改：`src/Files.App/Data/Contracts/IInfoPaneSettingsService.cs`
- 修改：`src/Files.App/Services/Settings/InfoPaneSettingsService.cs`
- 修改：`src/Files.App/Services/Settings/UserSettingsService.cs`

- [ ] **步骤 1：新建枚举**

```csharp
namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines constants that specify where the Info Pane is placed.
	/// </summary>
	public enum PreviewPaneLayout
	{
		/// <summary>
		/// Right side, preview above details.
		/// </summary>
		RightSplit,

		/// <summary>
		/// Right side, details and preview in tabs.
		/// </summary>
		RightTabs,

		/// <summary>
		/// Below the file list, preview left and details right.
		/// </summary>
		Bottom,
	}
}
```

- [ ] **步骤 2：接口加三个属性**

在 `IInfoPaneSettingsService` 中、`SelectedTab` 之后添加（类型与 XML 注释风格照抄现有成员）：

```csharp
PreviewPaneLayout Layout { get; set; }
double SplitRatio { get; set; }
double BottomDetailsWidthPx { get; set; }
```

- [ ] **步骤 3：实现默认值与约束**

在 `InfoPaneSettingsService` 中：

```csharp
public PreviewPaneLayout Layout
{
	get => Get(PreviewPaneLayout.RightSplit);
	set => Set(value);
}

public double SplitRatio
{
	get => Math.Clamp(Get(0.42d), 0.22d, 0.72d);
	set => Set(Math.Clamp(value, 0.22d, 0.72d));
}

public double BottomDetailsWidthPx
{
	get => Math.Max(132d, Get(300d));
	set => Set(Math.Max(132d, value));
}
```

- [ ] **步骤 4：注册 JSON 序列化**

`UserSettingsJsonSerializationContext` 的 `[JsonSerializable]` 列表中、`InfoPaneTabs` 之后加一行：

```csharp
[JsonSerializable(typeof(PreviewPaneLayout))]
```

- [ ] **步骤 5：编译**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

预期：无错误无警告。

- [ ] **步骤 6：Commit**

```powershell
git add src/Files.App/Data/Enums/PreviewPaneLayout.cs src/Files.App/Data/Contracts/IInfoPaneSettingsService.cs src/Files.App/Services/Settings/InfoPaneSettingsService.cs src/Files.App/Services/Settings/UserSettingsService.cs
git commit -m "Add preview pane layout settings"
```

---

### 任务 2：顶栏位置按钮与模式切换

**文件：**
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml`
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml.cs`
- 修改：`src/Files.App/Views/MainPage.xaml.cs`
- 可选修改：`src/Files.App.Controls/ThemedIcon/Styles/Icons.Panel.xaml`

- [ ] **步骤 1：顶栏加三个按钮**

在 `InfoPane.xaml` 的顶栏 `StackPanel`（"Details / Preview" 胶囊，位于 Grid.Row=0）之后，同一行右对齐再加一个横向 `StackPanel`：

- 三个按钮都是 `Button` + `controls:ThemedIcon`，`Width=28 Height=24`，`Padding=0`，`Background=Transparent`。
- 顺序：上下两行、底部、标签页（标签页最右）。
- 图标：上下两行用 `App.ThemedIcons.PanelRight`，底部用 `App.ThemedIcons.PanelBottom`；标签页需要新图标 `App.ThemedIcons.PanelTabs`（照 `Icons.Panel.xaml` 现有格式加，16 px 的"带两个页签的矩形"；不要求 Layers，只给 `OutlineIconData` 与 `FilledIconData`）。
- 每个按钮设置 `AutomationProperties.Name` 与 `ToolTipService.ToolTip`，文本见任务 6。
- 当前模式高亮：`Tag` 存模式值，由 `InfoPane.xaml.cs` 在应用布局时切换 `Selected` 视觉（或代码里改 `Background`/`Opacity`），保证同时只有一个是高亮。

- [ ] **步骤 2：InfoPane 应用位置**

把 `InfoPane.UpdatePosition(double panelWidth, double panelHeight)` 改为 `ApplyLayout(PreviewPaneLayout layout)`：

- `RightSplit`：`Position = Right`，`MinWidth = 150`，`VisualStateManager.GoToState(this, "Vertical", true)`，胶囊 `Visibility = Collapsed`。
- `RightTabs`：`Position = Right`，`MinWidth = 300`，`Vertical`，胶囊 `Visible`。
- `Bottom`：`Position = Bottom`，`MinHeight = 140`，`Horizontal`，胶囊 `Collapsed`。
- 三个按钮的高亮跟随 `layout`。

`Position` 属性保留（MainPage 仍用它判断当前模式）。

- [ ] **步骤 3：MainPage 按设置切状态**

`MainPage.UpdatePositioning()`：

- 删除"由 `InfoPane.UpdatePosition(width,height)` 推断位置"的调用，改为读 `UserSettingsService.InfoPaneSettingsService.Layout` 并调用 `InfoPane.ApplyLayout(layout)`。
- `RightSplit` / `RightTabs` 分支：`InfoPaneColumnDefinition.Width = new(Math.Max(VerticalSizePx, layout is PreviewPaneLayout.RightTabs ? 300d : 0d))`，`GoToState("InfoPanePositionRight")`。
- `Bottom` 分支：沿用现有 `HorizontalSizePx` 与 `InfoPanePositionBottom`。
- `Page_SizeChanged` 里原先按 `Position` 调整尺寸的逻辑保留，比较的最小值改为对应模式的值（右侧 150 / 标签页 300 / 底部 140）。

- [ ] **步骤 4：监听 Layout 变化**

`UserSettingsService_OnSettingChangedEvent` 增加分支：

```csharp
case nameof(IInfoPaneSettingsService.Layout):
	UpdatePositioning();
	break;
```

- [ ] **步骤 5：编译**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

预期：无错误。

- [ ] **步骤 6：手工冒烟**

启动应用 → 打开预览窗（Ctrl+Alt+I）→ 依次点三个按钮：

- 右侧上下两行：窗格在右，上预览下详情。
- 右侧标签页：窗格在右，出现"详情 / 预览"胶囊；预览页占满。
- 底部通栏：窗格在底部。
- 三个按钮高亮跟随；关掉再打开，模式保留；任务管理里重启应用后仍保留。

- [ ] **步骤 7：Commit**

```powershell
git add src/Files.App/UserControls/Pane/InfoPane.xaml src/Files.App/UserControls/Pane/InfoPane.xaml.cs src/Files.App/Views/MainPage.xaml.cs src/Files.App.Controls/ThemedIcon/Styles/Icons.Panel.xaml
git commit -m "Add preview pane position buttons and layout switching"
```

---

### 任务 3：上下两行的分隔条

**文件：**
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml`
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml.cs`

- [ ] **步骤 1：根 Grid 增加分隔条行**

把根 Grid 的行定义改为 4 行：

```xml
<RowDefinition Height="Auto" />                 <!-- 顶栏 -->
<RowDefinition x:Name="PreviewRow" Height="2*" />
<RowDefinition x:Name="RowSplitterRow" Height="0" />
<RowDefinition x:Name="PropertiesRow" Height="3*" />
```

在行 2 放一个 `controls:GridSplitter`（`x:Name="RowSplitter"`、`Grid.ColumnSpan` 覆盖所有列、`ResizeBehavior="PreviousAndNext"`、`ResizeDirection="Rows"`），`Visibility="Collapsed"` 默认收起。

- [ ] **步骤 2：Vertical 状态设置分隔条**

`Vertical` 视觉状态增加：

- `RowSplitter.Visibility = Visible`
- `RowSplitterRow.Height = 8`
- `RootPreviewGrid.(Grid.Row) = 1`、`RootPropertiesScrollViewer.(Grid.Row) = 3`
- 两者 `Grid.ColumnSpan` 覆盖所有列。
- 顺手把现有 `RootPropertiesScrollViewer.Column` 笔误改为 `(Grid.Column)`（见任务 6 步骤 2）。

`Horizontal` 状态保持两块左右并排（后续任务 4 会再调列），并设 `RowSplitterRow.Height = 0`、`RowSplitter.Visibility = Collapsed`。

- [ ] **步骤 3：应用与保存比例**

`InfoPane.ApplyLayout` 在 `RightSplit` 时按 `SplitRatio` 设置星号高度：

```csharp
double ratio = Math.Clamp(PaneSettingsService.SplitRatio, 0.22d, 0.72d);
PreviewRow.Height = new GridLength(ratio, GridUnitType.Star);
PropertiesRow.Height = new GridLength(1d - ratio, GridUnitType.Star);
```

`RowSplitter` 的 `ManipulationCompleted`（或 `DragCompleted`，以控件实际暴露的事件为准）里计算 `PreviewRow.ActualHeight / (PreviewRow.ActualHeight + PropertiesRow.ActualHeight)` 并写回 `SplitRatio`。

- [ ] **步骤 4：编译与手工验证**

编译通过后启动应用，切到"上下两行"：拖动分隔条，预览与详情高度变化；关掉预览窗再打开、重启应用后比例保留；范围限制在 0.22–0.72。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/UserControls/Pane/InfoPane.xaml src/Files.App/UserControls/Pane/InfoPane.xaml.cs
git commit -m "Add draggable splitter between preview and details"
```

---

### 任务 4：底部模式的详情列宽

**文件：**
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml`
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml.cs`

- [ ] **步骤 1：根 Grid 增加分隔条列**

列定义改为 3 列：

```xml
<ColumnDefinition Width="2*" />                       <!-- 预览 / 详情 -->
<ColumnDefinition x:Name="ColumnSplitterColumn" Width="0" />
<ColumnDefinition x:Name="PropertiesColumn" Width="3*" />
```

在列 1、行 1–3 放第二个 `controls:GridSplitter`（`x:Name="ColumnSplitter"`、`ResizeDirection="Columns"`、`ResizeBehavior="PreviousAndNext"`），默认 `Visibility="Collapsed"`。

- [ ] **步骤 2：Horizontal 状态设置列**

`Horizontal` 状态：

- `ColumnSplitter.Visibility = Visible`，`ColumnSplitterColumn.Width = 6`
- `RootPreviewGrid`：行 1、行跨 3、列 0、列跨 1
- `RootPropertiesScrollViewer`：行 1、行跨 3、列 2、列跨 1
- `RowSplitterRow.Height = 0`

- [ ] **步骤 3：应用与保存列宽**

`ApplyLayout` 在 `Bottom` 时：

```csharp
double width = PaneSettingsService.BottomDetailsWidthPx;
PropertiesColumn.Width = new GridLength(width, GridUnitType.Pixel);
```

`ColumnSplitter` 拖动完成后把 `PropertiesColumn.ActualWidth` 写回 `BottomDetailsWidthPx`，写回前用 `Math.Max(132, ...)` 与"不超过窗格宽度的 60%"兜底。

详情区在 132 px 时依赖现有模板的 `TextWrapping="Wrap"` 自然换行，**不新增紧凑布局状态**（现有详情本来就是标签在上、值在下）。

底部模式隐藏"打开属性"按钮：在 `ApplyLayout` 里设 `DetailsOpenProperties.Visibility = Collapsed`（其它模式 `Visible`）。

- [ ] **步骤 4：编译与手工验证**

编译通过后切到"底部通栏"：

- 拖底栏上沿改高度、拖中间竖条改详情列宽，两者都保存。
- 详情拖到 132 px：标签与值换行、不横向溢出。
- 打开属性按钮不可见；右键菜单里"属性"仍可用。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/UserControls/Pane/InfoPane.xaml src/Files.App/UserControls/Pane/InfoPane.xaml.cs
git commit -m "Add resizable details column in bottom layout"
```

---

### 任务 5：预览加载按模式走

**文件：**
- 修改：`src/Files.App/ViewModels/UserControls/InfoPaneViewModel.cs`

- [ ] **步骤 1：真实预览的条件**

`UpdateSelectedItemPreviewCoreAsync` 里把

```csharp
if (SelectedTab == InfoPaneTabs.Preview || SelectedItem?.PrimaryItemAttribute == StorageItemTypes.Folder)
```

改为

```csharp
if (SelectedTab == InfoPaneTabs.Preview ||
	infoPaneSettingsService.Layout is not PreviewPaneLayout.RightTabs ||
	SelectedItem?.PrimaryItemAttribute == StorageItemTypes.Folder)
```

效果：上下两行 / 底部模式忽略 `SelectedTab`，总是走真实预览；标签页模式保持现有分支（详情页仍用基础预览）。

- [ ] **步骤 2：切换模式时刷新预览**

`PreviewSettingsService_OnPropertyChangedEvent` 增加对 `Layout` 的处理：当 `Layout` 变化且预览窗处于激活状态时调用一次 `UpdateSelectedItemPreviewAsync()`（与现有 `SelectedTab` 分支同样的条件判断）。

- [ ] **步骤 3：编译**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

- [ ] **步骤 4：手工冒烟**（对应审查重点 1–5）

- 选中一张图片：上下两行 → 真图片预览；底部 → 真图片预览；标签页·详情 → 基础预览（放大图标），标签页·预览 → 真预览。
- 选中 PDF：上下两行显示 Shell 预览（不显示图标）。
- 选中 .txt：显示文本预览。
- 选中文件夹：显示文件夹预览，详情正常。
- 多选、无选中：空状态文案正确，详情区不被 `LoadingPreview` 藏掉。
- 快速点选多个文件：预览不串台，最终显示最后一个文件。
- 选中 .lnk、可浏览 zip、回收站里的文件：仍显示基础预览，不报错。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/ViewModels/UserControls/InfoPaneViewModel.cs
git commit -m "Load real preview in right-split and bottom layouts"
```

---

### 任务 6：文案与收尾

**文件：**
- 修改：`src/Files.App/Strings/en-US/Resources.resw`
- 修改：`src/Files.App/UserControls/Pane/InfoPane.xaml`

- [ ] **步骤 1：加三条文案**

在 `en-US/Resources.resw` 加：

| name | value |
| --- | --- |
| `PreviewPaneLayoutRightSplit` | `Right, preview above details` |
| `PreviewPaneLayoutBottom` | `Below the file list` |
| `PreviewPaneLayoutRightTabs` | `Right, as tabs` |

按钮上通过 `helpers:ResourceString Name=...` 引用，同时用于 `AutomationProperties.Name` 与 `ToolTipService.ToolTip`。

- [ ] **步骤 2：修正 XAML 笔误**

`InfoPane.xaml` 的 `Vertical` 状态里：

```xml
<Setter Target="RootPropertiesScrollViewer.Column" Value="0" />
```

改为

```xml
<Setter Target="RootPropertiesScrollViewer.(Grid.Column)" Value="0" />
```

- [ ] **步骤 3：编译**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

- [ ] **步骤 4：手工验证**

悬停在三个位置按钮上，提示文本正确；读屏名不为空。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/Strings/en-US/Resources.resw src/Files.App/UserControls/Pane/InfoPane.xaml
git commit -m "Localize preview pane layout buttons"
```

---

### 任务 7：整体验证与打包

- [ ] **步骤 1：整份解决方案构建**

```powershell
msbuild -restore Files.slnx -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

- [ ] **步骤 2：按验收清单冒烟**

逐条走规格 §11 的 10 条验收项，记录实际结果；不可测的（如高对比度）显式记成"未验证"。

- [ ] **步骤 3：提交分支**

```powershell
git status --short
git diff --check
git log --oneline -8
```

- [ ] **步骤 4：打包**

按 `AGENTS.md` 用 `.github/scripts/Build-Internal.ps1 -PortableOnly` 出包，报告产物路径。版本号保持不变；提升内部迭代需先问维护者。

- [ ] **步骤 5：启动打包产物冒烟**

打开便携包里的可执行文件，重复步骤 2 的关键几条（三种模式切换、拖分隔条、重启后保留）。
