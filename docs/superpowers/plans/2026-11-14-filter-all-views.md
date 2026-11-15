# 全视图筛选（Filter in All Views）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把"仅详细视图可用的列筛选"升级为全视图可用的筛选栏——常规/高级双模式、条件列表（并且/或者、同维度重复、即时生效）、12 个文本运算符、扩展名独立维度、递归子文件夹（默认 2 层可调）。

**架构（复用优先）：** 引擎原位扩展 `DirectoryFilterSpec` / `DirectoryFilterEvaluator`（新运算符、AND 优先链、关键字工厂）；条件模型**复用 `DirectoryFilterViewModel`**（补 Join/Scope/Length/日期预设），筛选栏只增加一层薄 VM 与一个控件；内联编辑器**由现有 `DirectoryFilterFlyout` 面板改造而来**（复用其 XAML，不重写）；投影沿用 `ShellViewModel.DirectoryFilter` + `ScheduleFilterProjection`；递归在 ShellViewModel 内做限深收集（约 40 行助手 + 单测），不建通用遍历框架。

**技术栈：** C# / .NET 10、WinUI 3、CommunityToolkit.Mvvm（`ObservableObject`）、MSTest、CsWin32（如需新 interop）。

**规格：** `docs/superpowers/specs/2026-11-14-filter-all-views-design.md`（1:1 交互原型：`docs/prototypes/filter-all-views/index.html`）

## 全局约束

- 遵循 `.editorconfig`；文本文件保持 CRLF；注释简洁、不重述代码。
- 禁止新增 `DllImport` / `ComImport`；如需 Win32/COM 走 `src/Files.Storage`、`NativeMethods.txt` 或现有封装。
- 用户可见文案一律走资源：新增键加入 `src/Files.App/Strings/en-US/Resources.resw`，XAML 用 `helpers:ResourceString`；空态复用现有 `NoSearchResultsFound`。
- 保留正则总预算（`DirectoryFilterEvaluator.RegexTotalBudget`）与 `DirectoryFilterEvaluationResult` 错误通道。
- 构建：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`（VS DevShell 内执行；不并行构建）。
- 测试：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~<Class>" -v:quiet`。本机 MTP IPC 命名管道被机器级策略阻断（`UnauthorizedAccessException`）：本地跑不动时按 AGENTS.md 备注换机/独立宿主执行并如实报告。
- 每个任务结束 commit；不改与任务无关的文件；不顺手重构。

## 审查重点（Review Focus）

1. **正则灾难性回溯**：`(a+)+$` 类模式不得冻结 UI——仍由 `RegexTotalBudget` 截断并返回 `Timeout`（任务 1 增补回归测试）。
2. **通配符边界**：空串、连续 `**`、`*.*`、逗号/空格混排、`?` 在首/尾——不抛异常、不全量误匹配（任务 1）。
3. **递归深度与环**：深度 0/负数/超大、目录联接自引用——钳制有效范围、不出现死循环（任务 7 的助手单测）。
4. **时间预设与长度边界**：小时级端点、昨日/午夜边界；`Length` 为 null/负数、非 ASCII 名称——按"未生效"或正确区间处理（任务 1、任务 3）。
5. **模式切换污染**：常规下高级条件不参与求值；切回高级两套配置原样恢复；清空互不影响（任务 3）。

---

## 文件结构

**创建**

| 文件 | 职责 |
| --- | --- |
| `src/Files.App/ViewModels/UserControls/DirectoryFilterBarViewModel.cs` | 筛选栏状态：模式、关键字、条件集合（元素复用 `DirectoryFilterViewModel`）、递归深度、`BuildSpec()` |
| `src/Files.App/UserControls/DirectoryFilterBar.xaml(.cs)` | 筛选栏控件：模式切换、名称编辑、条件流（连接符/编辑器/添加）、递归深度、计数与清除 |
| `tests/Files.App.UnitTests/Filtering/DirectoryFilterBarViewModelTests.cs` | 模式隔离、规格构建、日期预设计算、深度钳制、事件 |
| `tests/Files.App.UnitTests/Filtering/DirectoryRecursionTests.cs` | 限深收集：层级、相对路径、钳制、环保护 |

**改造（复用现有资产）**

| 文件 | 变更 |
| --- | --- |
| `src/Files.App/Utils/Storage/Filtering/DirectoryFilterSpec.cs` | 新运算符、`Length`、`Join`、`FromKeywordText` 工厂 |
| `src/Files.App/Utils/Storage/Filtering/DirectoryFilterEvaluator.cs` | 新运算符求值、通配符（私有缓存）、AND 优先链 |
| `src/Files.App/ViewModels/UserControls/DirectoryFilterViewModel.cs` | 补 `Join`/`Scope`/`Length`/日期预设；`ToColumnFilter()`/`LoadFrom()` |
| `src/Files.App/UserControls/DirectoryFilterFlyout.xaml(.cs)` | `git mv` 为 `DirectoryFilterEditor.xaml(.cs)`；页脚可隐藏、输入即时写回（内联复用） |
| `src/Files.App/Views/Shells/ModernShellPage.xaml(.cs)` | FilterHeader 行替换为 `DirectoryFilterBar` |
| `src/Files.App/ViewModels/ShellViewModel.cs` | 暴露 BarVM、投影、递归收集、退役 `FilesAndFoldersFilter` |
| `src/Files.App/Views/Layouts/DetailsLayoutPage.xaml(.cs)` | 列头=追加条件；递归时"位置"列 |
| `src/Files.App/Strings/en-US/Resources.resw` | 模式/连接符/深度/运算符键 |
| `tests/Files.App.UnitTests/Filtering/DirectoryFilterEvaluatorTests.cs` | 运算符/通配/链求值测试 |
| `tests/Files.App.UnitTests/Filtering/DirectoryFilterViewModelTests.cs` | 新运算符绑定、日期预设、ToColumnFilter 测试 |

---

## 任务 1：引擎扩展——运算符 + 关键字工厂 + 通配

**文件：**
- 修改：`src/Files.App/Utils/Storage/Filtering/DirectoryFilterSpec.cs`
- 修改：`src/Files.App/Utils/Storage/Filtering/DirectoryFilterEvaluator.cs`
- 测试：`tests/Files.App.UnitTests/Filtering/DirectoryFilterEvaluatorTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
[TestMethod]
[DataRow(DirectoryTextOperator.StartsWithNot, "annual", true)]
[DataRow(DirectoryTextOperator.StartsWithNot, "rep", false)]
[DataRow(DirectoryTextOperator.EndsWithNot, ".txt", false)]
[DataRow(DirectoryTextOperator.NotEquals, "REPORT.TXT", false)]
public void NewTextOperators_CompareAgainstDisplayName(DirectoryTextOperator op, string text, bool expected)

[TestMethod]
[DataRow(DirectoryTextOperator.LengthGreaterThan, 5, true)]
[DataRow(DirectoryTextOperator.LengthLessThan, 7, true)]
[DataRow(DirectoryTextOperator.LengthEquals, 6, true)]
[DataRow(DirectoryTextOperator.LengthNotEquals, 6, false)]
public void LengthOperators_UseDisplayNameCharacterCount(DirectoryTextOperator op, int length, bool expected)
// 名称 "report.txt"、displayName "report"（长度 6）

[TestMethod]
public void LengthOperator_WithoutLength_MatchesEverything()

[TestMethod]
[DataRow("*.png", "首页-视觉稿.png", true)]
[DataRow("*.png", "logo.png.bak", false)]
[DataRow("*.PNG", "x.png", true)]
[DataRow("?.md", "a.md", true)]
[DataRow("?.md", "ab.md", false)]
public void WildcardOperator_MatchesWholeDisplayName(string pattern, string displayName, bool expected)

[TestMethod]
public void FromKeywordText_SplitsOnSpaceAndCommas()
// "报告, 2026 目标" → 3 个 Name 条件；"*.png" → 1 个 Wildcard 条件；""/null/"  ,，  " → 空
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~DirectoryFilterEvaluatorTests" -v:quiet`
预期：编译失败（新枚举成员/`Length`/工厂/`Wildcard` 不存在）。

- [ ] **步骤 3：实现**

`DirectoryTextOperator` 增加 `StartsWithNot, EndsWithNot, NotEquals, LengthGreaterThan, LengthLessThan, LengthEquals, LengthNotEquals, Wildcard`；`DirectoryColumnFilter` 增加 `int? Length`；`DirectoryFilterSpec.FromKeywordText(string? text)`（按 `[\\s,，]+` 拆分，含 `*`/`?` 的 token → `Wildcard`，否则 `Contains`；空输入 → `Empty`）。`DirectoryFilterEvaluator.MatchesText` 实现新分支；`Wildcard` 用私有静态缓存（`ConcurrentDictionary<string, Regex>`）把 `*`/`?` 转 `.*`/`.` 并整名 `IgnoreCase | CultureInvariant` 匹配（非通配字符先转义）。长度用 `value.Length`（UTF-16 code unit，注释说明与"字符数"的差异）。

- [ ] **步骤 4：运行测试验证通过**

运行：同上
预期：全部通过（含既有运算符测试）。

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/Utils/Storage/Filtering/DirectoryFilterSpec.cs src/Files.App/Utils/Storage/Filtering/DirectoryFilterEvaluator.cs tests/Files.App.UnitTests/Filtering/DirectoryFilterEvaluatorTests.cs
git commit -m "feat: extend text operators and parse keyword queries"
```

---

## 任务 2：连接符与 AND 优先求值

**文件：**
- 修改：`src/Files.App/Utils/Storage/Filtering/DirectoryFilterSpec.cs`
- 修改：`src/Files.App/Utils/Storage/Filtering/DirectoryFilterEvaluator.cs`
- 测试：`tests/Files.App.UnitTests/Filtering/DirectoryFilterEvaluatorTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
[TestMethod]
public void Chain_UsesAndOverOrPrecedence()
// A: Name Contains "报告"；B: Extension Values [".png"]；C: Name Contains "计划"
// (A∧B)∨C：验证 3 个边界用例

[TestMethod]
public void Chain_FirstConditionJoinIsIgnored()
// 首条 Join=Or 仍按首位求值

[TestMethod]
public void Chain_DefaultsToAnd_WhenJoinNotSet()
// 既有测试语义保持不变
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~DirectoryFilterEvaluatorTests" -v:quiet`
预期：编译失败（`Join` 不存在）。

- [ ] **步骤 3：实现**

`DirectoryFilterSpec.cs`：新增 `public enum DirectoryConditionJoin { And, Or }`；`DirectoryColumnFilter` 增加 `Join`（默认 `And`）。`DirectoryFilterEvaluator.Matches` 改为链求值：首个元素作为当前组；`Join==Or` 时 `total |= group` 并开启新组，否则 `group &= v`；返回 `total || group`。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Filtering" -v:quiet`
预期：全部通过（既有 Filtering 测试不受影响）。

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/Utils/Storage/Filtering/DirectoryFilterSpec.cs src/Files.App/Utils/Storage/Filtering/DirectoryFilterEvaluator.cs tests/Files.App.UnitTests/Filtering/DirectoryFilterEvaluatorTests.cs
git commit -m "feat: support and/or joins with and-precedence"
```

---

## 任务 3：条件模型复用 + 筛选栏 VM

**文件：**
- 修改：`src/Files.App/ViewModels/UserControls/DirectoryFilterViewModel.cs`
- 修改：`src/Files.App/Utils/Storage/Filtering/DirectoryFilterSpec.cs`（R8：`DirectoryNameScope` + `DirectoryColumnFilter.Scope`；R9：`DirectoryColumnFilter.Regex`）
- 修改：`src/Files.App/Utils/Storage/Filtering/DirectoryFilterEvaluator.cs`（R8：按 Scope 取名；R9：条件级正则预编译与错误通道）
- 创建：`src/Files.App/ViewModels/UserControls/DirectoryFilterBarViewModel.cs`
- 测试：`tests/Files.App.UnitTests/Filtering/DirectoryFilterViewModelTests.cs`（扩展）、`DirectoryFilterBarViewModelTests.cs`（新建）、`DirectoryFilterEvaluatorTests.cs`（R8/R9）

- [ ] **步骤 1：编写失败的测试**

```csharp
// DirectoryFilterViewModelTests（扩展）
[TestMethod]
public void ToColumnFilter_IncludesJoinScopeLengthAndDatePreset()
[TestMethod]
[DataRow(DirectoryDatePreset.LastHour, -1)]        // 相对 clock 的小时偏移
[DataRow(DirectoryDatePreset.Last6Hours, -6)]
[DataRow(DirectoryDatePreset.Last24Hours, -24)]
public void ApplyDatePreset_ComputesRelativeRange(DirectoryDatePreset preset, int hourOffset)
[TestMethod]
public void ApplyDatePreset_Yesterday_SpansWholePreviousDay()
[TestMethod]
public void ApplyDatePreset_ThisMonth_StartsAtFirstDayMidnight()

// DirectoryFilterBarViewModelTests（新建）
[TestMethod]
public void BuildSpec_BasicMode_ParsesKeywordText()
// "报告, 2026" → spec.Columns.Count == 2
[TestMethod]
public void BuildSpec_AdvancedMode_UsesConditionItemsWithJoins()
[TestMethod]
public void ModeSwitch_PreservesBothConfigurations()
[TestMethod]
[DataRow(0, 1)][DataRow(999, 64)]
public void RecursiveDepth_IsClamped(int requested, int expected)
[TestMethod]
public void ClearAll_ResetsKeywordsAndConditions_ButKeepsRecursiveScope()
[TestMethod]
public void SpecChanged_IsRaisedOnAnyMutation()
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~DirectoryFilterBarViewModelTests|FullyQualifiedName~DirectoryFilterViewModelTests" -v:quiet`
预期：编译失败。

- [ ] **步骤 3：实现**

`DirectoryFilterViewModel` **原位扩展**（不新建条件 VM 类）：增加 `Join`、`Scope`（`RawName`/`DisplayName`）、`Length`、`DirectoryDatePreset` 枚举与 `ApplyDatePreset(preset)`（用注入了 `clock` 计算 `MinDate`/`MaxDate`：LastHour=`clock(-1h)` 起；Yesterday=昨日 00:00–23:59:59.999；ThisMonth=当月 1 日 00:00），新增 `DirectoryColumnFilter ToColumnFilter()` 与 `LoadFrom(DirectoryColumnFilter)`。`DirectoryFilterBarViewModel : ObservableObject`：`FilterMode Mode`（Basic/Advanced）、`string? KeywordText`、`ObservableCollection<DirectoryFilterViewModel> Conditions`、`bool RecursiveEnabled`、`int RecursiveDepth`（默认 2，钳制 1..64，64≈不限）、`event EventHandler? SpecChanged`；`BuildSpec()`——Basic 用 `DirectoryFilterSpec.FromKeywordText(KeywordText)`，Advanced 只投影 `Conditions.Where(c => c.IsActive)`（空白草稿不生效）；`AddCondition(DirectoryColumnKind, DirectoryConditionJoin)`、`RemoveCondition(...)`、`ClearAll()`。`DirectoryFilterViewModel` 增加 `IsActive` 谓词；日期预设按 R2 计算；`ToColumnFilter()/LoadFrom()` 承载 Join/Scope/Length/日期/条件级 Regex（R9）。

- [ ] **步骤 4：运行测试验证通过**

运行：同上
预期：全部通过。

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/ViewModels/UserControls/DirectoryFilterViewModel.cs src/Files.App/ViewModels/UserControls/DirectoryFilterBarViewModel.cs tests/Files.App.UnitTests/Filtering/DirectoryFilterViewModelTests.cs tests/Files.App.UnitTests/Filtering/DirectoryFilterBarViewModelTests.cs
git commit -m "feat: reuse condition view model for filter bar"
```

---

## 任务 4：内联编辑器（复用 Flyout 面板）+ DirectoryFilterBar 控件

**文件：**
- 重命名/改造：`src/Files.App/UserControls/DirectoryFilterFlyout.xaml(.cs)` → `DirectoryFilterEditor.xaml(.cs)`
- 创建：`src/Files.App/UserControls/DirectoryFilterBar.xaml(.cs)`
- 修改：`src/Files.App/Strings/en-US/Resources.resw`
- 验证：构建 + 手动冒烟

- [ ] **步骤 1：改造现有 Flyout 为内联编辑器**

`git mv` 两个文件并同步 `x:Class`；增加 `bool ShowFooter`（默认 false 于内联）、`bool IsInline`；输入控件改为对 `DirectoryFilterViewModel` 属性**双向即时绑定**（去掉"应用/清除"按钮作为内联路径；页脚仅在弹层路径保留）。保留现有全部面板（名称运算符/文本、类型与扩展名复选、标签、大小、日期预设、正则）。

- [ ] **步骤 2：新增 resw 键**

`FilterModeBasic=常规`、`FilterModeAdvanced=高级`、`FilterKeywordsPlaceholder=文件名（空格或逗号分隔，支持 * ?）`、`FilterAddConditionContent=添加`、`FilterJoinAndContent=并且`、`FilterJoinOrContent=或者`、`FilterConditionCountFormat={0} 个匹配项`、`FilterRecursiveContent=搜索子文件夹`、`FilterRecursiveDepthFormat={0} 层`、`FilterRecursiveDepthUnlimited=不限`、`FilterScopeRawName=完整文件名`、`FilterScopeDisplayName=显示名称`，12 个运算符键（`FilterOperator*`），日期预设键（`FilterDatePresetLastHour/Last6Hours/Last24Hours/Today/Yesterday/Last3Days/Last7Days/Last30Days/ThisMonth/ThisYear/Custom`）。空态复用 `NoSearchResultsFound`。

- [ ] **步骤 3：创建 DirectoryFilterBar 控件**

单行 `StackPanel`：`SelectorBar`（常规/高级）→ 名称编辑（内联 `DirectoryFilterEditor` 实例）→ 条件流（`ItemsControl`：连接符 `Button` + `DirectoryFilterEditor` 实例 + 移除按钮）→ `Button`("添加") → "搜索子文件夹" `CheckBox` + 深度 `ComboBox` → 计数 `TextBlock` → "清除所有筛选"按钮。常规模式隐藏条件流与添加按钮（`Visibility` 绑定 `IsBasicMode`）。code-behind 绑定 `DirectoryFilterBarViewModel`，选择器复用 `GetFilterChoices` 的 `Flyout` 多选，`Ctrl+Shift+F` `KeyboardAccelerator` 聚焦名称框。

- [ ] **步骤 4：构建并冒烟**

运行：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：0 errors；控件在临时宿主页可加载（可选，最终在任务 5 验证）。

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/UserControls/DirectoryFilterEditor.xaml src/Files.App/UserControls/DirectoryFilterEditor.xaml.cs src/Files.App/UserControls/DirectoryFilterBar.xaml src/Files.App/UserControls/DirectoryFilterBar.xaml.cs src/Files.App/Strings/en-US/Resources.resw
git commit -m "feat: reuse filter flyout panels as inline editor and add bar"
```

---

## 任务 5：Shell 集成（替换 FilterHeader 行）

**文件：**
- 修改：`src/Files.App/Views/Shells/ModernShellPage.xaml:128-157`
- 修改：`src/Files.App/Views/Shells/ModernShellPage.xaml.cs`
- 修改：`src/Files.App/ViewModels/ShellViewModel.cs`
- 验证：构建 + 启动冒烟

- [ ] **步骤 1：ShellViewModel 暴露筛选栏**

新增 `public DirectoryFilterBarViewModel FilterBar { get; }`；构造时创建并订阅 `SpecChanged`：`DirectoryFilter = FilterBar.BuildSpec()`（沿用现有 `ResetQueryView` + `ScheduleFilterProjection`）；`FilesAndFoldersFilter` 保留为兼容层（setter 写 `FilterBar.KeywordText`）；`ShowFilterHeader` 逻辑不变。

- [ ] **步骤 2：替换 XAML 行**

删除 FilterHeader `Grid`（缩略图/`FilteringFor`/`AutoSuggestBox`），改为：

```xml
<uc:DirectoryFilterBar x:Name="DirectoryFilterBar" Grid.Row="1"
    DataContext="{x:Bind ShellViewModel.FilterBar, Mode=OneWay}"
    Visibility="{x:Bind ShellViewModel.ShowFilterHeader, Mode=OneWay}" />
```

- [ ] **步骤 3：构建并启动冒烟**

运行构建命令；启动 Debug 构建。
检查：漏斗 ✓ 显示/隐藏筛选栏；常规输入 `报告, 2026` 即时过滤；切高级出现条件区；`Ctrl+Shift+F` 聚焦；筛选生效且栏收起时漏斗出圆点；空态文案正确。

- [ ] **步骤 4：Commit**

```bash
git add src/Files.App/Views/Shells/ModernShellPage.xaml src/Files.App/Views/Shells/ModernShellPage.xaml.cs src/Files.App/ViewModels/ShellViewModel.cs
git commit -m "feat: host filter bar in shell and project spec changes"
```

---

## 任务 6：列头 = 弹窗（复用同一编辑器）

**文件：**
- 修改：`src/Files.App/Views/Layouts/DetailsLayoutPage.xaml.cs:997-1091`
- 验证：构建 + 手动清单

- [ ] **步骤 1：列头改为追加条件**

`OpenColumnFilter(string name)`：解析 `DirectoryColumnKind`；在列头下方弹出 `DirectoryFilterColumnFlyout`，内部复用 `DirectoryFilterEditor` 与同一套查询语言；“应用”时移除该列旧条件、把编辑器条件加入 `FilterBar.Conditions` 并切到高级，“清除”移除该列条件，“清除所有筛选”调用 `FilterBar.ClearAll()`。名称列头同样弹窗（沿用筛选栏名称算法）。

- [ ] **步骤 2：列头激活指示改为读条件集合**

`UpdateFilterIndicators` 读取 `ShellViewModel.FilterBar.Conditions`（任一激活即点亮圆点），不再依赖本页 flyout 内容。

- [ ] **步骤 3：构建并手动验证**

运行构建命令。
检查：任一列头漏斗 → 弹出该列筛选弹窗（含应用/清除/清除所有筛选）；“应用”后条件进入筛选栏条件列表并切高级；圆点同步；空态正常。

- [ ] **步骤 4：Commit**

```bash
git add src/Files.App/Views/Layouts/DetailsLayoutPage.xaml.cs
git commit -m "feat: append conditions from details column headers"
```

---

## 任务 7：递归子文件夹（限深）+ 位置列

**文件：**
- 修改：`src/Files.App/ViewModels/ShellViewModel.cs:1520-1600`
- 修改：`src/Files.App/Views/Layouts/DetailsLayoutPage.xaml(.cs)`
- 创建：`tests/Files.App.UnitTests/Filtering/DirectoryRecursionTests.cs`
- 创建：`src/Files.App/Utils/Storage/Filtering/DirectoryRecursion.cs`（约 40 行局部助手，仅供 ShellViewModel 使用）

- [ ] **步骤 1：编写失败的测试**

```csharp
// 纯逻辑助手：Flatten(roots, childrenOf, pathOf, maxDepth)
[TestMethod]
public void Flatten_DepthOne_IncludesFirstLevelOnly()
[TestMethod]
public void Flatten_DepthTwo_AddsRelativePaths()
[TestMethod]
[DataRow(0, 1)][DataRow(-5, 1)][DataRow(999, 64)]
public void Flatten_ClampsDepth(int requested, int effective)
[TestMethod]
public void Flatten_CycleGuard_StopsOnRepeatedPath()
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~DirectoryRecursionTests" -v:quiet`
预期：编译失败。

- [ ] **步骤 3：实现助手并在 ShellViewModel 接线**

`DirectoryRecursion.Flatten<T>`：`Math.Clamp(maxDepth,1,64)`，迭代/栈遍历，`RelativePath` 为父路径 + `\` + 名称，`HashSet<string>(OrdinalIgnoreCase)` 防环；**只做收集，不做枚举**。ShellViewModel 在 `filesAndFolders` 构建处：仅当 `FilterBar.RecursiveEnabled && BuildSpec() 非空` 时，用现有目录枚举按 `RecursiveDepth` 收集子项并写入相对路径；未筛选时不触发枚举。

- [ ] **步骤 4：位置列**

`DetailsLayoutPage.xaml` 增加"位置"列（`Visibility` 绑定递归生效），行内绑定相对路径；其余视图沿用 tooltip（原型行为）。

- [ ] **步骤 5：运行测试 + 构建 + 手动验证**

运行：测试命令 + 构建命令；启动 Debug。
检查：勾选+输入关键字 → 子目录文件命中且"位置"列显示；深度 1/2 计数不同；未筛选时勾选无变化；状态栏"（含 N 层子文件夹）"。

- [ ] **步骤 6：Commit**

```bash
git add src/Files.App/Utils/Storage/Filtering/DirectoryRecursion.cs src/Files.App/ViewModels/ShellViewModel.cs src/Files.App/Views/Layouts/DetailsLayoutPage.xaml src/Files.App/Views/Layouts/DetailsLayoutPage.xaml.cs tests/Files.App.UnitTests/Filtering/DirectoryRecursionTests.cs
git commit -m "feat: search subfolders with depth control and show location"
```

---

## 任务 8：清理、全量验证与打包

**文件：**
- 修改：`src/Files.App/ViewModels/ShellViewModel.cs`（移除 `FilesAndFoldersFilter` 及引用）
- 修改：`src/Files.App/Views/Shells/ModernShellPage.xaml.cs`、`DetailsLayoutPage.xaml.cs`（残留）
- 验证：全量构建 + 单元测试 + 打包冒烟

- [ ] **步骤 1：退役 FilesAndFoldersFilter 与无用 API**

`rg "FilesAndFoldersFilter" src` 清空引用；移除属性；`BaseShellPage` 监听分支只保留 `DirectoryFilter`；若 `DirectoryFilterViewModel.ApplyTo/ClearFrom` 已无调用方，一并删除。

- [ ] **步骤 2：全量构建**

运行：`msbuild -restore Files.slnx -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：0 errors。

- [ ] **步骤 3：运行单元测试**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -v:quiet`（MTP 受限时按备注换机/独立宿主，如实报告）。
预期：全部通过。

- [ ] **步骤 4：Commit 后打包**

```bash
git add -A && git commit -m "chore: retire legacy name filter state"
pwsh .github/scripts/Build-Internal.ps1 -PortableOnly
```

预期：`artifacts/internal/<version>/portable/*.zip`；启动打包版冒烟（常规/高级切换、条件增删、递归深度）。

- [ ] **步骤 5：验收清单**

- 常规：`报告, 2026`、`*.png`、`?.md`、空输入正确；
- 高级：12 运算符、长度类数字输入、正则红框、并且/或者、同维度重复；
- 列头追加条件与激活圆点；`Ctrl+Shift+F`；空态与计数；跨视图筛选保持；
- 递归：默认 2 层、深度切换、未筛选不枚举、位置列；
- 无回归：搜索框、地址栏、其他视图交互正常。

---

## 自检记录（计划作者执行）

- **规格覆盖**：入口/模式（T5）、筛选栏（T4/T5）、条件列表与连接符（T2/T3/T4）、12 运算符（T1）、关键字+通配（T1）、日期预设（T3）、列头（T6）、递归深度（T7）、空态/计数（T4/T5）、复用架构（各任务）。"选择类取反"已按 D7 降为 P2，不排任务；括号分组与地址栏编码按开放问题处理。
- **步骤扫描**：实现步骤给签名/语义与规格钉死的取值，不写函数体；测试给名字与断言。
- **类型一致性**：`DirectoryFilterSpec.FromKeywordText`、`DirectoryColumnFilter.Join/Length`、`DirectoryFilterViewModel.ToColumnFilter/LoadFrom`、`DirectoryFilterBarViewModel.BuildSpec/AddCondition`、`DirectoryRecursion.Flatten` 在任务间一致。
- **审查重点**：五类风险分别落在 T1（正则/通配/长度）、T3（日期/模式）、T7（深度/环）。
- **比例**：任务为签名清单与验证步骤，不复制实现；UI 以构建+手动清单验收（项目无 UI 单测基建）。

## 实现期裁决与偏差（执行记录）

| # | 裁决/偏差 | 原因 |
| --- | --- | --- |
| R6 | 地址栏不承载 Join/Length/Scope/条件级 Regex 与通配/负向运算符（P2 跟进） | `DirectoryFilterAddress` 未在原任务清单；应用内导航经 `NavigationArguments` 不受影响 |
| R8 | 允许扩展 `DirectoryNameScope`（完整文件名/显示名）并进入求值 | `.ext` 是 D6 真实能力，纯 VM 状态会成为装饰 |
| R9 | 允许条件级 `DirectoryColumnFilter.Regex` | 每条件正则不可装进 spec 单 Regex；否则 `.*` 从 T4 起失效 |
| R13 | spec 变化不使递归缓存失效（缓存=未筛选根投影） | 失效会导致每次按键重枚举整棵子树；正确性不受影响 |
| 列头显隐 | 列头点击显式展开筛选栏（写 `ShowFilterHeader`） | 显式筛选意图，条件不可见=更差体验；用户可再用漏斗收起 |
| 列头交互 | 列头打开弹窗，“应用”把条件加入筛选栏条件列表（自动切高级） | 保持用户熟悉的列头弹窗；查询语言与筛选栏一致，筛选状态仍只有一份 |
| 缩略图 | 删除原 FilterHeader 的文件夹缩略图与“Filtering for” | 简报 T5 要求；装饰性跨页状态，骨架/Padding 保留 |
| 空态 | 复用 `NoMatchingItems` | 语义比 `NoSearchResultsFound` 更贴切 |
| 递归范围 | 仅 Win32 可枚举目录；库/回收站/压缩包/MTP/FTP 跳过 | 复用根目录同一枚举助手（隐藏/点文件规则一致） |
| 递归进度 | 进度反馈与状态栏“（含 N 层子文件夹）”未做 | 无现成承载位，延后 |
| 位置列 | 固定 160px、不可排序/筛选/持久化；仅详细视图列 | 原型一致；其余视图 tooltip |
