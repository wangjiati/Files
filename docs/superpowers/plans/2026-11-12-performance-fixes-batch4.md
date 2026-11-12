# 性能修复批次 4 实现计划（v1，2026-11-12）——最终批次，两波结构

> **面向 AI 代理的工作者：** 必需子技能：subagent-driven-development 或 executing-plans。步骤复选框跟踪。

**目标：** 收官战役——4a 波：S3 三向处置 + 手势流/订阅/守卫清扫 + 测试基建 + K5 三轮均值；4b 波：C3 选择 O(N²) 增量化、C4 集合增量更新、M5 窗口树滞留、K2 长尾。完成即达成章程全部可达成 KPI。

**架构：** 4a 稳定性优先（S3 复现率 30% 是当前最大已知质量问题）；4b 三项性能原件各自独立成任务；终测收官含上游报告材料汇总。

**规格：** 调查报告 §1 C3/C4、§2 M5、§8；批次 3 终审甄别清单与 S3 取证（s3-forensics-report.md）；charter KPI 表。

**技术栈/工具：** 同前三批（WinPS 5.1 + DevShell 7ac2ee5b；perf-b2-runner 通道含 SendInput 扩展；构建带 -p:Distribution=Portable；冻结+SHA256 铁律）。

## 全局约束（承前三批）

- AGENTS.md 全部规范；CRLF；.NET UTF-8；不并行构建；不 bump 版本。
- 被测 exe 冻结+哈希铁律；压测构建必带 Portable；测量同配置 A/B。
- 用户手动项只备清单；预期未达如实记录归因。

## 审查重点

1. **C3 行为语义**：选择集的最终内容与顺序在增量化后与现状逐字节等价（SelectionChanged 事件序列可不同但最终 SelectedItems 一致）；框选拖动中的中间态允许差异（现状本就有）。
2. **C4 不变量**：分组/排序语义在增量路径下与 Reset 路径等价（OrderGroups/分组归属的增量维护正确性是最大坑）；监视批次与枚举批次的交错（同目录边拷贝边枚举）不产生丢项/重项。
3. **M5 兼容性**：MainWindow.Instance 置 null/退订改造不得破坏单实例假设下的正常重建路径（重启窗口流）。
4. **S3 处置决策树**：任务 0 的版本比对结论决定任务 1 形态（升级验证 / 减载改造 / 仅上游提单）——不预先锁定。
5. **清扫零行为变化**（卫生项纪律）。

---

## 4a 波：稳定性与卫生

### 任务 0：S3 第一向——WinAppSDK 版本比对调查（只读调研）

- [ ] 查当前 WinAppSDK 版本（csproj/运行时 CoreMessagingXP 12.6.5.0 对应包版本）；检索上游 microsoft/WindowsAppSDK 仓库关窗 fail-fast/DispatcherQueue 已知 issue（0xc000027b/0xadf5e 不可搜则按 RoFailFast+关窗场景）；评估升级到最新 1.7/1.8 的变更日志覆盖度
- [ ] 产出：结论三选一（升级可验证 / 需减载改造 / 上游未知问题）+ 证据；无代码改动

### 任务 1：S3 第二/三向（形态由任务 0 定）

- [ ] 若升级可验证：升级包版本→30+ 次压测复现率对比→失败则回退并转减载
- [ ] 减载改造（若需）：关窗序列减少 posted work 的最小改造（候选：CloseAllTabs/WaitForCompletion 的调度策略）
- [ ] 上游提单材料：dump+取证+复现步骤打包成 issue 草稿（是否提交由用户定）
- [ ] 压测 30+ 次复现率对比（基线 30%）

### 任务 2：清扫提交（§2.F 20 处手势流 + 杂项卫生）

- [ ] §2.F 清单逐处：统一"TryShowAsync 容错或窗口存活判定"模式（任务 1 报告 §2.F 的账本为准）
- [ ] MainPropertiesPage 两订阅幂等；MainPage Math.Max→常量；RemoveHistory 仅命中才 --index（+测试）；C6 Import 竞态 + Import/Export flush 返回值处理
- [ ] 全部零行为变化（除 RemoveHistory 守卫的可论证边界）；单测全绿

### 任务 3：测试基建（C6 MSIX 宿主 + 生命周期 + 断言加固）

- [ ] MSIX 口径的 SettingsDebounce 测试宿主（注入接口替代静态 Server）或等效覆盖论证；SettingsFlushDebouncer 注册表/Timer 可清理（测试模式）；墙钟断言改注入时钟或放宽
- [ ] 单测全绿

### 任务 4：4a 测量（S3 复现率 + K5 三轮）

- [ ] S3 复现率复测（任务 1 后 vs 30% 基线）；K5 三轮均值（每步 WS 落盘+大目录翻滚并入会话尾段）；压测+冻结铁律
- [ ] baseline §7 + charter 更新

## 4b 波：性能原件

### 任务 5：C3 选择路径增量化

**文件：** RectangleSelection_ListViewBase.cs:66-108（每 move 多趟 O(S) 物化→差集缓存比较）、ExtendPreviousItemSelectionStrategy.cs:22-40（逐项 Add→批量）、DetailsLayoutPage.xaml.cs:468-475/1349-1367（Ctrl+A 逐项→批量 SetSelectedItemsOnNavigation 复用）、ItemManipulationModel.cs:61-92（逐项 Add→范围批量）

- [ ] 框选：pointer move 路径只对变化项调策略、事件末梢单次物化；Ctrl+A：单次批量；恢复：批量赋值
- [ ] 5000 项目录选择操作 A/B 计时（通道+SendInput 全选/框选）；最终选择集等价性断言（测试）

### 任务 6：C4 集合增量更新

**文件：** ShellViewModel.cs:1446-1474（ApplyFilesAndFoldersChangesAsync 增量分支：diff 出 add/remove 应用而非 Clear+AddRange+Reset）、:2095-2114（本地化名重排仅 DisplayName 真变触发）

- [ ] 监视批次(200ms)/枚举中间批次(500ms)→增量 diff 应用；分组归属维护（GroupableBulkConcurrentObservableCollection 既有增量 API 调研先行）
- [ ] 拷贝进行中目录的列表稳定性 A/B（视觉/日志：Reset 次数 5/s→0）

### 任务 7：M5 窗口树滞留

**文件：** MainPage.xaml.cs:64-71/137（构造订阅→Unloaded 退订或弱事件）、MainWindow.xaml.cs:23-24（_Instance 置 null 时机）、WindowContext.cs:34-36（退订）

- [ ] 条件性泄漏路径闭合；窗口重建（关标签后重开）回归验证
- [ ] 内存验证：多轮重建 MainPage/关开窗口后 RSS 不爬升

### 任务 8：K2 长尾归因与修复

- [ ] 321ms 尖峰复现与归因（ETW 或 bisect：候选=布局塌缩/GC/字体缓存）；修复或如实归档不可修因素
- [ ] K2 复测（p95/p50/max 三档 vs Explorer 参照）

### 任务 9：战役收官

- [ ] 全 KPI 终测（K1-K7 可测项）；打包（或换机补打指引）；上游报告材料汇总（M1 证伪方法/S1/S2/S3/独有发现）；战役总结报告（docs/research/）
- [ ] 收尾公示：全部裁决清单、四批对照、剩余已知项
