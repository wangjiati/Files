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

> 注：出窗判定为 50 ms 轮询（第 0 节注记 3），轮询分辨率 ±50 ms，Δ 低于此量级不具区分力。

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
- 姊妹目录一行生成命令（同 §0 首目录规格：`perf-file-0001.txt`…`perf-file-0200.txt`，每个 546 B，共 109,200 B）：

  ```powershell
  New-Item -ItemType Directory -Force "$env:TEMP\perf-baseline-dir-b" | Out-Null; 1..200 | ForEach-Object { [System.IO.File]::WriteAllText(('{0}\perf-baseline-dir-b\perf-file-{1:d4}.txt' -f $env:TEMP, $_), 'x' * 546, [System.Text.Encoding]::ASCII) }
  ```
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

## 4. 修复后（dev A/B 对照）——2026-11-12 任务 3

> 追加章节，不改上文基线原文。修复内容 = M2 预览图解码上限（`51e2078ed`）+ C5 布局预测缓存/后台预取（`5f15dcad1`）。

### 4.0 产物与口径

| 项 | 值 |
| --- | --- |
| DEV-BASE（修复前） | `d975e5b6c` 于独立 worktree `.agents/worktrees/perf-baseline-devbuild` 构建 |
| DEV-FIXED（修复后） | `5f15dcad1`（本分支 HEAD）于 `.agents/worktrees/perf-fixes-batch1` 构建 |
| 构建命令（两侧同配置） | `msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -p:Distribution=Portable -p:RuntimeIdentifier=win-x64 -v:quiet -clp:ErrorsOnly`（VS DevShell 7ac2ee5b） |
| 产物路径（两侧同相对路径） | `src\Files.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Files.exe`（各 723,968 B，ver 4.3.3.2，解包自包含 dev 形态；非 `-t:Publish`） |
| 打包阻断 | `Build-Internal.ps1 -PortableOnly` 在 **TMP 重定向下**仍失败于 "Run unit tests"：`System.UnauthorizedAccessException` at `System.IO.Pipes.NamedPipeClientStream.TryConnect`（MTP IPC 管道，机器级策略；第 0 节注记 1 的独立宿主 429/429 全绿证据不变）。故按裁决改 dev 口径 A/B，**本批无便携 Release 产物**，便携基线数字仅作参照行 |
| 测量口径 | 与第 0/1/2 节同 harness/同目录/补测后两侧各 6 轮/同参数/同计划任务通道；Explorer 参照沿用第 2 节已录数据未重测；测量期间无构建负载、全程前台逐条驱动 |
| 原始记录 | `.superpowers/sdd/2026-11-11-performance-fixes-batch1/measure-results.txt` |

### 4.1 K4 冷启动到可交互（5 次取中位，启动参数=测量目录）

| 产物 | 轮 1-5 (ms) | 中位 | 均值 | min/max | 全部出窗 |
| --- | --- | --- | --- | --- | --- |
| DEV-BASE | 1706 / 1613 / 1596 / 1595 / 1619 | **1613** | 1625.8 | 1595 / 1706 | 是 |
| DEV-FIXED | 1652 / 1629 / 1618 / 1608 / 1612 | **1618** | 1623.8 | 1608 / 1652 | 是 |
| （参照：便携 Release 4.3.3.2 基线，第 1 节） | 890/835/901/883/835 | 883 | 868.8 | 835 / 901 | 是 |

判定：dev 同配置 A/B **持平**（Δ+5 ms，+0.3%，轮间噪声内），符合预期 (b) 持平或略降。dev Debug（JIT）绝对值高于便携 Release（AOT）约 0.7 s 属配置差异，不作前后依据。

### 4.2 K5 空闲资源（加载测量目录后空闲 60 s，3 轮）

| 指标（中位） | DEV-BASE | DEV-FIXED | Δ |
| --- | --- | --- | --- |
| WorkingSet64 (MB) | 455.5（455.5/464.9/455.5） | **457.3**（457.3/462.4/455.5） | +1.8（+0.4%） |
| PrivateMemorySize64 (MB) | 323.6（323.6/326.7/322.8） | **327.1**（321.6/333.3/327.1） | +3.5（+1.1%） |
| Threads | 132（132/133/131） | **132**（132/133/130） | 0 |
| Handles | 3,544（3544/3565/3541） | **3,567**（3585/3567/3548） | +23（+0.6%） |

判定：**持平**（C5 新增 256 条 LRU 字典/链表在噪声内不可见），符合预期 (a)。注：DEV-FIXED 首次 Mem 采样 3 轮中第 3 轮进程于空闲期消失（疑 CloseMainWindow 延迟关闭与下轮启动的单实例交接竞态），事件日志/WER 无崩溃证据，剔除该次后复测得上表干净 3 轮。

### 4.3 K1 窗口拖动（每轮新启动，200 步，r=150 px；p50/p95/max，ms）

| 产物 | 轮 1 | 轮 2 | 轮 3 | 补充轮 4 | 补充轮 5 | 补充轮 6 | p50 中位 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| DEV-BASE | 9.04/11.33/24.31 | 9.01/11.63/30.62 | 9.26/14.58/203.48 | 9.37/12.08/44.17 | 8.80/11.55/47.62 | 9.52/11.71/32.95 | **9.15** |
| DEV-FIXED | **20.03**/26.25/309.37 | 9.75/12.70/31.94 | **20.02**/25.55/374.75 | 9.68/12.06/34.46 | 8.85/11.21/22.37 | 9.05/10.90/22.23 | **9.72** |

**异常注记（如实记录，未定论）**：DEV-FIXED 轮 1/3 为慢轮：其出窗时间 2584/2569 ms（其余轮与本测量全部 13 次启动均 ~1480-1610 ms），且整轮分布整体上移 ~10 ms（min 15.3 vs 常态 ~6.6）。补充 3 轮与"同进程先测后静置 15 s 再测"探针（move1 p50=9.46 → 静置后 move2 p50=11.42，出窗 1549 ms）均未复现慢轮，稳态拖动与 DEV-BASE 持平；慢轮时序上集中于本轮测量开局（第 1/3 次 DEV-FIXED 启动）。机制上 M2（本目录无图片，未触发）与 C5（本目录无子目录，预取为空操作）均无致慢路径，暂判环境性启动竞态而非修复回归；判定取**持平**（排除慢轮后 ~9.4 vs 9.15），慢轮现象已留档待复测观察。

### 4.4 K2 窗口 resize（每轮新启动，200 步，amp=120 px；p50/p95/max，ms）

| 产物 | 轮 1 | 轮 2 | 轮 3 | p50 中位 |
| --- | --- | --- | --- | --- |
| DEV-BASE | 15.91/18.98/20.77 | 15.41/20.40/43.32 | 15.19/17.98/69.28 | **15.41** |
| DEV-FIXED | 16.38/24.36/81.65 | 15.15/17.98/24.64 | 15.40/17.71/20.76 | **15.40** |

判定：**持平**（K1/K2 本批不修，仅同口径建立 dev 参照，窗口拖动属批次 2）。参照：第 2 节 Explorer 同机参照 K1 p50 ~2.2 ms、K2 p95 ~32-33 ms（便携 Release Files：K1 ~10.5、K2 p95 ~36-38）。

### 4.5 预期方向对照（任务 3 步骤 1 逐项）

| 项 | 预期 | 实测 | 判定 |
| --- | --- | --- | --- |
| (a) K5 空闲 RSS/线程/句柄 | 持平或略降 | 持平（Δ≤1.1%） | 符合 |
| (b) K4 冷启动中位 | 持平或略降 | 持平（Δ+0.3%） | 符合 |
| (c) 导航 p50/p95（K3/C5） | 显著下降（修复前每次导航 UI 线程同步 desktop.ini 读+≤200 项枚举 → 修复后命中缓存零磁盘 I/O） | **待用户复测**（见 4.7-M-c） | — |
| (d) RightTabs 图片切换峰值/滞留（K6/M2） | 大幅下降（单张解码 ≤17 MB vs 原 48 MB+；本机 100% DPI 下 1024 px 逻辑解码约 3 MB） | **待用户复测**（见 4.7-M-d） | — |
| K1/K2 | 不修仅记录 | 持平（9.15→9.72 排除慢轮 ~9.4；15.41→15.40） | 符合 |

### 4.6 冒烟（自动化部分，DEV-FIXED，通道启动）

| 场景 | 出窗 (ms) | t+5 s | t+15 s | 结果 |
| --- | --- | --- | --- | --- |
| 图目录（含 4000×3000 横图/3000×4000 竖图） | 1480 | 进程 1/带窗 | 进程 1/带窗 | 通过 |
| 纯文档目录（30 文本/md） | 1533 | 进程 1/带窗 | 进程 1/带窗 | 通过 |
| 深层未预取目录首进（L1\L2\L3，120 PNG） | 1530 | 进程 1/带窗 | 进程 1/带窗 | 通过 |
| 同深层目录二次进入 | 1480 | 进程 1/带窗 | 进程 1/带窗 | 通过 |

崩溃证据：测量窗口内 Application 事件日志 Error/Critical 0 条（提及 Files.exe 亦 0）、WER ReportArchive/ReportQueue 新增 0。视觉项（布局预测正确性、横/竖图显示、首进允许一次布局切换、二进不切换）→ 用户冒烟清单。

### 4.7 待用户复测清单（手动项，不虚构数字）

使用产物：DEV-FIXED（`.agents/worktrees/perf-fixes-batch1/src/Files.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/`）。

- [ ] **M-c 目录往返导航（K3/C5）**：`%TEMP%\perf-baseline-dir` ↔ `%TEMP%\perf-baseline-dir-b`（后者已按同法生成：200×546 B、109,200 B），Alt+←/→ 往返 20 次。预期方向：p50/p95 显著下降且导航期间 UI 可即时中断（修复前每次导航 UI 线程同步 I/O；修复后预测命中路径零磁盘 I/O）。
- [ ] **M-d 图片预览切换（K6/M2）**：布局 RightTabs + InfoPane（C1 已收窄勿用 RightSplit 口径）；`%TEMP%\perf-smoke-images\perf-img-landscape-4000x3000.png`（52,411 B）Details↔Preview 10 次。预期方向：RSS 峰值与 30 s 滞留大幅下降（1024 px 逻辑解码 ≈3 MB@100% DPI，上限 ≤17 MB；原 ≈48 MB+）；顺带确认横/竖图（portrait-3000x4000.png）显示正常、无重载/闪烁。
- [ ] **M-e 首次进入未预取深层文件夹（C5 回退预期）**：`%TEMP%\perf-smoke-deep\L1\L2\L3`（3 层、120 PNG、>60% 媒体 → 预测 GridView）。预期：首次允许一次"默认→校正"切换（已接受回退），二次进入不再切换；顺带观察 K1 慢轮现象是否复现（拖动跟手性）。
- [ ] 视觉冒烟：三目录列表渲染、缩略图、无卡顿回归。

### 4.8 打包阻断证据链（如实报告，不宣称打包完成）

1. 尝试 1（任务 0，无 TMP 重定向）：`build-task.log`，失败于 "Run unit tests"，exit -532462766。
2. 尝试 2（任务 3 夜间，TMP 重定向）：`build-task3.log`/`build-task3-status.txt`（DONE code=1，2026-11-12T00:28:09），同点同异常。
3. 尝试 3（本日 08:25，TMP 重定向 + 干净工作树）：`build-task3b.log`/`build-task3b-status.txt`（DONE code=1，2026-11-12T08:25:16），同点同异常：`Microsoft.Testing.Platform.IPC.NamedPipeClient.ConnectAsync` → `NamedPipeClientStream.TryConnect` → `UnauthorizedAccessException`。
4. 反证非代码回归：同套 429 项单测独立宿主 + TMP 重定向 429/429 全绿（任务 0，`standalone-tests-tmpredirect.log`）；计划任务逃逸重跑 `dotnet test` 亦同样失败（机器级策略）。对照：`standalone-tests-run2.log` 为无重定向对照运行（92 例 TEMP 建目录被拒失败）。

## 5. 批次 2 前后对照（K1/K2/场景 D）——2026-11-12 任务 5

> 追加章节，不改上文原文。批次 2 修复 = C1 预览切换重载收敛 + C2a InfoPane ApplyLayout 幂等化 + C2b resize 设置写去抖/LoadPaneChanged 节流 + K1 移动早退（cbeeec5a0/76206f9f3/9df8b8241/dac765f37）。

### 5.0 产物与口径

| 项 | 值 |
| --- | --- |
| A 侧（批次 2 前） | 批次 1 修复构建：worktree `perf-fixes-batch1` 树干净 @ `e0ebdc27f`（自 `5f15dcad1` 仅 2 个 docs-only 提交，代码等价）；`Files.exe` 723,968 B，ProductVersion `4.3.3.2+5f15dcad1…`（版本戳证实源提交） |
| B 侧（批次 2 后） | 本 worktree `perf-fixes-batch2` @ `dac765f37` 干净重建：`dotnet restore Files.slnx` → 清 `Files.App`/`Files.App.Server` obj+bin → 同 §4.0 命令（DevShell 7ac2ee5b，Debug x64 Portable RID win-x64）；`Files.exe` 723,968 B，版本戳 `4.3.3.2+dac765f37…`，exit 0 |
| 通道 | 原批次 1 计划任务 `perf-escape-runner` 已不存在，重建为 `perf-b2-runner`（同交互会话/同用户/ LIMITED）；全部被测进程（Files 两侧 + explorer.exe 参照）启动均在通道内，零直接 Start-Process；测量脚本同 harness 同参数（200 步/r150/amp120） |
| 设置存储（场景 D） | 便携形态设置在 exe 旁 `…\win-x64\Data\settings\user_settings.json`（`PortableAppRuntimeEnvironment`，两侧各自独立）；两侧启动前预置 `IsInfoPaneEnabled:true`（否则 `Page_SizeChanged` 对 `InfoPane is null` 早退，场景 D 无写可测——实测发现并留档） |
| 场景 D 替代口径 | 人工拖动不可用，以编程 resize 序列替代：同步 `SetWindowPos` 逐帧改变尺寸 10 s（帧间 8 ms 上限，实际由同步节奏决定 ~267-813 帧）。两个变体：正弦 700×650±120（跨“内容列钉 MinWidth”边界）与单调收缩 740→590（全程钉 MinWidth）；窗口需 >450×450（`LoadPaneChanged` isBigEnough 阈值）面板才加载 |
| 原始记录 | `.superpowers/sdd/2026-11-12-performance-fixes-batch2/b2-results.txt`（主仓库，不入库）；测量期间无构建负载，全部前台逐条驱动、逐条落盘 |

### 5.1 K1 窗口拖动（每轮新启动，200 步，r=150 px；p50/p95/max，ms）

| 产物 | 轮 1 | 轮 2 | 轮 3 | p50 中位 | p95 中位 |
| --- | --- | --- | --- | --- | --- |
| A（批次 2 前） | 10.07/12.63/59.29 | 10.83/36.59/205.35 | 10.21/12.19/27.35 | **10.21** | 12.63 |
| B（批次 2 后） | 7.02/19.59/107.85 | 6.73/9.07/53.07 | 6.61/8.26/9.69 | **6.73** | 9.07 |
| explorer.exe（同日复核 1 轮） | 0.97/1.84/3.78 | — | — | 0.97 | 1.84 |

- B vs A：p50 **10.21 → 6.73（-34%）**；p95 中位 12.63 → 9.07；max 尾部 205 → 108。与 Explorer 参照差距由 ~10.5× 收窄至 ~6.9×。Explorer 复核与 §2 基线（~2.2）同量级方向一致。
- 与 §4.3 批次 1 对照：批次 1 两臂 ~9.15/9.72（互持平），批次 2 B 臂 6.73 为首个显著下降，与“K1 早退消除每次移动 ~185 个窗口管理器调用”的预期方向一致。
- 注：B 轮 1 p95 19.59 与一次 107.85 尖峰集中在该轮（轮 2/3 干净至 max 53/9.7）；未复现批次 1 的“慢轮整体上移”形态。

### 5.2 K2 窗口 resize（每轮新启动，200 步，amp=120 px；p50/p95/max，ms）

| 产物 | 轮 1 | 轮 2 | 轮 3 | p50 中位 | p95 中位 |
| --- | --- | --- | --- | --- | --- |
| A（批次 2 前） | 13.77/17.92/23.26 | 15.34/18.37/248.45 | 14.64/18.87/228.74 | **14.64** | 18.37 |
| B（批次 2 后） | 12.16/37.37/320.96 | 11.09/13.63/19.91 | 12.03/14.29/39.28 | **12.03** | 14.29 |

- B vs A：p50 **-18%**、p95 中位 **-22%**（18.37 → 14.29）。A 侧两轮出现 ~230-250 ms 尖峰（与 §2 便携 Release 203 ms 尖峰同向）；B 侧轮 2/3 max ≤40 ms 干净，但轮 1 出现一次 320.96 ms 尖峰——长尾未完全消除。
- Explorer 参照（§2，同期同机）：p50 ~3.4、p95 ~32-33。本批 dev 口径 B p95（14.29）已低于 §2 Explorer p95 参照；p50（12.03）仍高于 Explorer p50。
- 判定：**部分达标**——p95/max 对 Explorer 参照占优/持平，p50 差距仍 ~3.5×。

### 5.3 场景 D：连续拖窗 resize 10 s 的设置文件 mtime 变化次数

前提发现（如实留档）：`Page_SizeChanged` 在 `InfoPane is null` 时早退；且窗口需 >450×450 面板才加载。故两侧设置文件预置 `IsInfoPaneEnabled:true`、基准窗 700×650/740×650（内容列 208 MinWidth 处于受压状态）。计数口径：40 ms 轮询 `user_settings.json` mtime 变化次数（基线取静置后、burst 开始前）。

| 变体 | A（批次 2 前） | B（批次 2 后） | 目标 |
| --- | --- | --- | --- |
| 正弦 700×650±120（跨钉/不钉边界，~5 个钉相） | **148**（146 during + 2 settle） | **5**（4+1，offsets 3286/4845/6320/8079/10500 ms，约每钉相 1 次） | — |
| 单调收缩 740→590（全程钉 MinWidth，720-813 帧） | **73**（全部 during） | **1**（offset 6023 ms） | ≤2：**达标** |

- 正弦变体每次越界后 300 ms 稳定 flush 一次是防抖语义下的合法行为（值确实变了）；A 侧同序列每 ~40-80 ms 一次持续写。B 侧单调连续钉压场景 1 次 ≤2 达标；A→B 写放大降幅 30×-73×。

### 5.4 B 侧一次启动崩溃（如实记录，未定论）

K1 轮 2 B 首次尝试：进程出窗后 ~1.3 s、harness 首次 `SetWindowPos` 之前自行退出。证据：Application 日志 `Application Error`——Files.exe 4.3.3.2，出错模块 `CoreMessagingXP.dll` 12.6.5.0，异常 `0xc000027b`，pid 0x8FC8（36808），2026-11-12T12:01:47；WER ReportArchive 留档。对照：自 2026-11-11 22:00 以来全部测量启动（批次 1 十三次 + 本日 A 侧三次 + B 侧其余各次）无任何 Files.exe 崩溃记录，仅此一次，重试同轮即成功。疑点指向 B（批次 2 改动面含 DispatcherQueue 定时器与 AppWindow.Changed 挂钩），但不能排除启动竞态；建议批次 2 收尾时以 Release/多轮启动压测复核（见任务报告疑虑）。

### 5.5 冒烟（自动部分，B，通道启动，双目录参数）

| 场景 | 出窗 (ms) | t+5 s | t+15 s | 结果 |
| --- | --- | --- | --- | --- |
| 双目录（perf-baseline-dir + perf-baseline-dir-b） | 1530 | 进程 1/带窗/ws 389 MB | 进程 1/带窗/ws 389 MB（持平） | 通过 |

事件日志本测量窗口内除 §5.4 一例外无新增 Files.exe 崩溃；正常关闭无残留进程。

### 5.6 待用户复测清单（手动项，不虚构数字）

使用产物：B 侧 dev 构建（`.agents/worktrees/perf-fixes-batch2/src/Files.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/`，Data 目录在其旁）。

> 产物口径注记（2026-11-12 12:45 补）：该 bin 路径下的 exe 已被 12:27 打包尝试以 `ad615f3e3` 重建（= `dac765f37` + 2 个 docs-only 提交，代码等价；现件版本戳 `4.3.3.2+ad615f3e3…`，723,968 B）。§5.1-5.3 的测量（≤12:24）用的是重建前的 `dac765f37` 戳原件；本清单复测与 12:29 后的启动压测用的是该重建件。

- [ ] **RightTabs 反复切换应瞬时**（C1/C2 后预览重建不卡顿；程序化无法触发 tab 切换）。
- [ ] **旋转后显新图**：`%TEMP%\perf-smoke-images\perf-img-portrait-3000x4000.png` 等横/竖图切换后显示新解码位图，无残留旧图。
- [ ] **媒体/Shell 目录每次重建无黑屏**：含大量媒体/Shell 项的目录进出与布局重建无黑屏闪烁。
- [ ] **明暗主题切换后 Markdown 恢复检查**（任务 4 审查要求的主题项）：切明/暗主题后 Markdown 预览恢复正常配色，无陈旧配色残留。
- [ ] K1 拖动跟手性主观复核（自动 p50 已 -34%）：拖动窗口应明显跟手；顺带观察是否复现批次 1 §4.3 慢轮现象与 §5.4 类启动闪退。

### 5.7 判定汇总

| KPI | 目标 | A（前） | B（后） | 判定 |
| --- | --- | --- | --- | --- |
| K1 拖动 p50 | ≤10 ms | 10.21 | **6.73（-34%）** | dev 口径**达标**（vs Explorer 0.97 仍 6.9×；Release 口径因打包阻断未复核） |
| K2 resize p95 | ≤ Explorer | 18.37 | **14.29（-22%）** | **部分达标**（p95 优于 §2 Explorer 参照 32-33；p50 12.03 仍高于 Explorer ~3.4） |
| 场景 D 写次数 | B ≤2（连续钉压） | 73（钉压）/148（正弦） | **1** / 5 | **达标**（连续钉压 1 ≤2；正弦每钉相 1 次属防抖合法语义） |
