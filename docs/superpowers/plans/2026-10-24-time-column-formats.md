# 时间列格式自定义实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让修改、创建和删除时间列支持全局、按列、指定目录及可选递归的格式规则，并显示秒和毫秒。

**架构：** 保留原始 `DateTimeOffset` 和现有格式化器体系，新增无副作用的格式解析器，为每个 ShellViewModel 解析各列格式化器； ListedItem 只更新显示标签，不共享可变全局格式状态。

**技术栈：** .NET 10 自定义日期格式、WinUI 3、现有设置服务、MSTest。

**规格：** `docs/superpowers/specs/2026-10-24-files-internal-edition-design.md`

## 全局约束

- 依赖双版本计划的数据服务；Portable 的规则随 `Data` 迁移。
- 优先级固定为目录列 > 目录默认 > 全局列 > 全局默认；递归时最近目录优先。
- 首版列为 Modified、Created、Deleted；访问和 Git 日期不阻塞交付。
- 排序和筛选使用原始时间；显示毫秒不承诺数据源具有毫秒精度。
- 不重构无关布局和设置代码，文本使用现有本地化资源。

## 审查重点（Review Focus）

- 1601/9999 等无效时间：显示空白而非异常；任务 1 测试。
- 自定义格式非法或包含区域性文本：设置界面报错且不崩溃；任务 1/4 测试。
- 父子目录规则重叠：最近且最具体的规则获胜；任务 2 测试。
- 两个标签页使用不同目录规则：不得互相串格式；任务 3 测试。
- 改变格式后排序不得改变，已打开列表应立即刷新；任务 3 测试和冒烟。

## 文件结构

- `Data/Enums/DateTimeFormats.cs`：增加 Custom。
- `Data/Items/DateTimeColumnKind.cs`：三列稳定键。
- `Services/DateTimeFormatter/`：Custom formatter、规则模型、解析器和工厂扩展。
- `Services/Settings/GeneralSettingsService.cs` 及接口：全局列规则和目录规则持久化。
- `Data/Items/ListedItem.cs`、`ViewModels/ShellViewModel.cs`、`Views/Shells/BaseShellPage.cs`：显示刷新。
- `Views/Settings/GeneralPage.xaml`、`ViewModels/Settings/GeneralViewModel.cs`、`Dialogs/DateTimeFormatRuleDialog.xaml`：设置 UI。
- `tests/Files.App.UnitTests/`：格式化、解析和刷新测试。

---

### 任务 1：自定义格式化器

**文件：** 创建 `CustomDateTimeFormatter.cs`、`DateTimeFormatRule.cs`；修改工厂和 `DateTimeFormats`。

- [ ] 编写失败测试：`yyyy-MM-dd HH:mm:ss.fff` 输出毫秒；12/24 小时制；无效年份空白；非法格式由 `DateTimeFormatRule.Validate()` 返回错误而非抛出到列表。
- [ ] 定义 `DateTimeColumnKind { Modified, Created, Deleted }` 和不可变 `DateTimeFormatRule(DateTimeColumnKind? Column, string? DirectoryPath, bool IncludeSubdirectories, DateTimeFormats Format, string? CustomPattern)`。
- [ ] 将工厂扩展为 `GetDateTimeFormatter(DateTimeFormats format, string? customPattern = null)`；Custom 使用当前区域性和 `ToLocalTime()`，保留现有三种格式行为。
- [ ] 运行测试和 `Files.App` x64 Debug 构建，预期通过。
- [ ] 提交：`Add custom date time formatter`。

### 任务 2：规则解析与可移动路径

**文件：** 创建 `IDateTimeFormatResolver.cs`、`DateTimeFormatResolver.cs`、`PortableDirectoryKey.cs`；修改 GeneralSettings 接口/实现。

- [ ] 编写失败测试覆盖完整优先级、目录默认与目录列、仅当前目录、递归父子重叠、无规则回退、大小写和尾部分隔符规范化、卷不可用不改绑。
- [ ] 实现 `DateTimeFormatRule Resolve(string directoryPath, DateTimeColumnKind column)`；可移动卷使用卷标识+相对路径，普通目录使用规范化绝对路径。
- [ ] GeneralSettings 增加全局列规则和目录规则列表，使用现有 JSON 设置持久化；旧设置缺失时返回空列表，不改原 `DateTimeFormat` 默认值。
- [ ] 运行测试，特别验证“父目录日期、子目录时分秒”和“目录列覆盖目录默认”。
- [ ] 提交：`Resolve date formats by column and directory`。

### 任务 3：文件列表显示刷新

**文件：** 修改 `ListedItem.cs`、`RecycleBinItem` 所在文件、`ShellViewModel.cs`、`BaseShellPage.cs`。

- [ ] 编写失败测试：同一 ListedItem 原始时间不变而标签按 formatter 改变；Modified/Created/Deleted 可使用不同格式；两个 Shell 上下文互不串扰；排序键不变。
- [ ] 为 ListedItem 增加 `RefreshDateLabels(IDateTimeFormatter modified, IDateTimeFormatter created, IDateTimeFormatter accessed)`，RecycleBinItem 扩展 deleted；不得修改静态共享 formatter。
- [ ] ShellViewModel 按当前 WorkingDirectory 一次解析三列 formatter，在加载和 `UpdateDateDisplay()` 中应用；BaseShellPage 同时监听新规则变更，而非只监听旧 `DateTimeFormat`。
- [ ] 格式变化只刷新显示，不重新枚举或重排；自动列宽在格式变化后按现有入口重算。
- [ ] 运行测试并构建，手动验证打开两个不同规则目录的标签页并即时修改设置。
- [ ] 提交：`Apply directory date formats to file lists`。

### 任务 4：设置界面与验收

**文件：** 修改 GeneralPage/GeneralViewModel；创建 `DateTimeFormatRuleDialog.xaml(.cs)`；修改 Details 列头菜单和英文资源。

- [ ] 编写 ViewModel 失败测试：预设映射、格式预览、非法输入阻止保存、删除目录规则恢复下一优先级、IncludeSubdirectories 持久化。
- [ ] General 设置提供全局默认和三个全局列规则；目录规则对话框提供目录、列、格式、自定义文本、递归开关、预览和删除。
- [ ] 从 Details 时间列菜单进入“此目录的时间格式”，从全局设置管理全部目录规则；无硬编码用户文本。
- [ ] 运行全部测试、Msix/Portable x64 Debug 构建；验证秒/毫秒、非法格式、优先级、递归、即时刷新、列宽、移动盘符和 Portable 重启后规则保留。
- [ ] 提交：`Add date format settings interface`。
