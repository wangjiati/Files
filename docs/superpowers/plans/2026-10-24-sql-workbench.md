# 只读 SQL 查询工作台实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [x]`）语法来跟踪进度。

**目标：** 对当前目录快照执行完整只读 SQLite SELECT，并自动提供文件列表或通用表格结果。

**架构：** 显式执行时将 ListedItem 标量复制到私有 `:memory:` 数据库；统一执行器通过 SQLite authorizer、prepare tail 单语句验证和进度中断保证安全。结果始终保留原始表格，含有效 `file_id` 时额外生成文件视图。

**技术栈：** .NET 10、Microsoft.Data.Sqlite.Core、SQLitePCLRaw.bundle_e_sqlite3/raw、WinUI 3、MSTest。

**规格：** `docs/superpowers/specs/2026-10-24-files-internal-edition-design.md`

## 全局约束

- 依赖高级筛选计划的目录快照和执行上下文；查询不递归磁盘。
- 完整只读 SELECT 包括子查询、CTE、聚合、窗口、连接和集合操作。
- 禁止写入、DDL、多语句、ATTACH/DETACH、扩展、危险 PRAGMA 和文件函数。
- 每次显式执行建立私有快照，不在 UI 线程执行，不在首版做增量镜像。
- 文件与表格双结果不能改成用户预先二选一；统计结果不强行映射文件。
- 不使用临时 P/Invoke；底层安全钩子使用现有 SQLitePCLRaw 能力。

## 审查重点（Review Focus）

- `SELECT ...; ATTACH ...` 或注释隐藏的第二语句：必须拒绝；任务 2 测试。
- 递归 CTE/超长运行查询：必须取消且 UI 可继续操作；任务 2 测试。
- 查询只返回统计列：显示表格，不能变成空文件列表；任务 3 测试。
- 多行含同一 `file_id`：文件列表去重但表格保留全部行；任务 3 测试。
- 查询期间源目录改变：结果标记快照且不得覆盖新目录；任务 4 测试。

## 文件结构

- `Utils/Storage/Querying/DirectoryQuerySnapshot.cs`：文件、标签标量记录。
- `DirectoryQuerySchema.cs`：稳定表/字段和建表。
- `SqliteDirectoryQueryService.cs`：快照、执行、取消和限制。
- `DirectoryQuerySecurity.cs`：authorizer、单语句和函数限制。
- `DirectoryQueryResult.cs`、`DirectoryQueryResultClassifier.cs`：原始表格与文件映射。
- `ViewModels/UserControls/SqlQueryWorkbenchViewModel.cs`：执行状态。
- `UserControls/SqlQueryWorkbench.xaml(.cs)`：编辑器、错误、耗时和结果视图。
- `Views/Shells/ModernShellPage.xaml(.cs)`、`ViewModels/ShellViewModel.cs`：当前目录和文件投影。
- `tests/Files.App.UnitTests/Querying/`：真实 SQLite 测试。

---

### 任务 1：快照 schema 和基本 SELECT

**文件：** 创建快照、schema、结果模型、服务和基础测试。

- [x] 编写失败测试：快照插入名称、扩展名、大小 null、UTC 时间和标签关系；基本 `SELECT * FROM files` 返回预期列和行。
- [x] 定义 `DirectoryQueryFileSnapshot`、`DirectoryQueryTagSnapshot`、`DirectoryQuerySnapshot`，字段与高级筛选共享稳定 ItemId/file_id 映射。
- [x] 固定首版 `files`、`tags`、`file_tags` 表；大小/时间使用 INTEGER，未知为 NULL，时间明确 UTC ticks 或 Unix 秒并在字段说明中一致使用。
- [x] 实现 `Task<DirectoryQueryResult> ExecuteAsync(DirectoryQuerySnapshot snapshot, string sql, DirectoryQueryExecutionOptions options, CancellationToken ct)`；显式初始化 SQLitePCL Batteries，连接归单次执行所有。
- [x] 使用事务批量插入和参数化生成值，后台建表/查询；运行测试确认 SELECT 和 null 语义。
- [x] 提交：`Add directory SQL snapshot executor`。

### 任务 2：完整只读 SELECT 与安全执行

**文件：** 创建 `DirectoryQuerySecurity.cs`；修改服务和 options。

- [x] 先编写攻击与能力测试：子查询、普通/递归 CTE、聚合、窗口、JOIN、UNION 成功；INSERT/UPDATE/DDL、第二语句、ATTACH、PRAGMA、load_extension 和文件函数失败。
- [x] 使用 SQLite prepare tail 确认只有一条语句；使用 `sqlite3_set_authorizer` 白名单只允许读取私有快照所需动作，禁止写、附加、危险 PRAGMA 和扩展。
- [x] 使用 SQLitePCLRaw progress handler/interrupt 联动取消；options 固定包含 Timeout、MaxRows、ProgressInterval，达到 MaxRows 设置 `IsTruncated=true`，不能静默截断。
- [x] 捕获并映射 SQLite 错误、超时和取消；所有连接、statement 和底层句柄在finally/Dispose释放。
- [x] 运行安全测试，确认递归 CTE 可中断、恶意语句无文件写入、UI 线程不参与执行。
- [x] 提交：`Secure read-only directory SQL execution`。

### 任务 3：文件/表格结果分类

**文件：** 创建结果分类器和测试。

- [x] 编写失败测试：有效 file_id 生成文件结果；统计/计算列无 ID 只生成表格；重复 ID 文件去重表格不去重；失效 ID、源版本过期和无 ORDER BY 行为。
- [x] `DirectoryQueryResult` 固定包含列、原始行、IsTruncated、Elapsed、SourceRevision 和 `FileQueryResult?`。
- [x] `DirectoryQueryResultClassifier.Classify(result, snapshot)` 仅当每行有效 file_id 可映射时生成文件结果；文件顺序遵循 SQL 行顺序，文件 ID 去重，原始行完整保留。
- [x] 文件结果只保存 ID 和快照引用，不复制 WinUI 对象；源版本过期时禁止作为当前目录新筛选提交。
- [x] 运行全部查询和分类测试。
- [x] 提交：`Classify SQL file and tabular results`。

### 任务 4：实用 SQL 工作台和文件视图

**文件：** 创建 Workbench ViewModel/XAML；修改 ModernShellPage、ShellViewModel、资源。

- [ ] 编写 ViewModel 失败测试：执行/取消、忙碌状态、错误、耗时、截断提示、文件/表格切换、目录变化后的快照状态。
- [x] 在 ModernShellPage 增加可关闭 SQL 工作台，提供编辑器、字段说明、少量示例、执行/取消、错误和耗时；不加入历史、保存查询或独立 schema 浏览器。
- [x] 文件结果默认投影到现有文件列表并支持选择、打开、复制；结果表格保留计算列。用户切换查看不清空原始结果，手动排序不回写 SQL。
- [x] 执行时捕获当前高级筛选执行上下文；旧查询不得覆盖新目录，源变化后标记“快照结果”，仅显式重新执行刷新。
- [x] 运行测试和 Msix/Portable x64 Debug 构建；验证复杂 SELECT、双视图、复制/打开、取消和大目录。
- [x] 提交：`Add SQL query workbench`。

### 任务 5：端到端验收与内部发布候选

**文件：** 只修改验收发现的问题和内部发布说明，不扩展功能。

- [x] 运行 `dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -c Release -p:Platform=x64`，预期全部通过。
- [x] 顺序运行 Msix/Portable Release 构建和 Publish，预期成功；Portable 在无数据库安装的环境直接运行 SQL。
- [x] 验证至少 10 万条合成快照的建表与查询不阻塞 UI，记录实测时间和限制；无证据时不增加缓存或增量镜像。
- [ ] 对普通目录、搜索结果、回收站和一种非本地存储做冒烟；验证 ATTACH、写入、多语句、递归取消、统计表格、重复文件 ID 和目录切换。
- [ ] 提交：`Validate internal SQL workbench release`。
