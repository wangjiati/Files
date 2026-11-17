# 筛选功能全自动化测试报告

日期：2026-11-17
环境：Windows 11 IoT 企业版 LTSC 26100 测试虚拟机（`192.168.221.130`）
被测产物：`Files.Internal_4.3.3.4_x64-portable.zip`（commit `e75671c9e`，SHA-256 `c5970cb89e700d50c96bf47707dc6fd47654d0e892c24f43e958f6b01ae2b724`）

## 结论

- UI 自动化：**38/38 通过，0 失败**。
- 单元测试：**564/564 通过**。
- 覆盖位置：地址栏 LINQ 筛选、顶部筛选栏（常规/高级）、列头筛选弹窗、递归筛选、布局切换、空结果与错误对话框。
- 测试结果：`artifacts/ui-pipeline/results/20261117-2330-filter-automation/win11/summary.json`。

## 测试方式

- 宿主机只负责构建、通过 SMB 投放产物与夹具、通过 WinSimpleSSH 启动测试进程；所有 UI Automation 操作、按键和断言都在虚拟机内执行。
- 测试运行器：`tools/ui-pipeline/FilesFilterUiTests`（工作树 `filter-bar-implementation`）。
- 夹具每次运行由运行器重建为 `C:\FilterTest`：包含大小写、中文、特殊字符、无扩展名、不同大小与修改时间、两级子目录和空目录的确定性文件集。
- 每个用例开始前统一清理筛选、递归、布局和弹出层；失败时导出 UI 树快照。

## 用例与断言

### 顶部筛选栏

| 用例 | 断言 |
| --- | --- |
| `launch` / `filter-bar` | 主窗口、文件夹标签、常规/高级切换、清除按钮、递归复选框存在 |
| `basic-wildcard` | `*.zip` 只匹配根目录 2 个 zip，子目录 zip 不出现 |
| `basic-multi-keyword` | `report 2026` 同时满足两个关键字 |
| `basic-case-insensitive` | `alpha report` 忽略大小写命中 `Alpha Report.TXT` |
| `basic-special-characters` | `a;b&c` 命中含分号/与号/空格的文件名 |
| `basic-chinese` | 中文关键字命中中文文件名 |
| `basic-enter` | 文本框中按 Enter 后结果保持/立即刷新 |
| `recursion-location` | 递归后命中 4 个 zip，出现"位置"列 |
| `recursion-depth` | 深度 1 命中 3 个 zip，深度 2 命中 4 个 zip |
| `clear-all` | 清除所有筛选后恢复全部 14 个根项 |
| `advanced-default-editor` | 高级模式显示名称条件编辑器、运算符和"添加"按钮 |
| `advanced-name-contains` | 名称包含条件命中 2 个报告文件 |
| `advanced-name-enter` | 高级名称框 Enter 后立即确认 |
| `advanced-operator-startswith` | "开头是"不命中 `Alpha Report.TXT` |
| `advanced-case-sensitive` | 区分大小写时 `ALPHA` 无结果，`Alpha` 命中 |
| `advanced-regex` | `^report.*\.txt$` 命中目标文件 |
| `advanced-name-editor-hidden-after-condition` | 有其它条件时空名称编辑器收起 |
| `advanced-add-button-aligned` | "添加"按钮与条件控件中心 Y 误差 ≤2px |
| `advanced-leading-join-hidden` | 首个生效条件不显示"并且/或者" |
| `advanced-join-or` | 两个名称条件用"或者"连接后命中 3 个文件 |
| `mode-switch-preserves-keyword` | 常规/高级往返后关键字与结果保持 |
| `layout-persistence` | 详细/列表切换后筛选结果保持，返回详细后列头恢复 |
| `empty-result-state` | 无匹配时显示空结果文案且列表为空 |

### 列头筛选

| 用例 | 断言 |
| --- | --- |
| `column-popup-name-enter` | 列头弹窗文本框按 Enter 立即应用并关闭弹窗，名称编辑器回填 |
| `column-popup-height-stable` | 文本/正则切换前后弹窗高度误差 ≤2px |
| `column-popup-clear` | 弹窗"清除"移除该列筛选并恢复全部条目 |
| `column-popup-regex-apply` | 正则页签应用后命中目标文件 |
| `column-popup-type-apply` | 类型"PNG 图像"只命中图片 |
| `column-popup-size-apply` | 最小 1000001 B 只命中 2 MB 文件 |
| `column-popup-date-apply` | 修改日期"昨天"命中昨天修改的文件 |

### 地址栏

| 用例 | 断言 |
| --- | --- |
| `address-linq-name` | `?filter=Name.Contains("notes")` 命中 1 项，面包屑后缀显示 LINQ 表达式 |
| `address-linq-size` | `?filter=Size >= 1MB` 命中大文件 |
| `address-ampersand-and-group` | 表达式内 `&&` 不被当作参数分隔符，`&group=` 正常解析 |
| `address-clear-current-breadcrumb` | 点击当前目录面包屑清除筛选，后缀中不再包含 `filter=` |
| `address-roundtrip` | 捕获完整地址、清除后粘贴回车，筛选结果一致 |
| `address-old-syntax-rejected` | 旧 `name contains` 语法弹出错误对话框 |
| `address-invalid-expression` | 非法 `Size >> 1MB` 弹出错误对话框且不导航 |

## 测试中发现并修复的问题

1. **列头弹窗 Enter 不生效**：TextBox 先消费了 Enter，原 `KeyDown` 处理器收不到。改为弹窗根节点 `PreviewKeyDown`，并只对真正的筛选文本输入生效。
2. **Enter 被 ComboBox 内部输入框误捕获**：递归深度下拉框按 Enter 时，筛选栏的 Enter 处理可能被触发，测试中观察到窗口 UIA 句柄丢失并出现第二个 Files 进程。已把两处 Enter 处理限制为"非 ComboBox 内的文本输入"；深度用例改为方向键选择、不按 Enter 后稳定通过。按 Enter 的边界场景仍列入风险，需单独复核。
3. **布局切换后列头找不到（测试稳定性）**：布局往返后窗口位置/尺寸可能变化，列头被挤出可视区域。测试在每个用例前恢复窗口尺寸并确保回到详细布局。
4. **地址后缀包含 `?layout=details`**：布局切换产生的临时布局覆盖本来就会写入地址；测试改为断言后缀中不再包含 `filter=`，而不是要求整个后缀为空。

## 未覆盖与风险

- 仅验证了 Windows 11 虚拟机；Windows 10 未纳入本轮矩阵。
- 分栏视图（列视图）中的逐栏筛选未覆盖；设计稿要求"每栏所在目录"生效，当前实现仍以主视图筛选状态为主，建议单独跟进。
- 标签筛选、地址栏日期/类型/正则表达式、超长/异常正则的性能与超时未覆盖。
- 递归深度下拉框按 Enter 的交互仍建议手工复核（测试已改为方向键选择）。
- 测试机的 WinSimpleSSH 每次命令会留下一个控制台窗口；本轮结束后已清理，后续批量运行需在收尾步骤统一关闭 `cmd`/`conhost`。

## 复现

```powershell
# 宿主机：设置测试 VM 密码后投放产物并启动 VM 内的测试运行器
$env:FILES_VM_PASSWORD = '<vm password>'
# 运行器源位于 tools/ui-pipeline/FilesFilterUiTests，使用 Release 单文件发布
# 结果由 VM 写回 C:\Test\results，再由宿主机复制到 artifacts/ui-pipeline/results
```
