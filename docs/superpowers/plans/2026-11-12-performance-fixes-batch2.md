# 性能修复批次 2 实现计划（v1 定稿，2026-11-12）

> **面向 AI 代理的工作者：** 必需子技能：subagent-driven-development 或 executing-plans。步骤复选框跟踪。

**目标：** 消除窗口拖动/resize 卡顿（K1/K2 对标 Explorer）与预览窗格切换全量重载（C1/C2），并清偿批次 1 终审甄别的 8 条延后项。

**架构：** 延后项清理 → C2 resize 链路幂等化+去抖 → K1 拖动区域早退（上游 #19028 建议方案）→ C1 预览双形态缓存+token。全部 UI 层文件，不动服务器边界。

**规格：** 调查报告 §1 C1/C2、§8 收窄结论；批次 2 设计草稿（batch2-design-draft.md）；终审甄别清单；上游 #19028。

**技术栈/工具：** 同批次 1（msbuild via DevShell 7ac2ee5b；测量走 perf-harness.ps1 + 计划任务通道；打包 Build-Internal -PortableOnly，阻断则如实报告）。

## 全局约束（承批次 1）

- AGENTS.md 规范：CRLF、-v:quiet -clp:ErrorsOnly、不并行构建、不 bump 版本。
- 文本读写 .NET 显式 UTF-8 API（GBK 陷阱）；被测进程启动一律计划任务通道（SmartScreen）。
- 测量同配置 A/B；K1/K2 以本机 Explorer 参照为基准（baseline §2）。
- 用户手动项（M-c/M-d/M-e）只备清单不虚构数据。

## 审查重点

1. **设置写去抖不得丢终值**：resize 快速结束+立即关窗场景，挂起写入必须 flush（窗口 Closing/失活路径）。
2. **K1 早退不得破坏拖拽区域正确性**：DPI 变化/演示模式/最大化（DidPresenterChange）仍需重算；仅位置变化必须跳过。
3. **C1 缓存失效**：选中项变化或文件 mtime 变化必须失效；不得缓存失败结果（NoPreview 不缓存）。
4. **token 下传不改公共行为语义**：被取消的预览不得写入 UI 状态。
5. **AGENTS.md 修改限环境注记**（3-5 行），不动其他内容；VS 实例 ID 改为"以 vswhere 为准"表述。

---

### 任务 0：延后项清偿（同域小提交，含终审 Minor-1）

**文件：** AdaptiveLayoutHelpers.cs（预取 per-item 容错+键 TrimEnd 保留根盘形态）、ShellViewModel.cs:2548 附近（预取守卫：IsAdaptiveLayoutEnabled 且非 SyncAcrossDirs）、baseline-batch1.md（50ms 分辨率注记、"两侧各 6 轮"措辞、M-c 目录生成命令）、charter（K7 未覆盖注记→顺延批次 4）、AGENTS.md（环境注记：GBK/Get-Content 禁用、MTP IPC 打包阻断与 TMP 重定向、VS 实例以 vswhere 为准）

- [ ] 逐条修复 8 项+Minor-1，单提交 "Address deferred review findings from batch 1"
- [ ] msbuild 验证；commit 前 status/diff --check

### 任务 1：C2a ApplyLayout 幂等化

**文件：** InfoPane.xaml.cs（ApplyLayout）、MainPage.xaml.cs（UpdatePositioning）

- [ ] 记录上次应用状态（layout+关键 GridLength），未变早退；InputSystemCursor.Create 两形态静态缓存；GoToState 仅状态真变时调用
- [ ] msbuild；冒烟：切布局各模式一次视觉正常；commit "Make InfoPane ApplyLayout idempotent"

### 任务 2：C2b resize 设置写去抖 + LoadPaneChanged 节流

**文件：** MainPage.xaml.cs（Page_SizeChanged:410-422、RootGrid_SizeChanged:445→LoadPaneChanged:506-529）

- [ ] VerticalSizePx/HorizontalSizePx 的每 tick += 改本地累计 + 300ms 稳定或 SizeChanged 结束信号（含窗口 Closing flush）一次写入
- [ ] LoadPaneChanged 加"相关值未变早退"；msbuild；commit "Debounce pane size settings writes during resize"

### 任务 3：K1 拖动区域早退（#19028 方案）

**文件：** MainPage.xaml.cs（AppWindow.Changed 处理：127-128 一带与 :353 lambda）

- [ ] 处理器仅 args.DidSizeChange || DidPresenterChange || DidVisibilityChange 时重算 SetTitleBarDragRegion；:353 lambda 改具名方法可退订（顺带消 M5 的不可退订订阅）
- [ ] msbuild；K1 复测（perf-harness Move 200 步 vs Explorer）；commit "Recompute drag regions only on size or state changes"

### 任务 4：C1 预览切换收敛

**文件：** InfoPaneViewModel.cs（双形态缓存槽：当前项 (fullPreview, basicPreview) 两槽，键= item+mtime，失效=选中项变化/mtime 变化/失败结果不缓存；GetBuiltInPreviewControlAsync 增 token 重载逐类型传递；防抖 100→200ms）

- [ ] 实现+全预览类型回归冒烟（图片/PDF/文本/媒体/Shell/基本）；msbuild；commit "Reuse cached preview content on tab switches"

### 任务 5：批次 2 测量与收尾

- [ ] K1/K2 前后 vs Explorer（harness 同参数）；场景 D（拖窗 10s 设置文件 mtime 写入计数 ≤2）；M-c/M-d 用户清单汇总
- [ ] 打包尝试（TMP 重定向）或如实报告阻断；KPI 表更新；收尾公示
