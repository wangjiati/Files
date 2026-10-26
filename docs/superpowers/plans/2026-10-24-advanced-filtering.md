# 高级列筛选与正则实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [x]`）语法来跟踪进度。

**目标：** 在当前已加载目录上提供统一的名称、类型、扩展名、标签、大小和日期筛选，并支持三种目标的正则匹配。

**架构：** 将现有 `MatchesFilter` 扩展为不可变 `DirectoryFilterSpec` 和纯快照求值器；ShellViewModel 只负责快照、取消、版本和 UI 投影。Details 列头生成同一规范，正则只是规范中的一种条件。

**技术栈：** .NET 10、WinUI 3、现有 ShellViewModel/DataGridHeader、MSTest、System.Text.RegularExpressions。

**规格：** `docs/superpowers/specs/2026-10-24-files-internal-edition-design.md`

## 全局约束

- 默认只筛选当前已加载项目，不递归、不重新枚举磁盘。
- 首版不同列 AND、同列多选 OR；切换目录清除临时条件。
- 正则目标必须包括 RawName、DisplayName、FullPath，支持大小写、错误提示、超时和取消。
- 未知大小/时间为 null，不得当成 0 或最小时间。
- 后台只访问标量快照，UI 集合只在 dispatcher 更新，不引入运行时反射或动态代码生成。

## 审查重点（Review Focus）

- 快速输入并切换目录：旧求值不得覆盖新目录；任务 2 测试。
- 重命名后原本匹配的项目不再匹配：应重算而非残留；任务 2 测试。
- 隐藏扩展名时正则目标不同：结果必须与所选目标一致；任务 4 测试。
- 灾难性回溯或非法正则：明确错误/超时且不崩溃；任务 4 测试。
- 文件夹未知大小：大小条件不得当作 0 字节；任务 1 测试。

## 文件结构

- `Utils/Storage/Filtering/DirectoryFilterSpec.cs`：条件模型。
- `DirectoryItemFilterSnapshot.cs`：标量快照。
- `DirectoryFilterEvaluator.cs`：结构化条件单遍求值。
- `DirectoryRegexFilter.cs`：安全正则。
- `DirectoryFilterExecutionContext.cs`：目录、版本和取消一致性。
- `ViewModels/ShellViewModel.cs`：现有投影接入。
- `UserControls/DataGridHeader.xaml(.cs)`、`Views/Layouts/DetailsLayoutPage.xaml(.cs)`：筛选 UI。
- `ViewModels/UserControls/DirectoryFilterViewModel.cs`、`UserControls/DirectoryFilterFlyout.xaml`：条件编辑器。
- `tests/Files.App.UnitTests/Filtering/`：纯逻辑测试。

---

### 任务 1：筛选规范和结构化求值

**文件：** 创建上述 Spec、Snapshot、Evaluator 与测试。

- [x] 编写失败测试：名称五种操作、类型/扩展名/标签多选、大小范围、修改/创建日期范围、null、跨列 AND、同列 OR、空 Spec 全通过。
- [x] 定义 `DirectoryColumnKind`、`DirectoryTextOperator`、`DirectoryColumnFilter`、`DirectoryFilterSpec`；Spec 包含现有 `NameContains`、列条件和可选 Regex。
- [x] 定义快照字段 `ItemId`、RawName、DisplayName、FullPath、Extension、ItemType、TagIds、SizeBytes?、ModifiedUtc?、CreatedUtc?、IsFolder，使用稳定 ItemId 映射原对象。
- [x] 实现 `IReadOnlyList<long> Evaluate(IReadOnlyList<DirectoryItemFilterSnapshot> items, DirectoryFilterSpec spec)`，一次遍历返回原顺序 ID；普通 Contains 不转正则。
- [x] 运行测试，预期全部通过。
- [x] 提交：`Add structured directory filter evaluator`。

### 任务 2：Shell 投影与变化失效

**文件：** 创建执行上下文；修改 `ShellViewModel.cs` 的名称过滤、投影、搜索增量、watcher、重命名和延迟属性路径。

- [x] 编写失败测试：旧 generation/目录/source revision 被拒绝，取消后不可提交；重命名、大小、日期和标签版本变化触发一次合并重算。
- [x] 将 `FilesAndFoldersFilter` 映射为 Spec.NameContains，保留 250ms debounce；快照在后台求值，结果按原 filesAndFolders 顺序映射。
- [x] 用一个执行上下文封装 DirectoryKey、Spec、SourceRevision、Generation 和 linked CancellationToken；提交前验证整个上下文。
- [x] 搜索增量与 watcher 不再调用旧 `MatchesFilter`，统一走 Spec；影响首版字段的变化标记 source dirty 并合并重算。
- [x] 保持批量 Reset 和分组行为；空结果显示“没有匹配项目”而不是“目录为空”。
- [x] 运行测试和 Files.App x64 Debug 构建，手动验证连续输入、目录切换、重命名和 watcher。
- [x] 提交：`Unify directory filter projection`。

### 任务 3：Details 列头筛选 UI

**文件：** 修改 DataGridHeader 和 DetailsLayout；创建 FilterFlyout/ViewModel；修改 ModernShellPage 和英文资源。

- [x] 编写 ViewModel 失败测试：各列生成预期 Spec、应用/清除只影响当前列、活动状态、顶部名称框不重复条件。
- [x] DataGridHeader 增加独立 `FilterCommand`、`FilterCommandParameter`、`IsFilterActive`，保留排序 Button，避免嵌套按钮。
- [x] Flyout 按列提供文本、多选、数值范围、日期范围和常用相对日期；只修改共享 Spec 的对应部分。
- [x] 在 Details 名称、类型、大小、修改、创建和标签列接入；显示活动标识和一键清除，目录切换清除全部临时条件。
- [x] 运行测试并构建，验证键盘、屏幕阅读器名称、空结果、分组和清除。
- [x] 提交：`Add details column filtering`。

### 任务 4：三种目标的正则

**文件：** 创建 `DirectoryRegexFilter.cs`；修改 Spec、Evaluator、FilterFlyout/ViewModel 和资源。

- [x] 编写失败测试：RawName/DisplayName/FullPath、IgnoreCase 开关、无效表达式、超时、取消、长输入、普通名称性能不退化。
- [x] 定义 `DirectoryRegexOptions(string Pattern, DirectoryRegexTarget Target, bool IgnoreCase)`；Evaluator 每个任务只构造一次 Regex，设置 matchTimeout 和整体取消。
- [x] 捕获 ArgumentException/RegexMatchTimeoutException，通过 ViewModel 显示错误；不使用 Compiled 或无超时匹配，不应用半份结果。
- [x] UI 提供目标、大小写、表达式、错误和超时状态，并明确 RawName 包含扩展名。
- [x] 运行全部测试、Msix/Portable 构建；手动验证灾难性表达式可取消、快速切换目录及隐藏扩展名时三种目标结果。
- [x] 提交：`Add safe regex directory filtering`。
