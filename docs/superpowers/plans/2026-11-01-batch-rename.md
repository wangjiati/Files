# 批量重命名 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 用可最大化的大弹窗替换现有"同名去重"式 BulkRenameDialog，提供查找替换（文本/通配符/正则）、方案模板、作用域+子文件夹深度、变量、预览、冲突策略、预设、剪贴板名称、整批撤销的批量重命名。

**架构：** 纯逻辑"重命名引擎"（模式转换、变量展开、校验、冲突解析）放在 `Files.App/Utils/Rename/` 并全部单测覆盖；ViewModel 编排状态与预览；视图是挂载在 `MainPage` 根 Grid 的自绘模态覆盖层 UserControl（自定义标题栏、最大化/还原、规则栏左右停靠）；执行走现有 `FilesystemHelpers.RenameAsync`（`registerHistory:false`）+ 合并为单条 `StorageHistory` 实现整批撤销。

**技术栈：** C#/.NET 8, WinUI 3 (WindowsAppSDK), MSTest，`System.Text.RegularExpressions`（源生成 JSON 用于预设存储）。

**规格：** `docs/superpowers/specs/2026-11-01-batch-rename-design.md`（执行者必读；视觉基准 `.superpowers/brainstorm/284-1793510410/content/layout-v11.html`）

## 全局约束

- 构建：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`；不并行构建；msbuild 不可用时进 VS DevShell
- 单测：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Rename"`（MSTest，`[TestClass]/[TestMethod]`，参照 `tests/Files.App.UnitTests/Filtering/DirectoryRegexFilterTests.cs`）
- 正则一律 `System.Text.RegularExpressions.Regex` + `MatchTimeout` 250ms（参照 `DirectoryRegexFilter`）
- 所有用户可见字符串进 `src/Files.App/Strings/en-US/Resources.resw`；XAML 用 `{helpers:ResourceString}`，代码用 `Strings.Xxx`
- 互斥选项=分段控件/单选；可叠加=CheckBox；图标只用 Fluent 线性图标；禁止 emoji 图标
- 禁 `DllImport`/`ComImport`/运行时反射；不改生成代码；`.editorconfig` 与 CRLF；只动必要文件
- 通配符→正则用 `System.IO.Enumeration.FileSystemName.TranslateWin32Expression`（先例：`Utils/Storage/Search/FolderSearch.cs:552`）

## 规格开放项的裁定（原 §14）

1. **弹窗实现**：主窗口内自定义模态覆盖层 `BatchRenameOverlayPresenter`，挂 `Views/MainPage.xaml` 根 Grid（`Grid.RowSpan="3"`，参照同页 TeachingTip 与 `ShellPanesPage` 的 `TabDropOverlay` 先例）。理由：保持模态、生命周期挂主窗口；`ContentDialog` 无法最大化，`AppWindow` 失去模态。
2. **预览重算防抖** 200ms；**深度枚举**异步可取消（`CancellationTokenSource`，切换作用域即取消旧枚举）；**列表虚拟化**用 `ListView` + `CollectionViewSource` IsSourceGrouped 分组。
3. **整批撤销**：每项 `RenameAsync(..., registerHistory:false)`，随后手工构造**一条** `StorageHistory(FileOperationType.Rename, sources, destinations)` 加入 `App.HistoryWrapper`（列表构造器现成，`StorageHistoryOperations` 已按列表逆序回放 Rename）。
4. **MatchTimeout = 250ms**。
5. **设置**：新增 `IBatchRenameSettingsService`（`RulePanelDockedLeft` 默认 false、`LastPresetName` 默认 ""）；预设存独立 JSON 文件（仿 `FileTagsSettingsService` 自建 `JsonSerializerContext`）。

## 审查重点（Review Focus）

1. **灾难性/非法正则**：坏模式必须变成规则级错误提示并禁用"应用"，绝不能崩溃或挂死 → 任务 4 的 `RegexTimeout/BadPattern` 测试。
2. **大小写-only 改名被误判冲突**（`IMG.jpg→img.jpg`）：必须不算冲突、可执行 → 任务 5 的 case-only 测试。
3. **扩展名误伤**：仅文件名默认、结尾追加默认插在扩展名前 → 任务 4 的 applyTo/append 测试。
4. **撤销不完整**：加序号批改后撤销必须逆序完整还原、不产生中间冲突 → 任务 9 的计划构建测试 + 单条合并历史。
5. **手改值丢失**：改规则/切模板/换预设后手改行必须原样保留，仅 ↺ 重置 → 任务 8 的 override-survives 测试。

---

### 任务 1：基础模型 + 通配符转换

**文件：**
- 创建：`src/Files.App/Utils/Rename/RenameModels.cs`（枚举 `RenameMode{Text,Wildcard,Regex}`、`RenameApplyTo{NameOnly,ExtensionOnly,NameAndExtension}`、`RenameTemplate{FindReplace,Prepend,Append}`、`RenameCaseConversion{None,Lowercase,Uppercase,FirstLetter,EveryWord}`、`RenameScopeOption{SelectedItems,CurrentFolder,CurrentFolderWithSubfolders}`、`FileDateSource{Created,Modified}`、`ConflictStrategy{AppendNumber,Skip,Overwrite}`）
- 创建：`src/Files.App/Utils/Rename/RenameRule.cs`（`sealed record RenameRule(...)`，字段：Template/Find/ReplaceWith/Mode/ApplyTo/MatchAllOccurrences/CaseSensitive/Prefix/SkipItemsWithPrefix/Suffix/InsertBeforeExtension/CaseConversion/DateSource）
- 创建：`src/Files.App/Utils/Rename/WildcardConverter.cs`：`public static string ToRegexPattern(string wildcard)`
- 测试：`tests/Files.App.UnitTests/Rename/WildcardConverterTests.cs`

- [ ] **步骤 1：写失败测试**（MSTest；断言语义而非正则串本身：`Regex.IsMatch("IMG_2049.jpg", "^" + ToRegexPattern("IMG_*") + "$", RegexOptions.IgnoreCase)` 为 true；`IMG_2050` 匹配 `IMG_?050` 不匹配 `IMG_?051`；`a.b` 中的 `.` 按字面量处理：模式 `IMG_.` 不匹配 `IMGab`；空模式返回可匹配任意串的等价物或空串——由实现决定，测试只钉 `ToRegexPattern("IMG_*")` 能让 `IMG_2049.jpg` 全串匹配）
- [ ] **步骤 2：跑测试确认失败**（类不存在，编译错误即失败）
- [ ] **步骤 3：实现** `ToRegexPattern`：`FileSystemName.TranslateWin32Expression(wildcard)`，为整串匹配语义补 `^...$` 由调用方处理——本方法只返回中段；记录 Dos-Star 语义决定
- [ ] **步骤 4：跑测试通过**
- [ ] **步骤 5：构建 Files.App**（新文件入主项目）确认无错
- [ ] **步骤 6：Commit** `Add wildcard converter and rename rule models`

### 任务 2：变量展开器

**文件：**
- 创建：`src/Files.App/Utils/Rename/RenameVariableExpander.cs`：`public sealed class RenameVariableExpander`，工厂 `public static RenameVariableExpander Create(string pattern)`；`public readonly record struct VariableContext(DateTimeOffset Created, DateTimeOffset Modified, FileDateSource Source, string FolderName);`；实例方法 `public string Expand(VariableContext context)`（每次调用推进计数器）
- 测试：`tests/Files.App.UnitTests/Rename/RenameVariableExpanderTests.cs`

- [ ] **步骤 1：写失败测试**，钉住规格 §7 确切取值：
  - `Create("照片_${}.jpg")` 连续 Expand 三次 → `照片_0.jpg/照片_1.jpg/照片_2.jpg`
  - `${start=10;padding=4;increment=2}` → `0010`、`0012`、`0014`
  - 同串两个计数器 `A${}B${padding=2}` → `A0B00`、`A1B01`（各自独立）
  - 日期：`Created=2026-11-01T09:05:07.123Z`，`$YYYY-$MM-$DD_$hh$mm$ss` → `2026-11-01_090507`；`$fff`→`123`；`Source=Modified` 时读 Modified
  - `${folder}`：`FolderName="夜景"` → `夜景`
  - 畸形变量保持字面量：`Create("${abc}$YYY")` 原样输出
- [ ] **步骤 2：跑测试确认失败**
- [ ] **步骤 3：实现**（正则解析 `${...}` 与 `$token`，未识别 token 原样保留；计数器状态按出现顺序挂实例字段）
- [ ] **步骤 4：跑测试通过**
- [ ] **步骤 5：Commit** `Add rename variable expander`

### 任务 3：名称校验器

**文件：**
- 创建：`src/Files.App/Utils/Rename/RenameValidator.cs`：`public static RenameValidationError? Validate(string fileName)`；`public enum RenameValidationError { Empty, IllegalCharacter, ReservedName, TrailingDotOrSpace, TooLong }`
- 测试：`tests/Files.App.UnitTests/Rename/RenameValidatorTests.cs`

- [ ] **步骤 1：写失败测试**：`京都_2056?.jpg`→IllegalCharacter；`CON.txt`、`LPT1`→ReservedName；`name.`、`name `→TrailingDotOrSpace；新名 >255 字符→TooLong；`""`→Empty；`京都_2053 (2).jpg`→null；`img.jpg`→null（校验对象是不含路径的完整文件名，扩展名一起算）
- [ ] **步骤 2：跑测试确认失败**
- [ ] **步骤 3：实现**：复用 `FilesystemHelpers.ContainsRestrictedCharacters/ContainsRestrictedFileName`（`Utils/Storage/Operations/FilesystemHelpers.cs:913/918`），补结尾点/空格与长度 255（`NameTooLong` 阈值取 `fileName.Length > 255`）
- [ ] **步骤 4：跑测试通过**
- [ ] **步骤 5：Commit** `Add rename name validator`

### 任务 4：重命名引擎（模板/模式/应用范围/大小写/超时）

**文件：**
- 创建：`src/Files.App/Utils/Rename/RenameEngine.cs`：`public sealed class RenameEngine`；`public readonly record struct RenameTarget(string FullPath, string Name, bool IsFolder, DateTimeOffset CreatedUtc, DateTimeOffset ModifiedUtc, string ParentFolderName);`；`public sealed record RenameOutcome(string OriginalName, string NewName, RenameOutcomeKind Kind, RenameError Error)`；`public enum RenameOutcomeKind { Unchanged, Changed, RuleError }`、`public enum RenameError { None, BadPattern, RegexTimeout, InvalidName }`
- 方法：`public IReadOnlyList<RenameOutcome> Run(IReadOnlyList<RenameTarget> targets, RenameRule rule)`
- 测试：`tests/Files.App.UnitTests/Rename/RenameEngineTests.cs`

- [ ] **步骤 1：写失败测试**（每个用例给 targets + rule，断言 NewName/Kind）：
  - 正则 `IMG_(\d+)`→`京都_$1`（MatchAll=true, NameOnly）：`IMG_2049.jpg`→`京都_2049.jpg`；`DSC_0001.jpg`→Unchanged
  - 文本模式区分大小写开关：`img_2053.jpg` 在 CaseSensitive=false 命中、=true 不命中
  - 通配符模式 `IMG_*`→`照片_*`（替换串中的 `*` 映射捕获内容）
  - MatchAll=false 只换第一处（规格 §"匹配全部"的反例：`powertoys-powerrename` 换 `power`→`super` 得 `supertoys-powerrename`）
  - Prepend：`京都之旅_` + NameOnly；SkipItemsWithPrefix=true 时已含前缀的项 Unchanged
  - Append：`_备份`+InsertBeforeExtension=true→`img_备份.jpg`；=false→`img.jpg_备份`
  - ApplyTo=ExtensionOnly：`txt.txt`→`txt.NewExt`；NameAndExtension：`txt.txt`→`NewName.NewExt`
  - CaseConversion=EveryWord 作用于名称部分不动扩展名：`hello world.txt`→`Hello World.txt`
  - 变量在文本模式也展开：替换串 `(${folder})` 展开父目录名
  - 新名==旧名 → Unchanged；计数器只对非 Unchanged 行递增（三行中第二行 Unchanged，编号为 0,?,1 → 断言 `0` 和 `1`）
  - 坏正则 `(` → Kind=RuleError/Error=BadPattern；超时模式 `(a+)+$` 输入 `'a'.Repeat(30)+'b'` → RuleError/RegexTimeout；引擎不抛异常
  - 引擎产出的 Changed 名再过 `RenameValidator`，非法 → RuleError/InvalidName（`京都_?` 用例）
- [ ] **步骤 2：跑测试确认失败**
- [ ] **步骤 3：实现**：按模板构造（Prepend/Append 语义 = 正则 `^`/`$` 前后缀注入）；三种模式统一走 `Regex.Replace`（文本=Escape、通配符=任务 1 转换、正则=原样，均 250ms 超时 + try/catch `RegexMatchTimeoutException/ArgumentException`）；先按 ApplyTo 拆名/扩，替换仅作用目标段，展开变量，再做大小写变换，最后整名校验；`RenameVariableExpander.Create` 每次 Run 重建（计数器跨行连续、行内独立）
- [ ] **步骤 4：跑测试通过**
- [ ] **步骤 5：Commit** `Add rename engine`

### 任务 5：冲突解析器

**文件：**
- 创建：`src/Files.App/Utils/Rename/RenameConflictResolver.cs`：`public sealed record ConflictInput(string Id, string FolderPath, string OriginalName, string ProposedName, IReadOnlySet<string> ExistingNamesInFolder);`（Existing 用 `StringComparer.OrdinalIgnoreCase` 集合）；`public sealed record ConflictOutcome(string Id, string FinalName, ConflictStatus Status);`、`public enum ConflictStatus { None, WillAppendNumber, WillSkip, WillOverwrite }`
- 方法：`public static IReadOnlyList<ConflictOutcome> Resolve(IReadOnlyList<ConflictInput> rows, ConflictStrategy strategy)`
- 测试：`tests/Files.App.UnitTests/Rename/RenameConflictResolverTests.cs`

- [ ] **步骤 1：写失败测试**：
  - 同夹两行 → 同一新名，AppendNumber：第一行保持、第二行 `京都_2053 (2).jpg`；Skip：第二行 WillSkip；Overwrite：第二行 WillOverwrite
  - 目标与已存在文件重名（ExistingNamesInFolder 含之）：AppendNumber→`名称 (2).ext`；Overwrite→WillOverwrite
  - **case-only 不冲突**：`IMG_2053.jpg`→`img_2053.jpg`，同夹无其他冲突 → Status=None
  - 加序号后仍重名继续 `(3)`（预取到不冲突为止）
  - 不同文件夹的同名互不影响
- [ ] **步骤 2：跑测试确认失败**
- [ ] **步骤 3：实现**（OrdinalIgnoreCase 比较；序号格式 `" {n}"` 插在扩展名前，n 从 2 起）
- [ ] **步骤 4：跑测试通过**
- [ ] **步骤 5：Commit** `Add rename conflict resolver`

### 任务 6：预设存储

**文件：**
- 创建：`src/Files.App/Utils/Rename/RenamePreset.cs`（`sealed record RenamePreset(string Name, RenameRule Rule, RenameScopeOption Scope, int Depth, bool IncludeFiles, bool IncludeFolders)`）
- 创建：`src/Files.App/Utils/Rename/RenamePresetStore.cs`：ctor`(string filePath)`；`Task<IReadOnlyList<RenamePreset>> LoadAsync()`（文件不存在→空表）；`Task SaveAsync(IReadOnlyList<RenamePreset> presets)`；内嵌 `RenamePresetsJsonContext : JsonSerializerContext`
- 修改：`src/Files.App/Helpers/Constants/.../LocalSettings`（`FileTagSettingsFileName` 同处）加 `RenamePresetsFileName = "rename_presets.json"`
- 修改：`tests/Files.App.UnitTests.csproj` 无需改（源生成在主项目）
- 测试：`tests/Files.App.UnitTests/Rename/RenamePresetStoreTests.cs`

- [ ] **步骤 1：写失败测试**：round-trip（构造含 2 个预设→Save→Load→字段逐一相等）；空文件/不存在文件→空表
- [ ] **步骤 2：跑测试确认失败**
- [ ] **步骤 3：实现**（临时文件用 `Path.GetTempPath()` 组合唯一名，测试自清理）
- [ ] **步骤 4：跑测试通过**
- [ ] **步骤 5：Commit** `Add rename preset store`

### 任务 7：作用域解析（枚举+深度）

**文件：**
- 创建：`src/Files.App/Utils/Rename/SubfolderDepth.cs`：`public static bool IsWithinDepth(string relativePath, int depth)`（`a\b` 为第 1 层）
- 创建：`src/Files.App/Utils/Rename/IRenameScopeResolver.cs` + `RenameScopeResolver.cs`：`Task<IReadOnlyList<RenameTarget>> ResolveAsync(RenameScopeOption option, int depth, bool includeFiles, bool includeFolders, IReadOnlyList<ListedItem> selectedItems, ListedItem currentFolderItem, CancellationToken ct)`
- 测试：`tests/Files.App.UnitTests/Rename/SubfolderDepthTests.cs`（纯逻辑：`"a"`→depth1 true/depth0 false；`"a\\b\\c"`→depth2 false、depth3 true）

- [ ] **步骤 1：写失败测试**（`SubfolderDepth.IsWithinDepth`）：`"a"`→depth1 true/depth0 false；`"a\\b\\c"`→depth2 false、depth3 true
- [ ] **步骤 2：跑测试确认失败**
- [ ] **步骤 3：实现 `SubfolderDepth` 并跑测试通过**
- [ ] **步骤 4：实现 `RenameScopeResolver`**（无单测，UI 集成验证）：SelectedItems→由 selectedItems 映射；CurrentFolder→`currentFolderItem` 所在 ShellViewModel 的 `FilesAndFolders`（排除 `..` 与当前文件夹项）；WithSubfolders→自当前目录 BFS，`GetFoldersAsync/GetFilesAsync` 递归 + `IsWithinDepth` 剪枝 + `ct` 取消；`ListedItem→RenameTarget` 映射含创建/修改时间（`ItemCreationTime/ItemDateModified`）与 `ParentFolderName`；计数在 VM 层统计返回行数
- [ ] **步骤 5：构建 Files.App 通过**
- [ ] **步骤 6：Commit** `Add rename scope resolver with depth filter`

### 任务 8：预览行模型 + ViewModel

**文件：**
- 创建：`src/Files.App/Utils/Rename/RenamePreviewRow.cs`：`public sealed class RenamePreviewRow(RenameTarget Target, RenameOutcome Outcome, ConflictOutcome Conflict)`；`string? ManualName`；`bool IsIncluded = true`；`public string FinalName`（ManualName ?? Conflict.FinalName ?? OriginalName）；`public RenameRowStatus Status`（`Unchanged/Changed/Manual/Sequenced/Skipped/Overwrite/Error`）；`public void SetManual(string name)`、`public void ResetManual()`
- 创建：`src/Files.App/ViewModels/Dialogs/BatchRenameViewModel.cs : ObservableObject`（ctor 注入 `IRenameScopeResolver`、`IRenameExecutor`；停靠侧与 LastPresetName 的读写归覆盖层（任务 10），VM 不碰设置）：Rule/Scope/Depth/IncludeFiles/IncludeFolders/ConflictStrategy 可观察属性；`RebuildPreview()`（防抖 200ms；engine→resolver→rows；**保留 ManualName 与 IsIncluded**）；`ApplyTemplate(RenameTemplate)`（切模板保留作用域/大小写/手改行）；`ApplyPatternPreset(string replaceText)`（把编号/日期模式串填入 ReplaceWith，其余状态不动）；筛选 `PreviewFilter{All,Changed,Sequenced}` + `SearchText`；剪贴板 `CopyOriginalNames()`（按行序、仅名称）/`PasteNewNames(string text)`（空行=跳过该行、多行截断并置 `PasteTruncated=true`、粘贴值走 SetManual）；统计 `Counts`
- 创建：`src/Files.App/Utils/Rename/IRenameExecutor.cs`（任务 9 实现）
- 测试：`tests/Files.App.UnitTests/Rename/BatchRenameViewModelTests.cs`（VM 不触 IO：resolver/executor 用假实现注入）

- [ ] **步骤 1：写失败测试**：
  - 手改行在 `RebuildPreview()`（换查找串、`ApplyTemplate(Prepend)`）后 `ManualName` 原样保留、Status=Manual；`ResetManual()` 后回到规则结果
  - `ApplyPatternPreset("照片_${padding=3;start=1}")` 后 ReplaceWith 被替换、Scope/大小写/手改行不变
  - `IsIncluded=false` 行不进执行计划、状态过滤可检出
  - Paste：3 行目标 + 粘贴 `"a\n\nb"`（空行跳过第 2 行）→ 行 1、3 Manual、行 2 保持；粘贴 5 行 → 只应用 3 行且 `PasteTruncated=true`
  - CopyOriginalNames 输出按行序换行拼接
  - 筛选：Changed 过滤掉 Unchanged；SearchText 对 改前/改后/位置 子串匹配
- [ ] **步骤 2：跑测试确认失败**
- [ ] **步骤 3：实现**（模型+VM；`ObservableObject` 来自 CommunityToolkit.Mvvm，参照 `BulkRenameDialogViewModel` 的引用方式）
- [ ] **步骤 4：跑测试通过**
- [ ] **步骤 5：构建 Files.App + Commit** `Add batch rename preview model and view model`

### 任务 9：执行器（整批撤销 + 覆盖回收站）

**文件：**
- 创建：`src/Files.App/Utils/Rename/RenameExecutor.cs : IRenameExecutor`，含三个类型：
  - `public sealed record RenameExecutionItem(string SourcePath, string DestinationPath, ConflictStatus Status)`
  - `public sealed record RenameExecutionResult(IReadOnlyList<(string Path, string Error)> Failed)`
  - 纯函数 `public static IReadOnlyList<RenameExecutionItem> BuildExecutionPlan(IReadOnlyList<RenamePreviewRow> rows, ConflictStrategy strategy)`
  - `Task<RenameExecutionResult> ExecuteAsync(IReadOnlyList<RenameExecutionItem> plan, IFilesystemHelpers fs, IProgress<string>? progress, CancellationToken ct)`
- 测试：`tests/Files.App.UnitTests/Rename/RenameExecutionPlanTests.cs`

- [ ] **步骤 1：写失败测试（BuildExecutionPlan）**：Unchanged/已取消勾选/Skipped 行被排除；Sequenced 行新名含 ` (2)`；行序=列表序；**逆序回放安全性**：`(a→b),(b→c)` 计划存在时，逆序 (b→c 还原 b) 后 (a→b 还原 a) 无中间冲突（断言计划包含成对 source/destination 且回放序为逆）
- [ ] **步骤 2：跑测试确认失败**
- [ ] **步骤 3：实现 BuildExecutionPlan 并跑测试通过**
- [ ] **步骤 4：实现 ExecuteAsync**：逐项 `fs.RenameAsync(StorageHelpers.FromPathAndType(...), newName, NameCollisionOption.FailIfExists, registerHistory:false, showExtensionDialog:false)`；Overwrite 行先回收删除目标（复用 `DeleteItemAsync` 等效入口：参照 `Actions/FileSystem/DeleteAction.cs` 的删除调用，permanently:false、不记历史）再改名；case-only 目标名（新旧名 OrdinalIgnoreCase 相等）如被误报"已存在"，改走与 `BaseGroupableLayoutPage.CommitRenameAsync` 相同的 case-rename 处理路径；全部成功后构造**一条** `new StorageHistory(FileOperationType.Rename, sources, destinations)` 并 `App.HistoryWrapper.AddHistory(...)`；单项失败收集进 `Failed`（含原因），不中断
- [ ] **步骤 5：构建 + 跑 Rename 过滤单测通过**
- [ ] **步骤 6：Commit** `Add rename executor with single-history undo`

### 任务 10：设置服务 + 覆盖层 UI + 键盘

**文件：**
- 创建：`src/Files.App/Data/Contracts/IBatchRenameSettingsService.cs`（`RulePanelDockedLeft`(默认 false)、`LastPresetName`(默认 "")；`IBaseSettingsService, INotifyPropertyChanged`）
- 创建：`src/Files.App/Services/Settings/BatchRenameSettingsService.cs`（`BaseObservableJsonSettings`，仿 `AppearanceSettingsService`）
- 修改：`Data/Contracts/IUserSettingsService.cs` + `Services/Settings/UserSettingsService.cs` 聚合；`Helpers/Application/AppLifecycleHelper.cs` 注册 singleton
- 创建：`src/Files.App/UserControls/BatchRename/BatchRenameOverlayPresenter.xaml(.cs)`（布局=原型 v11：标题栏[图标/标题/作用域摘要/⇄停靠/最大化/关闭]、规则栏 300px 右侧默认、预览区[三段筛选+计数/剪贴板两按钮/搜索/四列列表+分组/底栏 冲突条+策略下拉+统计+取消/应用]；`VisualStateManager` 两组状态：Normal/Maximized、DockRight/DockLeft；`IsMaximized` 切换覆盖层 inset；静态 `Instance` 模式仿 `MainWindow.Instance`）
- 修改：`src/Files.App/Views/MainPage.xaml` 根 Grid 加 `<uc:BatchRenameOverlayPresenter Grid.RowSpan="3" x:Name="BatchRenameOverlay" Visibility="Collapsed"/>`
- 修改：`src/Files.App/Strings/en-US/Resources.resw` 新增全部键（BatchRename 系列：标题/查找/替换为/模式三段/应用到三段/匹配全部/区分大小写/模板三枚/前缀/后缀/跳过已含前缀/插入在扩展名之前/作用域三选/深度/包含文件/文件夹/变量三 chip/两模式预设/大小写五选/筛选三段/搜索占位/复制改前/粘贴新名称/冲突条/策略三选/统计文案/取消/应用重命名/行标签：手动/将加序号/已跳过/未改动/不匹配/未包含文件夹/非法字符/已取消勾选；对照原型 v11 文案逐条英译）

- [ ] **步骤 1：实现设置服务**（照抄 Appearance 链路五件套：契约/实现/聚合/注册/无需 serialization context 改动——bool/string 已可序列化）
- [ ] **步骤 2：实现覆盖层 XAML + code-behind**：`public void Open(IShellPage shellPage, IReadOnlyList<ListedItem> selectedItems)`（构造 VM；默认作用域=有选中 SelectedItems 否则 CurrentFolder；Open 时禁用全局"显示/隐藏隐藏项"的 Ctrl+H 加速器、Close 时恢复）；`Close()`；键盘：根 `KeyDown`——`Ctrl+H`→聚焦查找框并 `Handled=true`；`Esc`→编辑态先退出再 Close；`F2`→下一可编辑行进入编辑；`Enter`→非编辑态触发应用。虚拟化：`ListView`+`CollectionViewSource`（IsSourceGrouped=true，键=文件夹相对路径）；行模板含勾选/位置/改前(差异红删)/改后(绿增+标签)/编辑态；双击改后格进入编辑、↺ 重置；规则栏/预览区宽度与停靠读 `RulePanelDockedLeft`，⇄ 按钮写回设置；预设下拉读写 `RenamePresetStore` 并把最后使用的预设名存 `LastPresetName`
- [ ] **步骤 3：构建 Files.App 通过**（此任务无单测；行为验证在任务 12）
- [ ] **步骤 4：Commit** `Add batch rename overlay UI and settings`

### 任务 11：入口接线 + 移除旧弹窗

**文件：**
- 创建：`src/Files.App/Actions/FileSystem/BatchRenameAction.cs`（`IAction` + `[GeneratedRichCommand]`，`Label/Description/Glyph` 走 resw/Fluent 字形；无全局快捷键；`IsExecutable => context.IsFolderSectionItem`? —— 以 `ContentPageContext` 有活动页面为准，参照相邻 Action 写法；Execute→`BatchRenameOverlayPresenter.Instance?.Open(...)` 传当前 pane 的选中项）
- 修改：`src/Files.App/Data/Commands/Manager/CommandManager.cs`（`CommandCodes` 枚举加 `BatchRename` + 注册实例，照相邻命令抄）
- 修改：`src/Files.App/Actions/FileSystem/RenameAction.cs:45-57` 多选分支改为打开覆盖层
- 修改：`src/Files.App/Data/Factories/ContentPageContextFlyoutFactory.cs`：`GetBaseItemMenuItems` 加两项——选中项菜单（`IsVisible = itemsSelected && selectedItems.Count >= 2`）与空白处菜单（`IsVisible = !itemsSelected`），`new ContextMenuFlyoutItemViewModelBuilder(Commands.BatchRename).Build()`
- 修改：`src/Files.App/Data/Items/ToolbarSections.cs`：默认模板加 BatchRename 条目（用户可在自定义工具栏移除）
- 删除：`src/Files.App/Dialogs/BulkRenameDialog.xaml(.cs)`、`src/Files.App/ViewModels/Dialogs/BulkRenameDialogViewModel.cs`；修改 `src/Files.App/Services/App/AppDialogService.cs:29` 移除注册
- resw：删除仅被旧弹窗使用的键（`BulkRename` 保留——菜单/工具栏新文案用新键 `BatchRename…`）

- [ ] **步骤 1：实现 Action/CommandManager/菜单/工具栏/RenameAction 分支**
- [ ] **步骤 2：删除旧弹窗三文件与注册行**
- [ ] **步骤 3：构建通过**（若 resw 键被生成器引用报错，按报错补齐/清理键）
- [ ] **步骤 4：Commit** `Wire batch rename entries and remove legacy bulk rename dialog`

### 任务 12：全量验证 + 手工 QA

- [ ] **步骤 1：跑全部 Rename 单测**：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Rename"` → 全 PASS
- [ ] **步骤 2：全量构建**：`msbuild -restore Files.slnx -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly` → 无错误
- [ ] **步骤 3：手工 QA 清单**（对照规格 §2 目标逐条；部署运行 Debug 版）：
  四入口可达；默认作用域规则（有/无选中）；三模式+通配符；模板三枚切换保留手改行；作用域切子文件夹+深度 2 时列表出现分组与位置列且计数仅当前项；冲突三策略实际行为（含覆盖→回收站、Ctrl+Z 只还原改名）；坏正则不崩溃且禁用应用；case-only 改名成功；逐项手改+↺；预设存/取/删；复制/粘贴名称（空行跳过、截断提示）；最大化/还原+⇄停靠记忆；Ctrl+H 弹窗内聚焦、关闭后恢复"显示隐藏项"；Esc 直接关；整批 Ctrl+Z 一次还原；5000 项流畅滚动
  未尽事项记录进任务备注，不静默跳过
- [ ] **步骤 4：Commit**（如有修补）`Finalize batch rename`
