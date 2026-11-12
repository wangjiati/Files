# 性能修复批次 3 实现计划（v1，2026-11-12）

> **面向 AI 代理的工作者：** 必需子技能：subagent-driven-development 或 executing-plans。步骤复选框跟踪。

**目标：** 消除设置写入反馈回路（C6）、三处无界缓存（M3）、既有崩溃 S1（关窗竞态），并清偿批次 2 终审归入的防退化加固项。

**架构：** 四条独立战线：加固小提交 → S1 崩溃守卫 → C6 客户端侧设置写去抖 → M3 缓存上限。全部 UI/客户端层，不动 WinRT 服务器契约、manifest、CsWin32。

**规格：** 调查报告 §1 C6、§2 M3、§8；批次 2 终审甄别清单；S1 崩溃证据（.superpowers/sdd/2026-11-12-performance-fixes-batch2/crash-evidence-*，主仓库侧）。

**技术栈/工具：** 同前两批（WinPS 5.1 + DevShell 7ac2ee5b；测量通道 perf-b2-runner；打包 Build-Internal -PortableOnly 阻断则如实报告）。

## 全局约束（承批次 1/2）

- AGENTS.md：CRLF、-v:quiet -clp:ErrorsOnly、不并行构建、不 bump 版本；设置持久化为高风险区——C6 改动须逐退出路径验证终值不丢。
- WinRT 边界规则适用性自查：C6 只改客户端序列化/数据库层（Utils/Serialization），不改 Server 契约与激活。
- 文本 .NET 显式 UTF-8 API；被测进程启动走计划任务通道；测量同配置 A/B。

## 审查重点

1. **C6 终值不丢**：任何退出路径（关窗/异常/进程收尾）前挂起写必须 flush；读路径（GetValue）必须能看到挂起值（读穿）或等效语义——设置读旧值窗口 ≤ 去抖窗口。
2. **M3 上限不得破坏正确性**：撤销栈截 oldest 是标准语义（保留最近 N 条）；目录大小缓存 LRU 淘汰后重算即可；图标缓存清理不得引起可见闪烁回归（任务 4 冒烟核对）。
3. **S1 修复不得改变正常路径行为**：同意对话框在窗口存活时的行为与现在完全一致；守卫只拦"窗口已关"。
4. **加固项零行为变化**（纯防御/常量上移/幂等化）。

---

### 任务 0：防退化加固 + S1 证据补档

**文件：** MainPage.xaml.cs（:613 光标复用两静态字段；GoToState 仅 true 记录）、InfoPane.xaml.cs（GoToState 同款）、InfoPaneSettingsService.cs + MainPage.xaml.cs（clamp 常量上移服务公共常量）、MainPropertiesPage.xaml.cs:74（幂等 -=/+=）

- [ ] 五项加固单提交 "Add regression hardening from batch 2 review"；构建验证
- [ ] S1 前置 COM 事件（崩溃前 ~1s 两条 0x800710DD）从两侧 Data\logs 摘录归档至 sdd 目录 crash-evidence-pre-events.txt（不入库）

### 任务 1：S1 关窗竞态修复

**文件：** src/Files.App/Views/Shells/BaseShellPage.cs:871 一带（DisplayFilesystemConsentDialogAsync 延续）

- [ ] 延续处访问 MainWindow.Instance.Content.XamlRoot 前加窗口存活守卫（IsMainWindowClosed 已有语义可复用）；被关闭窗口路径直接放弃 UI 呈现（对话框结果已无意义）。核实同类延续点（同文件其他 EnqueueOrInvokeAsync 后读 Window 的位置）逐一守卫
- [ ] 构建验证；commit "Guard consent dialog continuation against closed window"；压测 B≥30 次（通道）核对零复现（对照批次 2 的 1/16）

### 任务 2：C6 设置写入客户端去抖

**文件：** src/Files.App/Utils/Serialization/Implementation/（ServerSettingsDatabase.cs 与 CachingJsonSettingsDatabase.cs 共同基类或各自）

- [ ] SetValue 变更挂起队列 + 300ms 尾随去抖 + 全退出路径 flush（复用批次 2 C2b 的 timer 模式与教训：打包前复制被测 exe；先查空再 stat 类比——读路径读穿挂起值）；MSIX RPC 与便携文件写两实现同语义
- [ ] 行为验证：连续 100 次 SetValue 不同键 → 落盘 ≤2 次；退出路径终值完整；GetValue 在挂起期间返回新值
- [ ] commit "Debounce settings persistence writes"

### 任务 3：M3 三缓存上限

**文件：** CachedSizeProvider.cs（容量上限 LRU，CleanAsync 实装真清理）、StorageHistoryWrapper.cs（条数上限，默认 100，超出截最旧）、IconCacheService.cs（LRU 或 DPI/布局尺寸变化时调用既有 Clear）

- [ ] 三处各自上限实装 + 边界（撤销栈截断后 index 语义正确；图标清理触发点不引入高频重解码）
- [ ] 构建验证；commit "Bound the size, history, and icon caches"

### 任务 4：批次 3 测量与收尾

- [ ] K5 长会话场景（开关 30 个标签/执行 50 次操作/翻滚大目录后空闲 RSS 斜率 vs 批次 2 构建）；设置写压测（滑块类连续改 100 次 → user_settings.json mtime 次数 ≤2）
- [ ] S1 压测复核（任务 1 后 30 次）；冒烟（含图标清理后视觉无闪烁）；打包尝试或如实报告
- [ ] charter KPI 更新（K5）；收尾公示
