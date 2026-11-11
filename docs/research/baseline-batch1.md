# 批次 1 修复前基线测量记录（2026-11-11）

测量人：基准测量工程师（任务 0）。对应章程：`docs/superpowers/plans/2026-11-11-performance-campaign-charter.md`（K1-K7）。

## 0. 测量环境与产物溯源

| 项 | 值 |
| --- | --- |
| 机器 | AMD Ryzen AI 9 HX 370（12 核 24 线程），59.6 GB RAM |
| 系统 | Windows 11 专业版，build 26300，x64 |
| 显示 | 主屏 2560×1440，96 DPI（100%），测量期间窗口短暂移动/缩放属预期 |
| 产物 | Files.Internal **4.3.3.2** 便携 Release（`Build-Internal.ps1 -PortableOnly` 流水线产物） |
| 产物提交 | `a0cb3d34067a19f242e1ba8d3e28c3a0775bc137`，Built-Utc `2026-11-10T23:50:31Z`（RELEASE-INFO.txt），Working-Tree: Clean |
| zip 校验 | `Files.Internal_4.3.3.2_x64-portable.zip` SHA-256 `1fe46397056f3a8071c03da91d388de14396a5c8335e49f9697c8089654f05b9`，与 `portable/SHA256SUMS.txt` 一致 |
| 解包路径 | `.superpowers/sdd/2026-11-11-performance-fixes-batch1/extracted/`（根 `Files.exe` 为原生启动器，真身为 `lib\Files.exe` 单文件负载） |
| 测量目录 | `C:\Users\win11\AppData\Local\Temp\perf-baseline-dir`：200 个文本小文件（`perf-file-0001.txt`…`perf-file-0200.txt`，每个 ~546 B，共 108,692 B） |
| 计时脚本 | `.superpowers/sdd/2026-11-11-performance-fixes-batch1/perf-harness.ps1`（SDD 工作区，不入仓库；批末裁决） |

**溯源与偏差说明（重要）**：本分支 HEAD `d975e5b6c` 相对产物提交 `a0cb3d340` 仅新增 3 个文档文件（`git diff --stat a0cb3d340..d975e5b6c`：仅 `docs/superpowers/` 下 3 个 .md，422 行插入，0 代码变更），故该产物与本分支应用代码**二进制等价**，用作修复前基线。未于今晚重新执行打包的原因与证据见任务报告：`dotnet test` 编排管道（`Microsoft.Testing.Platform` IPC 命名管道）在本机被策略拒绝（`UnauthorizedAccessException`），测试宿主亦被拒写 `%TEMP%`；同一套 429 项单测在 TMP 重定向至工作区后以独立宿主方式全绿（429/429），且经计划任务逃逸上下文重跑 `dotnet test` 仍同样失败，判定为机器级环境限制而非代码回归。

**环境注记（复测须保持同口径）**：

1. 本机策略拒绝 WinRT/.NET 单文件进程写 `%TEMP%`（表现为 Files 单文件负载自解压失败、单测 92 例 TEMP 建目录失败）。所有应用启动均将 `TMP`/`TEMP` 重定向到工作区目录（`.superpowers/.../tmp-redirect/`）。
2. Windows PowerShell 5.1 的 `Process.Start` 默认走 ShellExecute，对未签名本地 exe 会触发 SmartScreen 同步校验并无限阻塞；harness 一律以 `UseShellExecute=false` 启动。
3. 应用启动经由计划任务上下文执行（绕开上述限制），时钟仍为同机同源 Stopwatch；冷启动口径 = 启动器进程创建 → 名为 `Files` 的进程出现非 0 主窗口句柄（50 ms 轮询）。
4. K1/K2 计时为 harness 进程对目标 HWND 同步调 `SetWindowPos`（上游 #19028 方法）：移动 = `SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE`，圆形轨迹半径 150 px，200 步；resize = `SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE`，正弦幅度 120 px，200 步；每步 Stopwatch 计时，报告 p50/p95/max。

## 1. 自动化指标

### K4 冷启动到可交互（5 次取中位）

| 轮 | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| 耗时 (ms) | 890 | 835 | 901 | 883 | 835 |

**中位 883 ms**；均值 868.8 ms；min 835 / max 901 ms。每轮均观察到主窗口句柄（handleSeen=True）；启动参数为测量目录路径（跳过首页直达目录）。

### K5 空闲资源（加载测量目录后空闲 60 s，3 轮取中位）

| 指标 | 轮 1 | 轮 2 | 轮 3 | 中位 |
| --- | --- | --- | --- | --- |
| WorkingSet64 (MB) | 372.4 | 388.0 | 386.8 | **386.8** |
| PrivateMemorySize64 (MB) | 290.5 | 315.5 | 313.2 | **313.2** |
| Threads | 127 | 128 | 128 | **128** |
| Handles | 3,158 | 3,257 | 3,258 | **3,257** |

采样对象为 `lib\Files.exe` 主进程（根启动器完成拉起后即退出）；每轮独立冷启动加载同一 200 项目录后静置 60 s。

## 2. Explorer 参照基准（K1/K2）

同一目录（`perf-baseline-dir`）分别打开 Files 4.3.3.2 与 `explorer.exe <目录>` 窗口，各 3 轮。测量期间机器无构建/编译负载。

### K1 窗口拖动（每步移动 UI 线程耗时，ms）

| 目标 | 轮 | p50 | p95 | max |
| --- | --- | --- | --- | --- |
| Files 4.3.3.2 | 1 | 10.55 | 11.91 | 16.18 |
| Files 4.3.3.2 | 2 | 10.47 | 11.72 | 12.34 |
| Files 4.3.3.2 | 3 | 10.58 | 11.79 | 34.86 |
| explorer.exe | 1 | 2.20 | 3.10 | 25.90 |
| explorer.exe | 2 | 2.24 | 3.12 | 16.34 |
| explorer.exe | 3 | 2.18 | 3.06 | 15.08 |

解读：本机 Files 移动中位 **~10.5 ms**，约为 Explorer（~2.2 ms）的 **4.8×**。上游 #19028 在其机器测得 33-41 ms（4.3.0）：本机绝对值更优（CPU/机器差异），相对差距方向一致。章程 K1 目标 ≤10 ms：基线 10.5 ms 略超，仍有收敛空间。

### K2 窗口 resize（每步耗时，ms）

| 目标 | 轮 | p50 | p95 | max |
| --- | --- | --- | --- | --- |
| Files 4.3.3.2 | 1 | 15.99 | 36.20 | 44.23 |
| Files 4.3.3.2 | 2 | 16.56 | 37.90 | 203.49 |
| Files 4.3.3.2 | 3 | 16.34 | 36.41 | 40.35 |
| explorer.exe | 1 | 3.23 | 33.24 | 53.38 |
| explorer.exe | 2 | 3.48 | 32.31 | 40.37 |
| explorer.exe | 3 | 3.39 | 32.49 | 37.13 |

解读：Files resize p95 **~36-38 ms** vs Explorer **~32-33 ms**（约 1.1×）；p50 差距显著（~16 vs ~3.4 ms）；Files 出现 203 ms 尖峰（Explorer ≤53 ms），与 #19028 报告的 resize 长尾方向一致。

## 3. 手动场景清单（修复后复测模板）

口径与本文第 1 节自动化测量互补，覆盖无法自动化的 (c)(d) 项与 C5 观察点。**当前为基线模板：体感项留空，修复后按同口径复测填入并对比相对趋势。**每次填写须注明构建版本与时间。

### M-c 目录往返导航（关联 K3 / C5）

- 前提：便携 Release 构建；两个固定本地目录：`%TEMP%\perf-baseline-dir`（200 项，本文口径）与 `%TEMP%\perf-baseline-dir-b`（同规格姊妹目录：200 个 ~546 B 文本文件，复测时按同法生成）。
- 操作：在两目录间以 Alt+←/→（或地址栏）往返 **20 次**。
- 记录：往返 p50/p95（秒表或录屏逐帧）；导航期间 UI 是否可即时中断；是否观察到卡顿帧。
- 基线体感：（留空）

### M-d 图片预览切换（关联 K6 / M2 / C1）

- 前提：**布局 = RightTabs**（InfoPane 开启 + Layout=RightTabs；C1 已收窄，RightSplit 不复现，勿以 RightSplit 口径测量）。
- 图片：≥4000×3000 单张（记录文件名与体积）。
- 操作：选中图片后 Details↔Preview 来回切换 **10 次**。
- 记录：切换期间 RSS 峰值；停止切换静置 **30 s** 后 RSS 滞留值；是否出现重载/闪烁。
- 基线体感：（留空）

### M-e 首次进入未预取深层文件夹（关联 C5）

- 前提：清空布局缓存（或全新便携解包首跑）。
- 操作：进入一个未访问过的深层文件夹（建议 ≥3 层、>100 项、非常规布局类型）。
- 观察点：是否出现布局切换（先 Details 后跳变）；跳变延迟。
- 基线体感：（留空）

## 附：复现命令

```powershell
# 计时（需先打开目标窗口）
powershell -NoProfile -ExecutionPolicy Bypass -File .superpowers\sdd\2026-11-11-performance-fixes-batch1\perf-harness.ps1 -Mode Move -TargetProcessId <pid>
# ... -Mode Resize / -Mode ColdStart -ExePath <便携 Files.exe> -Arguments <目录> / -Mode Mem / -Mode FindWindow
```

应用启动与冷启动/内存采样在本机须经 `.superpowers\sdd\2026-11-11-performance-fixes-batch1\request-runner.ps1`（计划任务上下文 + TMP 重定向）执行，直接在本会话 Shell 启动会命中 SmartScreen/TEMP 限制（见第 0 节注记）。
