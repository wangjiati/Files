# 导出文件列表实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在工具栏提供「导出文件列表」，把当前文件夹内容（可选筛选、可选递归子文件夹）导出为 CSV 或零依赖手写 OpenXML 的 xlsx。

**架构：** 纯逻辑层（列/行模型、行工厂、CSV/xlsx 写入器、排序器、枚举器）全部可单元测试；UI 层是一个 `[GeneratedRichCommand]` 动作 + ContentDialog 对话框 + StatusCenter 完成卡片；筛选复用现有 `DirectoryFilterSpec`/`DirectoryFilterEvaluator`，不复制筛选逻辑。

**技术栈：** .NET 10、WinUI 3、MSTest、System.IO.Compression（已有依赖，无新增包）。

**规格：** `docs/superpowers/specs/2026-10-31-export-file-list-design.md`

## 全局约束

- 不新增 NuGet 依赖；无运行时反射、无动态代码生成（AOT/裁剪安全）。
- CSV：UTF-8 带 BOM、逗号分隔、RFC 4180 转义、CRLF 行结束。
- xlsx：ZipArchive + 手写 OpenXML，单工作表、表头加粗（静态 styles.xml）、不设列宽、inline string、日期为 ISO 文本。
- 日期输出本地时间 `yyyy-MM-dd HH:mm:ss`；`DateTimeOffset.MinValue`/default 视为未知输出空。
- 文件行大小(字节)写真实值（含 0）；文件夹行留空，不得写 0。
- 标签列输出显示名称（分号+空格连接），不是内部 ID。
- 递归顺序：每层文件夹行在子项之前；层内按当前 `SortOption`/`SortDirection`，不支持的排序选项回退按名称；文件夹排在文件前。
- 文本文件保持 CRLF、遵循 `.editorconfig`；新 UI 文本必须走资源（en-US + zh-Hans）。
- 测试命令：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~<测试类名>"`。
- 应用构建命令（UI 任务用，见 AGENTS.md）：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`。

## 审查重点（Review Focus）

- **字段里的逗号/引号/换行/等号开头文件名**：`=cmd`、`a,b`、`say "hi"`、带换行的名称必须原样往返（CSV 转义后可还原，xlsx XML 转义 &<>），等号开头不消毒（数据保真优先，内部工具接受公式注入风险）——任务 2/3 测试钉住。
- **未知值腐蚀**：未知日期（MinValue）变成 `0001-01-01`、文件夹大小变成 0，是下游统计最容易被坑的脏数据——任务 1 测试钉住。
- **目录联接（junction）自环**：递归遇到指向祖先的 junction 若照常下钻会死循环——跳过 ReparsePoint 目录的下钻（行本身保留）——任务 5 测试用真实 junction 钉住。
- **xlsx 结构非法**：Excel 对结构错误直接拒开，必须能用 `ZipFile` 重新解包且 XML 良构——任务 3 测试钉住；最终任务加 Excel 实际打开的手动验证。
- **搜索页/库根/MTP 状态泄漏到对话框**：选项可用性规则错一处就是运行期异常或空导出——任务 6 状态机测试全枚举钉住。

## 文件结构

创建（`src/Files.App/Utils/Storage/Exporting/`）：

- `FileListExportColumn.cs`：列枚举 + 目录顺序 + 默认勾选集 + 表头资源键。
- `FileListExportRow.cs`：一行数据的 record。
- `FileListExportRowFactory.cs`：`ListedItem`/`FileSystemInfo` → 行；相对路径；日期格式化。
- `FileListExportSorter.cs`：`(SortOption, SortDirection)` → 行比较器。
- `FileListCsvWriter.cs`：CSV 写入器（`Stream` 目标）。
- `FileListXlsxWriter.cs`：xlsx 写入器（`Stream` 目标）。
- `FileListExportContext.cs`：`FileListExportFormat` 枚举 + `SkippedCount` 计数上下文。
- `FileListExportEnumerator.cs`：当前层包装 + 递归枚举（可注入目录列表委托）+ 筛选求值。

创建（UI）：

- `src/Files.App/Actions/FileSystem/ExportFileListAction.cs`
- `src/Files.App/Dialogs/ExportFileListDialog.xaml` + `.xaml.cs`
- `src/Files.App/ViewModels/Dialogs/ExportFileListDialogViewModel.cs`（含 `ExportFileListColumnViewModel`）

修改：

- `src/Files.App/ViewModels/ShellViewModel.cs`：新增 `GetFullItemListSnapshot()`。
- `src/Files.App/UserControls/Toolbar.xaml`：新增导出按钮（置于 `SqlQueryButton` 之后）。
- `src/Files.App/Data/Enums/FileOperationType.cs`：新增 `ExportFileList = 14`。
- `src/Files.App/Utils/StatusCenter/StatusCenterHelper.cs`：新增 `AddCard_ExportFileList` + `switch (card.Operation)` 新 case。
- `src/Files.App/Strings/en-US/Resources.resw`、`src/Files.App/Strings/zh-Hans/Resources.resw`：全部新键。

测试（`tests/Files.App.UnitTests/Exporting/`）：`FileListExportRowFactoryTests.cs`、`FileListCsvWriterTests.cs`、`FileListXlsxWriterTests.cs`、`FileListExportSorterTests.cs`、`FileListExportEnumeratorTests.cs`、`ExportFileListDialogViewModelTests.cs`。

---

### 任务 1：列定义、行模型与行工厂

**文件：**
- 创建：`src/Files.App/Utils/Storage/Exporting/FileListExportColumn.cs`、`FileListExportRow.cs`、`FileListExportRowFactory.cs`
- 测试：`tests/Files.App.UnitTests/Exporting/FileListExportRowFactoryTests.cs`

- [ ] **步骤 1：编写失败的测试**

测试类骨架（断言必须齐全）：

```csharp
[TestClass]
public sealed class FileListExportRowFactoryTests
{
    // ListedItem 有公共无参构造，直接对象初始化器赋值

    [TestMethod]
    public void FromListedItem_FolderSizeStaysEmpty()
    // PrimaryItemAttribute = StorageItemTypes.Folder, FileSize = "", FileSizeBytes = 0
    // 断言 row.SizeBytes 为 null

    [TestMethod]
    public void FromListedItem_ZeroByteFileExportsZero()
    // File 项, FileSize = "0 bytes", FileSizeBytes = 0
    // 断言 row.SizeBytes == 0（不是 null）

    [TestMethod]
    public void FromListedItem_UnknownDatesBecomeNull()
    // ItemDateCreatedReal/ItemDateModifiedReal = default
    // 断言 DateCreated/DateModified 为 null

    [TestMethod]
    public void FromListedItem_KnownDatesFormatAsIsoLocal()
    // 2026-10-31 15:04:05 +08:00 → 断言 FormatDate 输出 "2026-10-31 15:04:05"

    [TestMethod]
    public void GetRelativePath_RootItemIsDot_Prefixed()
    // root=C:\A, path=C:\A\b.txt → ".\b.txt"; path=C:\A\Sub\c.txt → ".\Sub\c.txt"

    [TestMethod]
    public void FromFileSystemInfo_BuildsRowFromFindData()
    // 临时目录建 "d.txt"（内容 3 字节）与子目录 "Sub"
    // FileInfo→row: SizeBytes==3, IsFolder==false; DirectoryInfo→row: SizeBytes 为 null, IsFolder==true
    // Hidden 属性映射 IsHidden；扩展名含点（".txt"），无扩展名为 null
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FileListExportRowFactoryTests"`
预期：编译失败，类型不存在。

- [ ] **步骤 3：实现三个类型**

```csharp
public enum FileListExportColumn : ushort
{ Name, Extension, ItemType, SizeBytes, SizeDisplay, DateCreated, DateModified, FullPath, RelativePath, IsFolder, IsHidden, FileTags }

public static class FileListExportColumns
{
    public static readonly IReadOnlyList<FileListExportColumn> All;          // 按上面枚举序
    public static IReadOnlyList<FileListExportColumn> DefaultSelection;      // Name, ItemType, SizeBytes, DateModified, FullPath
    public static string GetHeaderResourceKey(FileListExportColumn column);  // "ExportFileList_Column" + 列名
}

public sealed record FileListExportRow(
    string Name, string? Extension, string? ItemType,
    long? SizeBytes, string? SizeDisplay,
    DateTimeOffset? DateCreated, DateTimeOffset? DateModified,
    string FullPath, string RelativePath,
    bool IsFolder, bool IsHidden, string? TagNames);

public static class FileListExportRowFactory
{
    public static FileListExportRow FromListedItem(ListedItem item, string rootPath, string? tagNames);
    public static FileListExportRow FromFileSystemInfo(FileSystemInfo info, string rootPath, string? tagNames);
    public static string GetRelativePath(string rootPath, string fullPath);   // 根内 ".\\" 前缀，路径分隔符统一 '\\'
    public static string FormatDate(DateTimeOffset? value);                   // null→""，否则本地时间 yyyy-MM-dd HH:mm:ss
}
```

映射注意：`FromListedItem` 未知日期判定用 `value == default`；大小判定 `string.IsNullOrEmpty(item.FileSize) ? null : item.FileSizeBytes`（与 `ShellViewModel.CreateFilterSnapshot` 一致）；`IsFolder = PrimaryItemAttribute == StorageItemTypes.Folder`。文件系统路径来源用 `info.FullName`；`FromFileSystemInfo` 的日期取 `CreationTime`/`LastWriteTime`（本地时间）转 `DateTimeOffset`。

- [ ] **步骤 4：运行测试验证通过**

运行同步骤 2。预期：全部 PASS。

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/Utils/Storage/Exporting/ tests/Files.App.UnitTests/Exporting/
git commit -m "Add file list export column and row model"
```

### 任务 2：CSV 写入器

**文件：**
- 创建：`src/Files.App/Utils/Storage/Exporting/FileListCsvWriter.cs`
- 测试：`tests/Files.App.UnitTests/Exporting/FileListCsvWriterTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
public static void Write(Stream stream, IReadOnlyList<(FileListExportColumn Column, string Header)> columns, IEnumerable<FileListExportRow> rows);
// 读回：MemoryStream → Encoding.UTF8.GetString，按 "\r\n" 分行

[TestMethod] Write_EmitsBomAndCrlfAndLocalizedHeaders
// 字节流以 EF BB BF 开头；首行 = 各 Header 逗号连接；行尾 \r\n（含末行）

[TestMethod] Write_EscapesSpecialFieldsPerRfc4180
// Name="a,b"、Name="say \"hi\""、Name="line1\nline2" → 字段被引号包裹且内容原样可还原（引号翻倍）
// Name="=cmd" → 原样输出 "=cmd"（不消毒，钉住数据保真决策）

[TestMethod] Write_EmptyValuesStayEmpty
// SizeBytes=null（文件夹）→ 空字段；SizeBytes=0 → "0"；DateModified=null → 空字段

[TestMethod] Write_BoolColumnsAsLowercaseText
// IsFolder → "true"/"false"
```

- [ ] **步骤 2：运行测试验证失败**（同任务 1 命令，过滤 `FileListCsvWriterTests`；预期编译失败）

- [ ] **步骤 3：实现写入器**

单遍流式写入：`StreamWriter(stream, new UTF8Encoding(true))`；字段规则：null→空、含逗号/引号/CRLF/LF→引号包裹+引号翻倍、其余原样。日期经 `FileListExportRowFactory.FormatDate`。不缓冲整个行集（IEnumerable 逐行写完即弃）。

- [ ] **步骤 4：运行测试验证通过**

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/Utils/Storage/Exporting/FileListCsvWriter.cs tests/Files.App.UnitTests/Exporting/FileListCsvWriterTests.cs
git commit -m "Add CSV writer for file list export"
```

### 任务 3：xlsx 写入器

**文件：**
- 创建：`src/Files.App/Utils/Storage/Exporting/FileListXlsxWriter.cs`
- 测试：`tests/Files.App.UnitTests/Exporting/FileListXlsxWriterTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
public static void Write(Stream stream, IReadOnlyList<(FileListExportColumn Column, string Header)> columns, IEnumerable<FileListExportRow> rows);
// 读回：ZipFile.OpenRead → 逐 entry 断言

[TestMethod] Write_ProducesReopenablePackageWithRequiredParts
// entries 含 "[Content_Types].xml"、"_rels/.rels"、"xl/workbook.xml"、"xl/_rels/workbook.xml.rels"、"xl/worksheets/sheet1.xml"、"xl/styles.xml"
// 各 XML 用 XDocument.Parse 断言良构；workbook.xml 的 sheet name 取自调用方传入的 sheetName

[TestMethod] Write_HeaderRowUsesBoldStyle_FieldsEscaped
// sheet1.xml：首行单元格 s="1"（指向 styles.xml 中加粗字体 xf）；文本用 inline string (t="inlineStr")
// Name="a<b>&c" 在 XML 中以转义形式出现（&lt; &gt; &amp;），解包 XDocument 读回后等于原文

[TestMethod] Write_NumericAndBoolAndDateCells
// SizeBytes → 原生数值单元格（无 t 属性），0 也输出 0；null → 空单元格
// IsFolder/IsHidden → 文本 "true"/"false"；日期 → ISO 文本（与 CSV 同 FormatDate）
```

（方法签名含工作表名：`Write(Stream, string sheetName, columns, rows)`；测试中 sheetName 传 "FileList"。）

- [ ] **步骤 2：运行测试验证失败**

- [ ] **步骤 3：实现写入器**

固定最小结构：`[Content_Types].xml`（默认 xml/rels + worksheet/styles 显式覆盖）、`_rels/.rels`（指向 workbook）、`xl/workbook.xml`（声明 r + 一个 sheet，r:id="rId1"）、`xl/_rels/workbook.xml.rels`（rId1→worksheet、rId2→styles）、静态 `xl/styles.xml`（font[0] 常规、font[1] 加粗、cellXfs：xf0 常规、xf1 bold）、`xl/worksheets/sheet1.xml`（`XmlWriter` 流式写 sheetData）。数值列仅 `SizeBytes`；文本列 `t="inlineStr"` + `<is><t>`；表头行所有单元格 `s="1"`。

- [ ] **步骤 4：运行测试验证通过**

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/Utils/Storage/Exporting/FileListXlsxWriter.cs tests/Files.App.UnitTests/Exporting/FileListXlsxWriterTests.cs
git commit -m "Add minimal OpenXML xlsx writer for file list export"
```

### 任务 4：导出排序器

**文件：**
- 创建：`src/Files.App/Utils/Storage/Exporting/FileListExportSorter.cs`
- 测试：`tests/Files.App.UnitTests/Exporting/FileListExportSorterTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
public static IComparer<FileListExportRow> Create(SortOption option, SortDirection direction);

[TestMethod] FoldersAlwaysFirst_RegardlessOfOptionAndDirection
// (SizeBy? 用 SortOption.Size descending) 文件夹行始终在文件行前

[TestMethod] SortsBy_Size_Name_DateModified_DateCreated_Extension_BothDirections
// 各选项按对应字段；desc 反转（文件夹优先不反转）

[TestMethod] UnsupportedOptionFallsBackToName
// SortOption.SyncStatus / DateDeleted → 按名称排序
```

- [ ] **步骤 2：运行测试验证失败**

- [ ] **步骤 3：实现排序器**

支持 `Name, Size, DateModified, DateCreated, Extension, FileType, Path`（对照 `SortOption` 实际成员名，枚举以代码库为准），其余回退 Name。null 大小/日期排最后；名称比较用 `NaturalStringComparer.GetForProcessor()`（与 `SortingHelper` 一致）。

- [ ] **步骤 4：运行测试验证通过**

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/Utils/Storage/Exporting/FileListExportSorter.cs tests/Files.App.UnitTests/Exporting/FileListExportSorterTests.cs
git commit -m "Add sorter for file list export rows"
```

### 任务 5：导出枚举器（当前层 + 递归 + 筛选）

**文件：**
- 创建：`src/Files.App/Utils/Storage/Exporting/FileListExportContext.cs`、`FileListExportEnumerator.cs`
- 测试：`tests/Files.App.UnitTests/Exporting/FileListExportEnumeratorTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
public sealed class FileListExportContext { public long SkippedCount { get; } public void AddSkipped(int count = 1); }
public enum FileListExportFormat { Csv, Excel }

public static class FileListExportEnumerator
{
    public static IEnumerable<FileListExportRow> EnumerateCurrentLevel(
        IReadOnlyList<ListedItem> items, string rootPath, Func<ListedItem, string?> tagNamesSelector);

    public static IEnumerable<FileListExportRow> EnumerateRecursive(
        IReadOnlyList<FileListExportRow> currentLevelRows, string rootPath,
        SortOption sortOption, SortDirection sortDirection,
        DirectoryFilterSpec? filterSpec,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tagsByPath,
        FileListExportContext context,
        Func<string, IReadOnlyList<FileSystemInfo>>? listDirectory = null);
}
```

测试用真实临时目录（`Path.GetTempPath` 下建 `Guid` 目录，Teardown 删除）：

```csharp
[TestMethod] Recursive_WalksFolderRowBeforeChildrenInSortedOrder
// root/{a.txt, Sub1/{c.txt}, Sub2/{b.txt}}
// 行序：root 行（含 Sub1/Sub2 文件夹行，按传入排序器序）→ Sub1 行 → Sub1 内容 → Sub2 行 → Sub2 内容

[TestMethod] Recursive_AppliesFilterSpecToSubfolderItemsOnly
// filterSpec 名称 Contains "b"：Sub2/b.txt 保留、Sub2 自身文件夹行被筛掉但 Sub2 子树仍被遍历且其内容照常过滤

[TestMethod] Recursive_JunctionIsEmittedButNotTraversed
// mklink /J root\Loop root（Process 启动 mklink，无需管理员）
// Loop 的文件夹行出现一次，其下不再产生任何行（含 root 自身重复行），进程不死循环

[TestMethod] Recursive_UnlistableDirectoryIncrementsSkipAndContinues
// listDirectory 注入：某目录抛 UnauthorizedAccessException → SkippedCount 增、其余目录照常产出

[TestMethod] Recursive_HiddenFilesAreIncluded
// 隐藏文件（File.SetAttributes Hidden）也产出且 IsHidden=true

[TestMethod] CurrentLevel_MapsListedItemsInOrder
// 传入 ListedItem 列表 → 行序一致、TagNames 走 selector
```

- [ ] **步骤 2：运行测试验证失败**

- [ ] **步骤 3：实现枚举器**

- `EnumerateCurrentLevel`：逐项 `FileListExportRowFactory.FromListedItem`，保持传入顺序。
- `EnumerateRecursive`：先逐个 yield `currentLevelRows`；随后 DFS：对当前层（含根）每个文件夹行，`listDirectory`（默认 `p => new List<FileSystemInfo>(Directory.EnumerateFileSystemEntries(p))`，`EnumerationOptions` 不可注入时等效忽略异常并计入 Skipped）列出条目 → 建 `FileListExportRow`（tags 查 `tagsByPath`，join "; "）→ 若 `filterSpec` 非空：该批条目构造 `DirectoryItemFilterSnapshot`（ItemId=序号、RawName/DisplayName=Name、FullPath、Extension、ItemType=null、TagIds=tagsByPath 原始列表、SizeBytes=FileInfo 长度否则 null、ModifiedUtc/CreatedUtc=本地时间转 UTC、IsFolder）→ `DirectoryFilterEvaluator.Evaluate` 取命中集 → 命中的文件夹行 yield 并下钻、命中的文件行 yield；无 filter 全部 yield。层内排序用 `FileListExportSorter.Create(sortOption, sortDirection)`。`(Attributes & FileAttributes.ReparsePoint) != 0` 的目录只出行不下钻。单个条目读取属性抛异常 → `context.AddSkipped()` 并跳过该条。
- 快照日期语义与 `ShellViewModel.CreateFilterSnapshot` 对齐（未知→null）。

- [ ] **步骤 4：运行测试验证通过**

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/Utils/Storage/Exporting/FileListExportContext.cs src/Files.App/Utils/Storage/Exporting/FileListExportEnumerator.cs tests/Files.App.UnitTests/Exporting/FileListExportEnumeratorTests.cs
git commit -m "Add file list export enumerator with recursion and filtering"
```

### 任务 6：导出对话框（ViewModel + XAML + 资源）

**文件：**
- 创建：`src/Files.App/ViewModels/Dialogs/ExportFileListDialogViewModel.cs`、`src/Files.App/Dialogs/ExportFileListDialog.xaml`、`.xaml.cs`
- 修改：`src/Files.App/Strings/en-US/Resources.resw`、`src/Files.App/Strings/zh-Hans/Resources.resw`
- 测试：`tests/Files.App.UnitTests/Exporting/ExportFileListDialogViewModelTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
public sealed partial class ExportFileListDialogViewModel : ObservableObject
{
    public ExportFileListDialogViewModel(bool isSearchResultsPage, bool hasActiveFilter, bool supportsRecursion);
    public bool IsFilterOptionEnabled { get; }        // !isSearchResultsPage && hasActiveFilter
    public bool ApplyCurrentFilter { get; set; }      // 默认 true
    public bool IsRecursionEnabled { get; }           // supportsRecursion && !isSearchResultsPage
    public bool IncludeSubfolders { get; set; }       // 默认 false
    public IReadOnlyList<ExportFileListColumnViewModel> Columns { get; }  // 12 列，默认勾选集见 FileListExportColumns.DefaultSelection
    public FileListExportFormat Format { get; set; }  // 默认 Csv
    public bool CanExport { get; }                    // 任一列勾选
}
public sealed partial class ExportFileListColumnViewModel : ObservableObject
{ public FileListExportColumn Column { get; } public string Label { get; } public bool IsChecked { get; set; } }

[TestMethod] SearchPage_LocksFilterOnAndDisablesBothOptions
[TestMethod] NoActiveFilter_DisablesFilterOption_RecursionStillAvailable
[TestMethod] DefaultSelection_MatchesCatalogDefaults_AndFormatIsCsv
[TestMethod] UncheckingAllColumns_DisablesExport
[TestMethod] TogglingOneColumn_ReEnablesExport
```

- [ ] **步骤 2：运行测试验证失败**

- [ ] **步骤 3：实现 ViewModel、XAML 与资源**

- ViewModel 如上签名；`Label` 用 `"ExportFileList_Column{Name}".GetLocalizedResource()` 风格（`GetLocalizedResource()` 字符串扩展，参照 StatusCenter 资源键用法）。
- XAML 参照 `DecompressArchiveDialog`：ContentDialog，`PrimaryButtonText/SecondaryButtonText` 走资源，`IsPrimaryButtonEnabled` 绑定 `ViewModel.CanExport`；两组 CheckBox + 列勾选列表 + 两个 RadioButton（CSV/Excel）；不可用选项 `IsEnabled="False"` 且旁注资源文案（如搜索页「视图即搜索结果」提示）。`.xaml.cs` 暴露 `ViewModel` 依赖属性或公共属性（同 DecompressArchiveDialog 的 `ViewModel` 属性模式）。
- resw 新键（en-US + zh-Hans 同步）：`ExportFileList`、`ExportFileListDescription`、`ExportFileListDialog_Header`、`ExportFileListDialog_FilterOption`、`ExportFileListDialog_FilterLockedHint`、`ExportFileListDialog_RecursiveOption`、`ExportFileListDialog_ColumnsHeader`、`ExportFileListDialog_FormatHeader`、`ExportFileListDialog_FormatCsv`、`ExportFileListDialog_FormatExcel`、`ExportFileListDialog_ExportButton`、`ExportFileList_ColumnName/Extension/ItemType/SizeBytes/SizeDisplay/DateCreated/DateModified/FullPath/RelativePath/IsFolder/IsHidden/FileTags`、`ExportFileList_SheetName`。

- [ ] **步骤 4：运行测试验证通过；构建应用**

运行测试过滤 `ExportFileListDialogViewModelTests`；然后 `msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`，预期 0 错误。

- [ ] **步骤 5：Commit**

```bash
git add src/Files.App/ViewModels/Dialogs/ExportFileListDialogViewModel.cs src/Files.App/Dialogs/ExportFileListDialog.xaml src/Files.App/Dialogs/ExportFileListDialog.xaml.cs src/Files.App/Strings/en-US/Resources.resw src/Files.App/Strings/zh-Hans/Resources.resw tests/Files.App.UnitTests/Exporting/ExportFileListDialogViewModelTests.cs
git commit -m "Add export file list dialog"
```

### 任务 7：动作、工具栏按钮与执行组装

**文件：**
- 创建：`src/Files.App/Actions/FileSystem/ExportFileListAction.cs`
- 修改：`src/Files.App/ViewModels/ShellViewModel.cs`、`src/Files.App/UserControls/Toolbar.xaml`、`src/Files.App/Data/Enums/FileOperationType.cs`、`src/Files.App/Utils/StatusCenter/StatusCenterHelper.cs`、两个 `Resources.resw`
- 测试：本任务为 UI 组装，无新单元测试；以构建 + 手动验证清单收尾。

- [ ] **步骤 1：ShellViewModel 暴露全集快照**

新增 `public IReadOnlyList<ListedItem> GetFullItemListSnapshot() => filesAndFolders.ToList();`（`filesAndFolders` 为现有私有 `ConcurrentCollection<ListedItem>`，保持排序）。

- [ ] **步骤 2：FileOperationType 与 StatusCenter 卡片**

- `FileOperationType` 增加 `ExportFileList = 14`。
- `StatusCenterHelper` 增加 `AddCard_ExportFileList(ReturnResult returnStatus, string destinationPath, long itemsCount, long skippedCount)`：`AddItem("StatusCenter_ExportFileListComplete_Header"/"_SubHeader"（或 Failed 键）, ReturnResult.Success/Failed, FileOperationType.ExportFileList, source: 空枚举, destination: destinationPath 单元素, false, itemsCount, skippedCount)`（`totalSize` 位承载 skipped 数，在方法注释注明）。在 `UpdateCardStrings` 的 `switch (card.Operation)` 增加 case：Header = 完成键 `.GetLocalizedFormatResource(card.TotalItemsCount)`；SubHeader = 子头键 `.GetLocalizedFormatResource(card.TotalItemsCount, card.TotalSize)`（resw 文案 `{0} 项已导出` / `跳过 {1} 项无法访问`；skipped=0 时文案写成「无跳过」也接受——两键固定格式即可）。resw 补 4 个 StatusCenter 键（en-US + zh-Hans）。

- [ ] **步骤 3：实现 ExportFileListAction**

```csharp
[GeneratedRichCommand]
internal sealed partial class ExportFileListAction : ObservableObject, IAction
```

- `Label => "ExportFileList".GetLocalizedResource()`；`Description => "ExportFileListDescription".GetLocalizedResource()`；`Glyph => new RichGlyph(baseGlyph: "\uE8E5")`（导出图标，可视觉微调）。
- `IsExecutable`：`context.PageType is ContentPageTypes.Folder or ContentPageTypes.Library or ContentPageTypes.CloudDrive or ContentPageTypes.SearchResults or ContentPageTypes.MtpDevice or ContentPageTypes.Ftp` 且 `context.ShellPage?.ShellViewModel` 非空（`IContentPageContext` 用法参照 `InstallFontAction`）。
- `ExecuteAsync`：
  1. 取 `shellViewModel = context.ShellPage.GetRequiredShellViewModel()`、`workingDirectory`；空则 return。
  2. `isSearchPage = context.PageType == ContentPageTypes.SearchResults`；`hasActiveFilter = isSearchPage || !shellViewModel.DirectoryFilter.IsEmpty`；`supportsRecursion = context.PageType is ContentPageTypes.Folder or ContentPageTypes.CloudDrive`。
  3. 构造 VM + `ExportFileListDialog { ViewModel = vm, XamlRoot = MainWindow.Instance.Content.XamlRoot }`；`TryShowAsync()` 非 Primary 则 return。
  4. `FileSavePicker`：`InitializeWithWindow`（照抄 `AdvancedViewModel` 模式），`SuggestedFileName = $"{目录名}_FileList_{DateTime.Now:yyyyMMdd}"`，`FileTypeFilter` 按所选格式加入（".csv" 或 ".xlsx"）；`PickSaveFileAsync()` 空则 return。
  5. 组装行序列（`Task.Run` 内）：根层行 = `vm.ApplyCurrentFilter ? shellViewModel.FilesAndFolders.ToList() : shellViewModel.GetFullItemListSnapshot()` → `EnumerateCurrentLevel(items, workingDirectory, item => string.Join("; ", item.FileTagsUI?.Select(t => t.Name) ?? []))`；`vm.IncludeSubfolders` 时套 `EnumerateRecursive(根层行, workingDirectory, folderSettings.DirectorySortOption, folderSettings.DirectorySortDirection, vm.ApplyCurrentFilter ? shellViewModel.DirectoryFilter : null, 标签map, context)`，标签 map 来自 `FileTagsHelper.GetDbInstance().GetAllUnderPath(workingDirectory)` → path→tags 字典（异常时空字典）。
  6. 写临时文件 `Path.GetTempFileName()` →（VM.Format 选写入器；xlsx 的 sheetName 用 `"ExportFileList_SheetName".GetLocalizedResource()`）→ `File.Move(temp, picked.Path, overwrite: true)`；任何异常删除临时文件并弹 Failed 卡片，成功弹 Complete 卡片（行数、skipped）。

- [ ] **步骤 4：Toolbar 按钮**

在 `Toolbar.xaml` 的 `SqlQueryButton` 之后加 AppBarButton：`x:Load="{x:Bind ViewModel.InstanceViewModel.IsPageTypeNotHome, Mode=OneWay}"`、`Command="{x:Bind Commands.ExportFileList}"`、`IsEnabled="{x:Bind Commands.ExportFileList.IsExecutable, Mode=OneWay}"`、`Label/AutomationProperties.Name/ToolTipService.ToolTip` 绑定命令属性、`<FontIcon FontSize="16" Glyph="&#xE8E5;" />`。

- [ ] **步骤 5：构建 + 手动验证清单**

`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly` 预期 0 错误；然后运行全部导出测试预期全绿。手动验证：普通文件夹导出 CSV（Excel 打开中文不乱码）/xlsx（Excel 实际打开无修复提示）；列头筛选+搜索各一次；递归含 junction 的目录；回收站/压缩包内按钮禁用；搜索页选项锁定；取消保存选择器后无残留临时文件。

- [ ] **步骤 6：Commit**

```bash
git add src/Files.App/Actions/FileSystem/ExportFileListAction.cs src/Files.App/ViewModels/ShellViewModel.cs src/Files.App/UserControls/Toolbar.xaml src/Files.App/Data/Enums/FileOperationType.cs src/Files.App/Utils/StatusCenter/StatusCenterHelper.cs src/Files.App/Strings/en-US/Resources.resw src/Files.App/Strings/zh-Hans/Resources.resw
git commit -m "Add export file list action with toolbar entry"
```
