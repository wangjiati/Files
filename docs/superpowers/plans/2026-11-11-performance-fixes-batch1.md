# 性能修复批次 1（止血批）实现计划（v2，经交叉验证+专家评估修订）

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 修复预览图片全尺寸解码（M2）与导航路径 UI 线程同步磁盘 I/O（C5，缓存式设计），并以同配置产物建立修复前后测量基线与 Explorer 参照基准。

**架构：** 三个代码文件的小改动，不改任何公共签名、不动服务器边界。C5 采用有界预测缓存 + 枚举完成后后台预取：`GetLayoutType` 同步签名不变（调用点 0 改动），缓存 miss 时跳过预测（回退到 #18888 之前的"默认布局 + 枚举后校正"行为，`ApplyAdaptativeLayout` 事后校正仍存在于 BaseShellPage.cs:816 与 ShellViewModel.cs:2542 挂点）。

**技术栈：** C# / WinUI 3 / .NET；构建 msbuild，打包与测量产物统一走 `.github/scripts/Build-Internal.ps1 -PortableOnly`（修复前后各构建一次，避免 Debug/Release 配置错配）。

**规格：** `docs/research/2026-11-11-performance-memory-investigation.md`（执行者必读 §2 M2、§1 C5、§8 交叉验证结论——**注意 M1 SpeedGraph 已证伪，本计划不含任何 SpeedGraph 改动**）。

## 战役总览与范围决策记录

范围决策（用户授权代理决策，2026-11-11）：全部 13 项发现都修，按风险分批；上游策略 = 先本地后观察（跟踪 #4180/#19028/PR#19004）。原批次 1 的 SpeedGraph 任务经 §8 三方交叉验证**证伪撤销**；经专家评估（2026-11-11）C5 以缓存设计**提前进批次 1**，C2 半修方案否决（避免同一根因验证拆两批）。北欧星目标与团队章程见 `docs/superpowers/plans/2026-11-11-performance-campaign-charter.md`。

| 批次 | 内容 | 状态 |
| --- | --- | --- |
| 1（本计划 v2） | M2 图片解码上限、C5 预测缓存+预取、基线+Explorer 参照 | 本文档 |
| 2 | 自有回归：C1 预览切换重载收敛、C2 ApplyLayout 幂等化 + resize 节流 + 每 tick 设置写移除 | 轮到时另立计划 |
| 3 | 中风险：C6 设置写入去抖、M3 三缓存上限（原 C5 已提前） | 轮到时另立计划 |
| 4 | 高风险：C3 选择路径增量化、C4 集合增量更新、M5 窗口树滞留；上游回流评估 | 轮到时另立计划 |

## 全局约束

- 遵循 AGENTS.md：CRLF；`msbuild` 加 `-v:quiet -clp:ErrorsOnly`；不并行构建；不 bump 内部版本号（沿用 4.3.3.2）。
- 仓库无适用于 AI 代理的测试套件：每任务验证 = 项目级 msbuild 成功 + 计划内运行时测量。
- 打包必须走 `.github/scripts/Build-Internal.ps1 -PortableOnly`（先 commit 后打包）；**所有前后对比测量使用该脚本产物**（便携 Release 同配置），不得用 Debug 产物做对比。
- 不动 `src/Files.App.Server*`、`Package.appxmanifest`、CsWin32 输入。
- Commit 前运行 `git status --short` 与 `git diff --check`。
- 文本文件必须 CRLF；PowerShell 读写文本一律用 .NET API 显式 UTF-8 编码（`[System.IO.File]::ReadAllText/WriteAllText` + `UTF8Encoding($false)`），禁用 `Get-Content`/`Set-Content` 默认编码（本机默认 GBK 会损坏 UTF-8 中文）。

## 审查重点（Review Focus）

1. **C5 缓存不得成为第 4 个无界缓存**：预测缓存必须有容量上限（256 条）与 LRU 淘汰；预取每次 ≤64 个子目录、每目录 ≤200 项采样（沿用 TryPredictLayout 现有上限）；导航离开必须取消在途预取（复用本次加载的取消令牌）。
2. **缓存 miss 的行为回退是有界的且已被接受**：未预取的文件夹首次进入可能先显示默认布局、枚举完成后由 `ApplyAdaptativeLayout` 校正为正确布局（一次可见切换）——这是回到 #18888 之前的既有行为，审查者不得把它当缺陷要求"补同步预测"。
3. **竖幅/全景图片**：只设 `DecodePixelWidth` 时极端竖长图解码反而更大；必须用 BitmapDecoder 先读原始像素尺寸，取**较大边**设上限（该规则对 EXIF 旋转鲁棒：XAML BitmapImage 不自动应用 EXIF，原始尺寸即解码尺寸）。
4. **清晰度取舍已接受**：1024 逻辑像素上限在高 DPI 下物理解码最大 ≈17MB；Bottom 布局宽窗格下可能可见模糊——接受项，不得调大或移除上限。
5. **异常语义不变**：ImagePreview 路径不加 try-catch（现状由上层处理）；损坏文件行为与现状一致。

---

### 任务 0：修复前基线 + Explorer 参照（便携 Release 产物 #1）

**文件：** 无代码改动；产物 `docs/research/baseline-batch1.md` 与 SDD 工作区 `perf-harness.ps1`。

- [ ] **步骤 1：打包基线产物**

运行：`pwsh -NoProfile -ExecutionPolicy Bypass -File .github/scripts/Build-Internal.ps1 -PortableOnly`
预期：单测全绿，产出 `artifacts/internal/4.3.3.2/portable/*.zip`（以脚本输出为准），记录解包路径。

- [ ] **步骤 2：采集基线指标（自动化部分）**

解包运行便携 exe（传参打开固定本地目录），采集并记入 baseline 文件：(a) 空闲 60s 后 WorkingSet64/PrivateMemorySize64/Threads/Handles（3 次取中位）；(b) 冷启动到可交互耗时（进程启动到主窗口句柄出现，5 次取中位）。指标 (c) 20 次目录往返导航 p50/p95 与 (d) RightTabs 图片切换峰值/滞留需 UI 交互：落为用户手动清单随 baseline 文件交付（口径：c=两个固定本地目录间 20 次往返；d=InfoPane 开启+Layout=RightTabs，选中 ≥4000×3000 图片，Details↔Preview 切换 10 次，记 RSS 峰值与停止 30s 后滞留；C1 已收窄，RightSplit 不复现）。

- [ ] **步骤 3：Explorer 参照基准（团队章程 K1/K2）**

编写窗口移动/缩放计时脚本（方法论：上游 #19028 的 `SetWindowPos` 同步跨进程计时：`SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE` 移动 200 步取中位/p95/max；resize 另测）。对**同一目录**分别测量 Files（步骤 1 产物）与 explorer.exe（`explorer.exe <目录>`），各 3 轮记入 baseline。脚本存 SDD 工作区 `perf-harness.ps1`，批末裁决是否入仓库。

- [ ] **步骤 4：手动场景清单（用户协作）**

列出修复后复测清单（口径同步骤 2 的 (c)(d)）；另加：首次进入未预取深层文件夹的布局切换观察点。当前体感可空缺，修复后对比相对趋势。

### 任务 1：M2 ImagePreview 解码上限

**文件：**
- 修改：`src/Files.App/ViewModels/UserControls/Previews/ImagePreviewViewModel.cs:26-38`

- [ ] **步骤 1：按较大原始边设置解码上限**

目标形态（较大边设限；上限 1024 逻辑像素为计划钉死取值；需要 `using Windows.Graphics.Imaging;`）：

```csharp
public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
{
	using IRandomAccessStream stream = await PreviewFile.OpenAsync(FileAccessMode.Read);

	var decoder = await BitmapDecoder.CreateAsync(stream);
	bool landscape = decoder.PixelWidth >= decoder.PixelHeight;

	await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
	{
		BitmapImage bitmap = new() { DecodePixelType = DecodePixelType.Logical };
		if (landscape)
			bitmap.DecodePixelWidth = Math.Min(1024, (int)decoder.PixelWidth);
		else
			bitmap.DecodePixelHeight = Math.Min(1024, (int)decoder.PixelHeight);
		stream.Seek(0);
		await bitmap.SetSourceAsync(stream);
		ImageSource = bitmap;
	});

	return [];
}
```

要点：decoder 读元数据移到 UI 分发之前（WinRT 异步 I/O 不占 UI 线程）；`FileAccessMode.Read` 对 `BitmapDecoder.CreateAsync` 合法（**不要**改 ReadWrite——只读文件上会失败）；同流复用前 `Seek(0)`（仓库先例 BitmapHelper.cs:73→87-91）；不加 try-catch。

- [ ] **步骤 2：构建验证**

运行：`pwsh.exe -NoProfile -Command "& { Import-Module 'C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\Tools\Microsoft.VisualStudio.DevShell.dll'; Enter-VsDevShell 1ba2cc4e -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'; msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly }"`
预期：无错误。

- [ ] **步骤 3：Commit**

```powershell
git status --short
git diff --check
git add src/Files.App/ViewModels/UserControls/Previews/ImagePreviewViewModel.cs
git commit -m "Cap preview image decode size to 1024px"
```

### 任务 2：C5 布局预测缓存 + 后台预取

**文件：**
- 修改：`src/Files.App/Helpers/Layout/AdaptiveLayoutHelpers.cs`（新增缓存与预取）
- 修改：`src/Files.App/Helpers/Layout/LayoutPreferencesManager.cs:273`（查询点改缓存）
- 修改：`src/Files.App/ViewModels/ShellViewModel.cs`（枚举完成后预取挂点，参照 :2542 附近 ApplyAdaptativeLayout 挂点）

- [ ] **步骤 1：AdaptiveLayoutHelpers 增加有界缓存与预取**

新增（与现有 `TryPredictLayout` 并存，后者逻辑不动）：`TryGetCachedPredictLayout(string? path, out FolderLayoutModes layout)`——查缓存；以及 `PrefetchPredictLayoutsAsync(IEnumerable<string> paths, CancellationToken ct)`——`Task.Run` 内对每个路径执行现有 `TryPredictLayout` 并写入缓存（跳过已缓存项）。缓存：容量 256 条 LRU（lock + LinkedList + Dictionary 实现即可），键为规范化路径（`Path.GetFullPath`）。禁止无界增长。

- [ ] **步骤 2：查询点切换 + 预取挂点**

(a) `LayoutPreferencesManager.cs:273` 的 `AdaptiveLayoutHelpers.TryPredictLayout(path, out var resolvedLayout)` 改为 `TryGetCachedPredictLayout(path, out resolvedLayout)`——miss 即返回 false（跳过预测，走默认布局，事后校正兜底）。签名与其余 20 个调用点零改动。
(b) `ShellViewModel` 枚举完成、`ApplyAdaptativeLayout` 被调用的同一位置：取 `FilesAndFolders` 中目录项的完整路径去重后**前 64 个**，`PrefetchPredictLayoutsAsync(paths, ct)` fire-and-forget（`_ = ` 丢弃），`ct` 复用本次加载的取消令牌（导航离开即取消在途预取）。参照 `ShellViewModel.cs:2967-2979` 的 Task.Run 先例。

- [ ] **步骤 3：构建验证**

同任务 1 步骤 2。预期：无错误。

- [ ] **步骤 4：Commit**

```powershell
git status --short
git diff --check
git add src/Files.App/Helpers/Layout/AdaptiveLayoutHelpers.cs src/Files.App/Helpers/Layout/LayoutPreferencesManager.cs src/Files.App/ViewModels/ShellViewModel.cs
git commit -m "Cache adaptive layout predictions and prefetch off the UI thread"
```

### 任务 3：修复后对比测量、打包与冒烟

**文件：**
- 修改：`docs/research/baseline-batch1.md`（追加修复后数据）

- [ ] **步骤 1：打包修复产物并同口径重测**

运行 Build-Internal（同任务 0 步骤 1），重复任务 0 步骤 2/3 全部指标。预期：(a)(b) 持平或略降；(c) 20 次导航 p50/p95 显著下降（修复前每次导航在 UI 线程同步做 desktop.ini 读+≤200 项枚举）；(d) 场景峰值与滞留大幅下降（单张解码 ≤17MB，DPI 相关，替换原 ≈48MB+）；K1/K2 相对 Explorer 的差距记录（本批不修窗口拖动，仅建立参照）。

- [ ] **步骤 2：冒烟（含 C5 回退预期）**

解包运行：打开多个含图/纯文档目录确认布局预测正常；**首次**进入未预取的深层文件夹允许出现一次"默认布局→校正布局"切换（已接受回退），二次进入不再切换；确认无卡顿回归。图片预览各方向（横/竖）正常显示。

- [ ] **步骤 3：汇报**

报告产物路径、前后对比表、KPI 更新（章程 K1-K7）、场景结论；批次 2 计划随后开工。
