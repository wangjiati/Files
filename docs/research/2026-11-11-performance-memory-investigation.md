# Files 卡顿与内存占用深度调查报告

- 日期：2026-11-11
- 适用项目：Files（WinUI 3 / .NET / Native AOT；上游 v4.3.3 fork，当前 HEAD `a0cb3d340`）
- 调查动机：用户反馈"使用起来感受特别卡顿，看起来特别占用内存"
- 调查方式：6 个并行静态审计（UI 线程阻塞 / 事件订阅泄漏 / 无界缓存 / 列表虚拟化与滚动 / 预览窗格近期改动 / 服务器化与启动路径），全部高严重度结论经人工读码复核；上游问题库对照（files-community/Files#4180）
- 局限：未做运行时测量（调查时无运行实例与可用产物）；内存数值为基于代码的量级估算，非实测

## 0. 结论速览

1. **卡顿不是单一根因**，而是四类叠加：大列表选择路径 O(N²)、目录变更批次全量 Reset 重建、预览窗格"切换即全量重载"（本次 fork 自定义改动引入的回归）、导航/设置路径的 UI 线程同步 I/O。
2. **内存"越用越高"最强的解释**：预览窗格图片全尺寸解码且切换即重载（无 DecodePixelWidth、无缓存）；无界缓存三件套（目录大小缓存 / 撤销栈 / 图标缓存）。（原列首的"SpeedGraph 事件泄漏"经 §8 三方交叉验证**证伪**，见 §8 与 §2 M1 后记。）
3. **内存"看起来高"的基线构成**：MSIX 双进程 + 每进程 12 条常驻 STA 线程 + 右键菜单后第三方 shell 扩展 DLL 常驻 + 按需的 WebView2/prevhost 伴生进程。这一层是架构性的，不随使用增长。
4. **本次 fork 的预览窗格布局改动确实引入可感知回归**：标签/布局切换从"可见性切换"变为"全量预览重载"，窗口 resize 每 tick 全量重排且在特定状态下每 tick 同步写设置。
5. 上游自身已知性能问题（#4180：RAM、缩略图、滚动卡顿、搜索 CPU）与本报告发现吻合——相当部分卡顿是上游 v4.3.3 固有的。

## 1. 卡顿根因（按用户感知影响排序）

### C1.【回归-自定义】预览窗格：切换即全量重载 + 底层加载不可取消

- 位置：`src/Files.App/ViewModels/UserControls/InfoPaneViewModel.cs:516-547`（切换触发 `UpdateSelectedItemPreviewAsync()`）、`:263`（`GetBuiltInPreviewControlAsync` 不接收 CancellationToken）
- 机制：合并 `334acba5e..a0cb3d340` 后，进入/离开 RightTabs 布局或 Details↔Preview 切换都会完整重跑预览管线：磁盘流打开 + Shell COM 激活 / 位图解码。被取代的请求因底层不接受 token 仍会跑完。旧版切换仅是可见性（VSM）切换，近零成本。
- 触发：预览窗格开启时每次切换标签页/布局模式/Details↔Preview。
- 佐证：选中项防抖仅 100ms（`InfoPaneViewModel.cs:196-210`），按住上下键会以 100ms 间隔持续产生完整预览加载。
- 备注：`previewRequestVersion` + `CancelPreviewLoad` 在 UI 分发边界可靠，问题在底层加载无 token。

### C2.【回归-自定义】窗口 resize 每 tick 全量 ApplyLayout + 特定状态每 tick 同步写设置

- 位置：`src/Files.App/Views/MainPage.xaml.cs:445`（`RootGrid_SizeChanged => LoadPaneChanged()`）、`:447-476`（`UpdatePositioning` → `InfoPane.ApplyLayout` + `GoToState(…, true)` + `InputSystemCursor.Create`）、`:410-422`（内容列钉在 MinWidth 时每个 tick `VerticalSizePx +=` → 设置写入）
- 机制：交互式拖动窗口大小时每秒触发几十次：VSM 过渡动画重启、最多 6 个 GridLength 赋值、COM cursor 对象新建、设置读取；叠加 `VerticalSizePx +=` 的每次设置写入——MSIX 下每次 `SetValue` 是同步跨进程 RPC（`ServerSettingsDatabase.cs:48`），便携版是 UI 线程同步 JSON 全文件落盘。本次改动把 InfoPane MinWidth 从 150 提到 300，使"内容列被钉死"场景更常见，触发面扩大。
- 正向确认：分隔条拖拽本身干净（`ManipulationCompleted` 才写一次，`InfoPane.xaml.cs:96-116`）。

### C3.【上游】大列表选择路径 O(N²)

- 框选：`src/Files.App/UserControls/Selection/RectangleSelection_ListViewBase.cs:66-108`——每次 PointerMoved 对 `SelectedItems` 做 `ToArray`/`Cast.ToList`/`SequenceEqual`/`Except` 多趟 O(S) 拷贝比较；策略层 `ExtendPreviousItemSelectionStrategy.cs:22-25` 每项 `Contains`+`Add` 且逐项触发 SelectionChanged；每次 SelectionChanged 又被 `BaseGroupableLayoutPage.cs:253-258` 以 O(S) 物化比较。
- Ctrl+A：`src/Files.App/Views/Layouts/DetailsLayoutPage.xaml.cs:468-472,1361`——对每个 AddedItem 做 `ContainerFromItem`+`FindDescendant`+`SelectedItems.Contains`，整体 O(N²)。
- 选择恢复：`ItemManipulationModel.cs:61-92` 逐项 Add，每次触发一次 SelectionChanged；导航/切视图恢复 N 项选择 O(N²)。
- 触发：几百上千项的目录里框选、全选、跨视图恢复选择。上游 #4180 中"middle-click scroll 卡顿数秒"同类。

### C4.【上游】目录变更批次 = 全量 Reset 重建

- 位置：`src/Files.App/ViewModels/ShellViewModel.cs:1459-1473`（枚举中间批次）、`:2862-2868`、`:3440-3441`（文件监视器批次 200ms 节流）——均 `BeginBulkOperation`+`Clear`+`AddRange`+`EndBulkOperation`（Reset）。
- 机制：目录内有拷贝/下载进行时，列表每秒全量重建约 5 次，虚拟化容器全部重实现。
- 放大器：`ShellViewModel.cs:2110-2114`——滚动经过本地化名称文件夹（文档/下载等）时其 DisplayName 解析后触发 Order+Apply 全量重排 Reset。

### C5.【上游】导航热路径 UI 线程同步磁盘 I/O

- 位置：`src/Files.App/Helpers/Layout/AdaptiveLayoutHelpers.cs:27-46`（`TryPredictLayout`：同步读 desktop.ini + 同步枚举最多 200 个目录项）；调用链 `LayoutPreferencesManager.cs:262-277` ← `ModernShellPage.xaml.cs:106,133-134`、`BaseShellPage.cs:586/602/693/711/926`（导航事件，UI 线程）。
- 机制：自适应布局开启（默认）且文件夹无已存布局偏好时，每次首次导航都在 UI 线程做磁盘 I/O。排除列表只含网络盘/无根盘（`AdaptiveLayoutHelpers.cs:26-27`）——光驱、可移动盘、慢速 HDD 不排除，单次可达数百 ms 至秒级。
- 归属：上游 PR #18888（讽刺地名为"Performance and optimizations"）。代码库自己的惯例见 `ShellViewModel.cs:2970`（同一 ini 服务用了 `Task.Run`）。

### C6.【上游】设置写入反馈回路（规律性卡顿）

- 位置：`src/Files.App/Utils/Serialization/Implementation/ServerSettingsDatabase.cs:48`（每次 `SetValue` 同步跨进程 RPC）、`:112-140`（`OnServerChanged` 回读全文件 RPC + `JsonElement.DeepEquals` 全量 diff）；服务端 `ServerEvents.cs:86` 150ms debounce。
- 机制：连续写设置（拖动设置页滑块、C2 的每 tick 写入）时每次变更都整文件跨进程读+全量反序列化 diff；便携版退化为 UI 线程同步文件写。
- 佐证：启动时 `UserSettingsService.cs:104`、`FileTagsSettingsService.cs:39` 构造 `ServerSettingsDatabase`，ctor 同步 `ReadValues()` RPC（`ServerSettingsDatabase.cs:26`），服务器冷启动忙于预热时这两个 RPC 排队，拖慢首帧。

### C7.【上游】其他确认的卡顿贡献者

- 详细视图行模板约 60+ 元素（16 列单元格 + 每行重命名 TextBox + CheckBox + 4 Image + 嵌套 tags ItemsRepeater，`DetailsLayoutPage.xaml:1026,1059,1153-1160`），且约 30 个 ElementName 反射 Binding（`:960,968,975,1041…`）——快速滚动每容器实例化成本高。
- 视图切换 = Frame 整页导航（`BaseShellPage.cs:601-608`），首次切换整页+ItemsControl 全重建。
- 文件占用失败路径：同步 Restart Manager RPC + 递归全目录枚举（`Win32Helper.Process.cs:78-133`、`ShellFilesystemOperations.cs:436-452`），大目录可阻塞数秒（线程上下文疑似 UI 延续）。
- `NavigationToolbarViewModel.cs:317-324`：每次选择变化切换 `IsDynamicOverflowEnabled` → CommandBar 溢出重排；`UpdateSelectionSize` 每次选择变化对全部选中项 Any+Sum（`BaseLayoutPage.cs:1058-1070`）。

## 2. 内存根因（按增长性排序）

### M1.【上游】【已证伪 2026-11-11】SpeedGraph 事件泄漏——原判断"随文件操作数无界增长"不成立

> **交叉验证后记（§8）**：三路独立验证（盲检/对抗证伪/可达性）中，可达性验证 + 微软官方文档 + 第一性原理证明双重订阅在本托管形态下不可达：WinUI 中 Loaded 先于首次 SizeChanged 触发（首次 Loaded 时 OnLoaded 处理器尚未挂上，无害空过）；chevron 折叠/展开仅切换 Visibility 不触发 Loaded/Unloaded；Flyout 开关与虚拟化回收的 Unloaded(-1)/Loaded(+1) 成对平衡，横幅移除时四组订阅全部清零。盲检与对抗验证者的"成立"裁决均建立在"SizeChanged 先于 Loaded"这一错误排序上。代码模式确属脆弱（防御性 `-=`/`+=` 幂等化仍是合理的维护性改进），但**不构成泄漏**，不作为修复依据。以下原文保留作记录：

- 位置：`src/Files.App/UserControls/StatusCenter/SpeedGraph.cs:73-77`（首次 SizeChanged 订阅一组事件）、`:88-91`（每次 Loaded 对同一组事件再叠加订阅）、`:94-100`（Unloaded 每组只退订一次）、`:73`（`Loaded +=` 永不移除）。
- 泄漏链：`IAppThemeModeService`（IoC 单例，永生）→ SpeedGraph → Points 速度采样 ObservableCollection + 组合视觉对象树。
- 触发：每个复制/移动/删除/解压操作的状态中心横幅实例化一个 SpeedGraph（`StatusCenter.xaml:238`）；横幅关闭后该实例无法 GC，主题切换还会调用全部残留处理器。

### M2.【上游实现 × 自定义放大】预览图片全尺寸解码、无缓存、切换即重载

- 位置：`src/Files.App/ViewModels/UserControls/Previews/ImagePreviewViewModel.cs:26-38`——`new BitmapImage()` 未设 `DecodePixelWidth/DecodePixelType`，`SetSourceAsync` 按原图分辨率解码（4000×3000 ≈ 48 MB/张），旧 BitmapImage 无显式释放。
- 放大器：C1 的"切换即重载"使大图被反复解码；无预览缓存（同一文件反复选中重复解析）。
- 对照：`BitmapHelper.cs:26-33` 已有 decodeSize 用法可循。

### M3.【上游】无界缓存三件套

- 目录大小缓存：`src/Files.App/Services/SizeProvider/CachedSizeProvider.cs:13,81-85`——`sizes` 字典无上限、`CleanAsync()` 是空操作；每次计算把深度≤3 的每个子目录都写入（node_modules 类目录单次写入 10³-10⁴ 条）。触发条件：开启"计算文件夹大小"（默认关）。
- 撤销栈：`src/Files.App/Utils/Storage/History/StorageHistoryWrapper.cs:17-25`——无条数/字节上限；批量操作一条可达 MB 级（万文件复制 ≈10-30 MB 路径列表），undo 过的旧条目仍驻留。
- 图标缓存：`src/Files.App/Services/Storage/IconCacheService.cs:16-17`——键空间 = 扩展名 × 8 档尺寸 × DPI，`Clear`（`:65`）无任何调用方；256px 位图每张 ≈256 KB。

### M4.【上游-设计性】每标签页整目录 ListedItem 全量驻留

- 位置：`src/Files.App/ViewModels/ShellViewModel.cs:781-782`；搜索结果全量载入 `:3835-3837`。
- 机制：ListedItem ≈0.5-1.5 KB/项；10 万项目录单标签 ≈100-150 MB 项数据，多标签同开叠加；全盘搜索结果全量入内存。
- 缓解已存在：缩略图位图在容器回收时释放（`BaseLayoutPage.cs:1447` → `ShellViewModel.cs:1035-1041`），位图上限 256px。

### M5.【上游】窗口/页面树滞留（条件性）

- `MainPage.xaml.cs:64-71,137,353`：构造函数批量订阅 5 个单例事件 + `AppWindow.Changed` lambda（无法退订），全文件无对应退订——根 Frame 再次 Navigate(MainPage) 时每重建一次泄漏一整棵旧页面树（所有标签关闭后进程存活期间的再激活路径）。
- `MainWindow.xaml.cs:23-24`：`static MainWindow _Instance` 关闭后从不置 null → 已关闭窗口整树被静态根钉住；`WindowContext.cs:34-36` 单例订阅 `AppWindow.Changed` 永不退订，叠加放大。
- 正常单窗口生命周期只创建一次，故为条件性（进程驻留/窗口重建场景）。

### M6.【上游-伴生进程】右键菜单与 Shell 扩展

- `ContextMenuWorkerPool.cs:18-19`：最多 3 个 idle STA worker，worker 内第三方 shell 扩展 DLL 常驻（便携模式在主进程内）；MSIX 下服务器启动即预热（`StartupServices.cs:8,14`）。右键几次不同上下文后内存上涨不回落。
- `ExplorerCommandActivator.cs:15,69`：`LockServer(true)` 的 COM 类对象 pin 住扩展的 local-server 进程直至会话结束——用户感知为"Files 占内存"，实际含伴生进程。

## 3. 进程内存基线构成（静态推断，量级）

| 进程 | 构成 | 量级 |
| --- | --- | --- |
| Files.App.exe（MSIX/便携） | AOT WinUI UI 树 + 12 条常驻 STA 池（`STATask.cs:17`，首次使用即全部启动，每条 OleInitialize）+ Shell image list + 缩略图缓存 + M4 标签驻留 | ≈150-300 MB |
| Files.App.Server.exe（仅 MSIX） | 专用 dispatcher 线程 + 30s grace timer + 12 条 STA 池 + ≤3 菜单 worker + shell 扩展 DLL + 5+2N 个 FSW/盘 + 设置存储 + 更新轮询（`UpdateCatalog.cs:20,40-51`，每 2h/5h PeriodicTimer + 常驻 HttpClient）+ KeepRunning/托盘开启时关窗仍驻留（`ServerHost.cs:144-152`） | ≈30-100 MB |
| WebView2 进程组（按需） | HTML 预览/发行说明（`HtmlPreview.xaml.cs:21`） | 各 ≈50-200 MB |
| prevhost.exe（按需） | 信息窗格 Shell preview handler（`ShellPreviewViewModel.cs:118`）；C1 使其创建/销毁更频繁 | ≈10-30 MB/实例 |

- 便携版：单进程合体（UI + 两套目录系统 + dispatcher + 12 STA 池 + 菜单 worker + 全部 FSW），单进程数值更高但总量通常低于双进程之和；无更新轮询。
- 线程/watcher/进程计数为代码实证；内存数值为量级推断（未实测）。
- 启动期：便携版存在三重目录系统预热（`InProcessAppServerService.cs:21-25` 的 LocalSystemDataProvider + `InProcessServer.cs:43-61` 的 InitializationCache/Recent×2/Network/逐盘 WarmDrives，其中 pinned/libraries 枚举两遍；`AppLifecycleHelper.cs:140-152` 第三轮 QuickAccess/库/云盘/WSL/标签），叠加 `App.xaml.cs:160` 对服务器初始化 fire-and-forget、`ServerHost.cs:218-230` 在 dispatcher 就绪前内联同步执行 Shell 调用——启动阶段并发大量 Shell COM，对应"启动卡 + 内存峰值"。MSIX 双进程合计最多 24 条常驻 STA 线程。

## 4. 已排查并排除的嫌疑（避免重复调查）

- 列表虚拟化断裂：不存在（全部 ItemsStackPanel/ItemsWrapGrid）；x:Bind/x:Phase/x:Load 齐备；缩略图异步加载 + 滚动挂起 + 回收取消已有；`ContainerContentChanging` 内无内联 COM（`EnqueueItemLoad` 延迟边界完好）。
- 标签页销毁链路完整：`BaseTabBar.CloseTab` → `TabBarItem.Unload/Dispose` → `ShellPanesPage.Dispose` → `BaseShellPage.Dispose` → `ShellViewModel.Dispose` 全部配对退订。FileSystemWatcher 释放正确。
- 服务端客户端生命周期：`ServerHost.clients`/`ServerEvents`/`RecycleBinCatalog.changes`（512 上限）/`CatalogWatchers` 均有清理。
- 流/句柄：PDF 预览流 finally Dispose 且页数有上限；各 FileStream 均 using。
- GC/AOT 配置无异常（默认 workstation+并发 GC；主应用与服务器均 PublishAot）；字体/图标无自定义 ttf 全量加载。
- 同步等待原语面窄：`.Wait()` 仅 `Program.cs:358` 一处；`GetAwaiter().GetResult()` 全库 2 处；Views 层 33+ 处 `.Result` 均为 `FilesystemResult<T>.Result` 属性非 Task.Result。
- 设置页 VM、布局页事件、属性窗口订阅基本配对；Toolbar `OnViewModelChanged` 属坏味道但不钉标签。

## 5. 修复优先级建议（未实施，按收益/风险排序）

> **执行状态更新（2026-11-11，v2）**：已确认按四批推进。批次 1（M2 图片解码上限 + C5 布局预测缓存/预取 + 同配置基线测量）详细计划见 `docs/superpowers/plans/2026-11-11-performance-fixes-batch1.md`；本节第 1 项（SpeedGraph）经 §8 交叉验证证伪撤销，C5 经专家评估以缓存设计提前至批次 1，原批次 3 剩 C6+M3。上游策略 = 先本地后观察。

1. **SpeedGraph 双重订阅**（M1）：一行级改动——`OnLoaded` 去掉重复订阅组，`OnUnloaded` 补 `Loaded -= OnLoaded`。收益：止住随操作数无界增长；风险：极低。
2. **ImagePreview 设 DecodePixelWidth**（M2）：按预览窗格实际尺寸解码，参照 `BitmapHelper` 现有用法。收益：单张大图从 ≈48 MB 降到 MB 级；风险：低。
3. **预览窗格切换重载收敛**（C1）：`GetBuiltInPreviewControlAsync` 传递 CancellationToken + 缓存上一预览（同文件+基本预览状态未变时复用）；防抖 100ms → 150-250ms。收益：消除切换卡顿主力；风险：中（触及预览管线，需回归）。
4. **ApplyLayout 幂等化**（C2）：值未变不重排、cursor 缓存为静态、resize 路径节流；`VerticalSizePx +=` 改为拖拽结束时写。收益：拖窗口不再掉帧；风险：低。
5. **选择路径增量化**（C3）：框选改差集批量应用、`SetCheckboxSelectionState` 批量化、选择恢复用 `SelectedItems` 批量赋值。收益：大目录框选/全选从秒级回到帧级；风险：中（选择语义需测试）。
6. **目录变更批次增量化**（C4）：监视器批次对已排序列表做增量 Add/Remove 而非 Reset；本地化名称重排只在名称真变时触发。收益：拷贝进行中列表不闪烁不卡；风险：中高（分组/排序不变量多）。
7. **TryPredictLayout 移出 UI 线程**（C5）：`Task.Run` 包裹（代码库已有惯例）或预取。风险：低。
8. **缓存上限**（M3）：sizes 加容量上限/LRU；撤销栈按条数或字节预算截断；IconCacheService 在 DPI/布局变化时调用已有 Clear 或加 LRU。
9. 设置写入去抖（C6）：连续 SetValue 在客户端聚合后单次提交（收益：设置页滑块、C2 残余路径）。

## 6. 运行时验证方案（后续步骤建议）

1. 构建便携版（`.github/scripts/Build-Internal.ps1 -PortableOnly`），记录：启动后 10s/60s 空闲 RSS、每进程 RSS、线程数、句柄数。
2. 场景 A（SpeedGraph）：执行 20 次小文件复制→关闭横幅→强制 GC（或观察 RSS）→ 对比修复前后斜率。
3. 场景 B（预览窗格）：选中 4000×3000 图片→切换 Details↔Preview 10 次→记录 RSS 增量（预期当前 ≈数百 MB 波动，修复后 ≈数 MB）。
4. 场景 C（框选）：生成 5000 项测试目录→框选全选→记录 UI 线程阻塞时长（ETW/PerfView 或简单秒表）。
5. 场景 D（resize）：预览窗格 Bottom 布局→连续拖窗口大小 10s→记录掉帧与设置文件写入次数（文件 mtime 变化计数）。

## 7. 上游问题库对照（2026-11-11 爬取）

方法：GitHub Search API 按 memory/lag/stutter/freeze/scroll/thumbnail/preview pane/cpu/slow 检索 issue 与 2026-06 以来合并的性能 PR；对本仓库关键文件逐行核对。

### 7.1 交叉验证：我们的发现 ↔ 上游证据

| 本报告发现 | 上游对应 | 状态 |
| --- | --- | --- |
| C2 resize 卡顿 + M5 的 `AppWindow.Changed` lambda | [#19028](https://github.com/files-community/Files/issues/19028)（开放）：ETW 实测每次窗口移动 UI 线程忙 15-40ms（Explorer ≈3ms），根因正是 MainPage 的 `AppWindow.Changed` 处理器每次移动重算标题栏拖拽区域（每次移动约 185 个窗口管理器调用）；resize 中位 50ms、p95 96ms、**max 688ms**；相关开放 PR #19004 | **独立量化证据交叉验证**；上游未修，与我们的修复建议一致（仅尺寸/可见性变化时才重算） |
| C7 详细视图 ElementName Binding | #19028 同一分析：resize 时 `DetailsLayoutPage` 重命名 TextBox 的每行 `{Binding ElementName=PageRoot}` 80 次 resize 花费 1.8s | 交叉验证 ✅ |
| C3 框选/选择 O(N²) | #18637（PR，2026-06 已合并"Optimized selection rectangle performance"）；#4971（框选挂起）；#4180 追踪项 | 我们的发现是上游优化后的**残余问题** |
| C4 全量 Reset 重建 | #18888（PR，2026-08）已做"sorted streaming enumeration / 保留旧列表防闪烁" | 部分修复，拷贝期间重建仍在（我们的发现） |
| C5 TryPredictLayout UI 线程同步 I/O | **由 #18888（"Performance and optimizations"）引入**，上游无对应 issue | 我们独有发现，可反哺上游 |
| C6 设置写入回路 | #19002（PR，2026-10）部分涉及（缺失设置不再立即写盘） | 部分修复 |
| M1 SpeedGraph 事件泄漏 | 无对应 issue | **我们独有发现，可反哺上游** |
| M2 图片全尺寸解码 | #7485（取更小缩略图提速，方向相关） | 相关 |
| M3 无界缓存三件套 | #4180 列出 "Improve ram usage"；#18888 已含 "Trim memory after leaving large folders, closing tabs, or going idle" | 部分修复 |
| M4 每标签全量驻留 | #14243（12 标签 1.3GB）、[#19062](https://github.com/files-community/Files/issues/19062)（3 标签 1.4GB + 标签切换 800ms + 导航 2100ms，4.2.9，2026-10 关为重复） | 用户实证 ✅ |
| M5 窗口/页面树滞留 | #14337（后台驻留时反复开关内存累积）、#18226、#11007（WinUI 3 Frame 导航泄漏的平台层问题） | 用户实证 ✅ |
| "越用越卡+内存极高"总体 | [#18459](https://github.com/files-community/Files/issues/18459)（原话吻合，关为重复 #4180）、[#18982](https://github.com/files-community/Files/issues/18982)（64% CPU + >1GB，4.2.9）、#16043（0.1MB/s 无界增长） | 用户实证 ✅ |

### 7.2 上游已修复且已包含在本 fork 基线（v4.3.3）中的

- **#19026 隐藏加载圆环空转合成器**（空闲 2.5-12% 单核，长会话至 94%）——本地已验证修复形态：`TabBarStyles.xaml:171` 与 `InfoPane.xaml:280` `IsActive="False"` 默认 + Loading 态才设 True。该高 CPU 嫌疑**排除**。
- #18888 / #18887（滚动/缩略图并行化/STA 池/滚出释放/离开大目录 Trim 内存）、#18849 + #19002（启动）、#18855（STA 亲和性/GDI/COM 释放等审计修复）、#18992（搜索，针对 #18982）、#19009（驱动器发现）、#18803（右键菜单）、#18729（文件夹加载，50k 文件测试）、#18837 + #18939（导航）。
- 结论：**本报告的静态发现是上游 2026-06~10 性能推进之后仍然存在的问题**，不是旧版本残余。

### 7.3 上游仍开放、与用户症状直接相关的

- #4180（性能追踪总项，78 👍）：RAM、缩略图、滚动卡顿、搜索 CPU 均未完成
- #19028（窗口拖动/resize 卡顿）——与本报告 C2/C7/M5 同源，修复建议一致，**值得跟踪其 PR #19004 并回流**
- #12704（SMB 慢）、#14964/#17794（滚动冻结）、#17776（打开标签文件夹卡顿/假死）、#14313（框选时滚轮）

### 7.4 可反哺上游的本报告独有发现

1. M1 SpeedGraph 双重订阅（随文件操作数无界泄漏，一行级修复）
2. C5 TryPredictLayout 在 UI 线程同步读 desktop.ini + 枚举 200 项（#18888 引入的回归）
3. M3 无界缓存细节（CachedSizeProvider.sizes / 撤销栈 / IconCacheService.Clear 无调用方）

### 7.5 检索方法与局限

- 共 9 次 API 检索（issue：memory/lag/stutter/freeze、scroll、thumbnail、preview pane、cpu、slow；PR：2026-06-01 以来合并的 performance），按 reactions 排序取前 12-20 条，共覆盖约 150 条结果中的高信号子集；未逐条全文阅读所有评论。
- 状态以 2026-11-11 抓取为准；上游 issue 关闭状态（completed/duplicate/not_planned）已按 API 字段区分。

## 8. 三方交叉验证（2026-11-11，验证后定稿）

用户要求对全部结论做独立交叉验证后再进入修复。三路方法：**盲检**（只给症状、不给位置，独立定位）、**对抗证伪**（10 项主张+行号，任务是推翻）、**可达性验证**（运行时触发路径逐条追查）。

| 主张 | 盲检 | 对抗证伪 | 可达性 | 最终裁决 |
| --- | --- | --- | --- | --- |
| M1 SpeedGraph 泄漏 | 成立* | 成立* | **不可达** | **证伪**（*两者均基于"SizeChanged 先于 Loaded"的错误排序；微软文档 + 语义分析：Loaded 先触发，Flyout 循环成对平衡，移除时订阅清零） |
| M2 图片全尺寸解码 | 成立（独立定位） | 成立（排除解码约束路径） | — | **证实**（50MP ≈ 200MB 峰值） |
| C1 预览切换全量重载 | — | 成立（git show f1932c602 证实旧行为仅属性通知） | 条件可达 | **证实（收窄）**：仅 RightTabs 布局中 Details↔Preview 跨越才重载；无预览缓存；防抖 100ms + previewRequestVersion 合并快速切换 |
| C2 resize 重排+写设置 | 成立 | 成立 | 条件可达 | **证实（收窄）**：每 tick 全量 ApplyLayout 无条件；设置写需窗口窄到内容列钉死 MinWidth=208（拖 10s ≈ 10²-10³ 次；MSIX=UI 线程阻塞跨进程 RPC，便携=整份 JSON 同步重写）；BaseJsonSettings 值比较与 Clamp 边界是部分缓解 |
| C5 导航 UI 线程同步 I/O | 成立 | 成立 | 可达 | **证实（放大）**：每次导航全链 = FRN+布局数据库+NTFS ADS+desktop.ini+≤200 项枚举；搜索提交/前进后退命中；布局切换 O(导航栈深) 轮；光驱/可移动盘不排除 |
| M3a 目录大小缓存 | — | 成立（盘移除驱逐与设置切换清空不构成周期性上限） | — | 证实 |
| M3b 撤销栈 | — | 成立（无修剪路径） | — | 证实 |
| C3 框选/全选 O(N²) | — | 成立（核实 PR#18637 commit 0dfde68e9 未触碰四条路径） | — | 证实 |
| C4 集合 Reset 重建 | — | 成立（IntervalSampler 200ms 证实 ≈5 次/秒） | — | 证实 |
| 常驻 STA 线程 | — | 成立（Background 线程随进程退出，属缓和事实） | — | 证实 |

**验证方法论教训**：静态读码能确认"代码模式存在"（M1 模式确实在文本上成立），但运行时语义（事件触发顺序、托管宿主行为）可能使结论完全反转。M1 的教训已反哺到修复流程：批次 1 计划在专家评估前先经此验证关。

**对修复计划的影响**：批次 1 的 SpeedGraph 任务（原任务 1）撤销；M2 升级为批次 1 唯一内存修复且证据强化；C5 的优先级应重估（可达性最广、放大最多）——由专家评估最终定夺（见 `docs/superpowers/plans/2026-11-11-performance-fixes-batch1.md` 的评估修订版）。

