# 导出文件列表设计

日期：2026-10-31
状态：已批准
关联规格：`docs/superpowers/specs/2026-10-24-files-internal-edition-design.md`

## 1. 意图与背景

用户经常需要把当前文件夹的内容变成一份结构化数据带走：提取路径和文件名、做统计汇总、制作第三方系统的目录导入源。目前只能截图或手工誊抄。

本功能在文件列表工具栏提供「导出文件列表」，弹出一个对话框选择范围、列和格式，产出 `.xlsx` 或 `.csv` 文件。下游用途多样，因此导出的数据必须是机器友好的真实值（完整路径、字节级大小、ISO 日期），而不是 UI 显示字符串。

与总设计「首版不做递归扫描磁盘的当前目录筛选」不冲突：那条约束针对筛选/SQL 查询功能本身；导出功能的递归枚举是本设计显式新增的独立能力，只在用户主动发起导出时执行一次性扫描。

## 2. 已确认的产品决定

以下决定来自与用户的对话，属于用户已定内容：

1. **入口**：当前窗格工具栏新增「导出文件列表」按钮，任何视图布局可用。不做右键菜单入口。
2. **筛选语义**：「仅导出当前筛选结果」勾选时导出**当前视图显示的内容**——即搜索框关键词、列头筛选（含正则）全部生效后的条目，顺序与视图一致。
3. **列策略**：导出对话框内以复选框列表勾选列，每次导出可不同。
4. **递归**：「包含所有子文件夹」复选框；递归时文件夹本身也占一行，用「是否文件夹」列区分。
5. **格式**：Excel (.xlsx) 与 CSV 单选。

## 3. 交互设计

### 3.1 入口与可执行条件

- 工具栏（`src/Files.App/UserControls/Toolbar.xaml`）新增 AppBarButton，绑定 `[GeneratedRichCommand]` 动作（模式参照 `InstallFontAction`）。
- 动作标签「导出文件列表/Export file list」，描述说明导出当前文件夹内容为 Excel 或 CSV。
- 可执行条件：当前页面为真实文件系统位置（含库、网络路径、OneDrive 本地路径）。回收站和压缩包内不可执行；MTP 设备、FTP 仅支持当前层导出（递归选项置灰）。无可执行时按钮禁用。
- **搜索结果页**：按钮可用，导出内容为当前显示的搜索结果。「仅导出当前筛选结果」锁定为勾选并禁用（整个视图本身就是查询结果，且内存中没有「全部项目」全集可取）；递归选项禁用。需要递归导出时应退出搜索后操作。

### 3.2 导出对话框

新建 `ExportFileListDialog`（模式参照 `DecompressArchiveDialog`：`Dialogs/` 下 XAML + `ViewModels/Dialogs/` 下 ViewModel + `IDialogService` 挂接）。对话框内容自上而下：

1. **范围**：
   - 「仅导出当前筛选结果」复选框。当前无任何活动筛选（`DirectoryFilter == Empty` 且无搜索关键词）时禁用并提示无可筛选。默认勾选状态：存在活动筛选时勾选，否则禁用。
   - 「包含所有子文件夹」复选框。位置不支持递归枚举时禁用（MTP、FTP、搜索结果页、库根目录）。默认不勾选。
   - 两项可同时勾选：递归枚举出的子文件夹条目也要经过同一筛选规范的求值。
2. **列**：复选框列表，至少勾选一列才能导出（确认按钮联动禁用）。默认勾选：名称、项目类型、大小(字节)、修改时间、完整路径。
3. **格式**：Excel (.xlsx) / CSV 单选，默认 CSV。

确认后走系统 FileSavePicker（需按现有模式用主窗口句柄 `InitializeWithWindow`，参照 `AdvancedViewModel.InitializeWithWindow`），默认文件名 `{当前文件夹名}_FileList_{yyyyMMdd}`，扩展名跟随所选格式。

### 3.3 执行与完成

- 枚举与写文件在后台线程执行，不阻塞 UI；不做实时进度与取消（v1 简化）。
- 输出先写同目录临时文件，成功后移动为目标文件；任何失败自动清理临时文件，不产生半成品。
- 完成时通过 StatusCenter 弹一张结果卡片（参照 `InstallFontAction` 的一次性卡片模式），显示导出行数与因无法访问被跳过的条目数。

## 4. 数据规范

### 4.1 列目录

| 列 | 数据来源 | 空值处理 |
| --- | --- | --- |
| 名称 | `ListedItem.Name` | 必有 |
| 扩展名 | `ListedItem.FileExtension` | 无扩展名留空 |
| 项目类型 | `ListedItem.ItemType` | 留空 |
| 大小(字节) | `ListedItem.FileSizeBytes` 原始值；文件行写真实值（含真实 0 字节），文件夹行留空（不得写 0） | 文件夹留空 |
| 大小(可读) | `ListedItem.FileSize` 显示串 | 留空 |
| 创建时间 | `ItemDateCreatedReal`（`DateTimeOffset`）格式化为 ISO；`DateTimeOffset.MinValue` 视为未知 | 未知留空 |
| 修改时间 | `ItemDateModifiedReal`（`DateTimeOffset`）格式化为 ISO；`DateTimeOffset.MinValue` 视为未知 | 未知留空 |
| 完整路径 | `ListedItem.ItemPath` | 必有 |
| 相对路径 | 相对被导出根目录计算，根内为 `.` 开头 | 必有 |
| 是否文件夹 | `PrimaryItemAttribute == Folder` | 必有 |
| 是否隐藏 | `ListedItem.IsHiddenItem` | 必有 |
| 文件标签 | 标签**显示名称**（`IFileTagsSettingsService.GetTagsByIds(FileTags)` 映射，非内部 ID），分号连接 | 无标签留空 |

日期输出本地时间的 `yyyy-MM-dd HH:mm:ss`（ISO 风格，可排序，与界面时间格式设置无关）。递归枚举出的条目沿用同一列目录；标签等需查询的属性在递归路径上尽力填充，取不到留空。

### 4.2 数据来源与顺序

- **勾选「当前筛选」**：直接取 `ShellViewModel.FilesAndFolders`（已是筛选+排序后的投影集合），顺序即视图顺序。
- **未勾选**：取当前目录全部项目（绕过活动筛选，从 ShellViewModel 的投影前集合或等价来源获得；不重新枚举磁盘）。顺序沿用当前排序设置。
- **递归**：在后台按当前排序设置逐层枚举子文件夹。每个子目录内按同一排序选项排序；行序为「当前层全部行，随后各子树按遍历序」，文件夹行出现在其内容行之前。
- **库根目录**：v1 不支持递归（库聚合多个物理成员文件夹，递归语义复杂且非核心场景）；需要递归时进入具体文件夹导出。库根当前层导出不受影响。
- **递归 + 筛选**：对每个枚举出的条目构造 `DirectoryItemFilterSnapshot`，用当前 `DirectoryFilterSpec`（搜索关键词已并入 `NameContains`）经 `DirectoryFilterEvaluator` 求值，不匹配的跳过；文件夹行是否保留按求值结果，其子树仍照常遍历（父被筛掉不等于跳过整个子树）。

### 4.3 CSV 格式

- UTF-8 带 BOM（保证 Excel 直接打开不乱码）。
- 逗号分隔，RFC 4180 引号规则（含逗号/引号/换行的字段加引号，引号翻倍），CRLF 行结束。

### 4.4 xlsx 格式

- 用 `System.IO.Compression` ZipArchive + 手写 OpenXML 生成，**不引入第三方库**。
- 单工作表「文件列表」；表头行加粗（静态 styles.xml）；不设显式列宽（打开后由 Excel 自适应/用户调整）。
- 行数据以流式写入 zip 条目，不把整份文档驻留内存；单元格用 inline string，数值列（大小字节、是否文件夹）写原生数值。
- 日期作为 ISO 文本写入，不做 Excel 日期序列转换。

## 5. 技术设计

### 5.1 新增文件

- `Actions/FileSystem/ExportFileListAction.cs`：命令入口，判定可执行性，打开对话框。
- `Dialogs/ExportFileListDialog.xaml(.cs)` + `ViewModels/Dialogs/ExportFileListDialogViewModel.cs`：选项 UI。
- `Utils/Storage/Exporting/FileListExportModel.cs`：导出请求（根目录、范围、列集、格式、排序、筛选规范）与结果（行数、跳过数）。
- `Utils/Storage/Exporting/FileListCsvWriter.cs`：纯函数 CSV 写入器（`Stream` 目标）。
- `Utils/Storage/Exporting/FileListXlsxWriter.cs`：纯函数 xlsx 写入器（`Stream` 目标）。
- `Utils/Storage/Exporting/RecursiveListEnumerator.cs`：递归枚举 + 快照构造 + 筛选求值，产出 `ListedItem` 或等价行模型。
- 本地化资源（`en-US` / `zh-CN` resw）与对应 `Strings` 生成项。

### 5.2 约束

- 无新 NuGet 依赖；无运行时反射、无动态代码生成，满足 AOT/裁剪规则。
- 复用 `DirectoryFilterSpec`/`DirectoryItemFilterSnapshot`/`DirectoryFilterEvaluator` 现有类型，不复制筛选逻辑。
- CSV/xlsx 写入器与相对路径计算为纯逻辑，进 `tests/Files.App.UnitTests` 做 MSTest 覆盖：转义、BOM、空值、日期格式、快照筛选一致性、xlsx zip 结构可被 `System.IO.Compression` 重新打开且 sheet XML 结构完整。
- UI 集成（工具栏按钮、对话框、进度横幅）按「构建成功 + 手动验证」标准，不写 UI 自动化测试。
- 遵循仓库规则：CsWin32 优先、CRLF、`.editorconfig`、不动生成文件。

## 6. 非目标

- 不保存导出预设、不记忆上次选项。
- 不做 xlsx 样式美化（冻结表头、筛选器、条件格式等）。
- 不做定时/自动导出、命令行导出。
- 不导出搜索框自身的递归搜索（当前产品搜索不递归；递归语义由本功能的选项承担）。
- 不支持 FTP/MTP/压缩包内的递归导出。

## 7. 留给建造者的决定

- 未勾选筛选时「全部项目」集合的具体取法（投影前集合或等价内部来源），以不重新枚举磁盘为准。
- 递归枚举复用哪个现有枚举助手（如 `FilesystemTasks`/`Win32Helper` 中现成目录枚举），以复用优先。
- 对话框视觉布局与控件细节，遵循现有对话框样式。
- 状态横幅的具体样式与文案组织。

## 8. 风险

- 递归枚举超大目录树耗时较长：后台执行不阻塞 UI，但 v1 无进度与取消，误发起的大导出只能等待其完成（接受的代价）。
- 网络路径递归可能很慢或部分失败：跳过不可访问项并计数，不中断整体导出。
- xlsx 手写 OpenXML 需保证结构合法：以测试重新解包验证；如实现中发现结构成本过高，降级方案为「Excel 可直接打开的带 BOM CSV」并回报用户重新确认。
