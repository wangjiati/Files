# 全视图筛选 — 打包件 UI 验收报告

日期：2026-11-15
分支：`main`（commit `a7efa9594`）
打包件：`artifacts/internal/4.3.3.3/portable/Files.Internal_4.3.3.3_x64-portable.zip`
SHA-256：`D705B725F8267A6B0EB28EA0E6A8D7A6B13416AD5FB48748649A4491D32A1D35`
构建：`.github/scripts/Build-Internal.ps1 -PortableOnly -ReplaceExistingRelease`（单元测试 548/548 通过）

## 测试环境

| 虚拟机 | 系统 | 地址 | 角色 |
| --- | --- | --- | --- |
| Win10 | Windows 10 IoT 企业版 LTSC 19044 | `192.168.221.128` | 主验收 |
| Win11 | Windows 11 IoT 企业版 LTSC 26100 | `192.168.221.130` | 跨系统验收（重建的未加密配置） |

## 自动化流水线

流水线已独立为单独项目 `D:\projects\vm-ui-pipeline`（本仓库不再包含），本次验收使用的版本仍保留在其示例套件中。

- Runner：C# 控制台测试器（UI Automation，无 PowerShell、无截屏）。
- 编排：宿主机解压打包件、`robocopy /MT` 推送、WinSimpleSSH 服务模式在 VM 桌面会话启动测试器、回传结果。
- 测试夹具 `C:\FilterTest`：2 个根级 zip、2 个文本、`sub\c.zip`、`sub\deep\d.zip`，用于确定性断言。

复现（在独立项目中）：

```powershell
$env:FILES_VM_PASSWORD = '<vm password>'
powershell -File run-pipeline.ps1 `
  -ArtifactZip <path>\Files.Internal_4.3.3.3_x64-portable.zip
```

## 结果：两台 VM 均 8/8 通过

| 用例 | 断言 | Win10 | Win11 |
| --- | --- | --- | --- |
| `launch` | 打包应用启动并暴露主窗口 | ✅ | ✅ |
| `filter-bar` | 模式切换与名称框存在 | ✅ | ✅ |
| `labels-zh` | 新文案本地化（常规/高级/搜索子文件夹） | ✅ | ✅ |
| `basic-wildcard` | 常规 `*.zip` 命中 2 个根级 zip | ✅ | ✅ |
| `advanced-editor` | 高级模式暴露名称条件编辑器 | ✅ | ✅ |
| `recursion-location` | 搜索子文件夹（2 层）命中 4 个 zip，位置列出现 | ✅ | ✅ |
| `clear-all` | 清除筛选恢复完整列表 | ✅ | ✅ |
| `column-header-flyout` | 列头漏斗打开弹窗，点“应用”后条件进入筛选栏条件列表 | ✅ | ✅ |

原始结果：`artifacts/ui-pipeline/results/20261115-202549/`（`summary.json`、`run.log`、`pipeline-summary.json`）。

## 说明

- 打包件与测试夹具均为临时目录，不随仓库提交；测试结果目录同样不提交。
- Win11 原始 VM 为 VMware 加密且密码未知，验收使用重建的未加密配置（`Win11-Rebuilt.vmx`，新 IP `.130`，新快照 `initial`）。
- 本次验收覆盖筛选栏的功能与本地化；未覆盖：地址栏往返（Join/Length/Scope/条件级 Regex，已知 P2）、选择类取反（P2）、递归进度 UI。
## 界面结构验收（对照高保真原型）

- 高级模式的条件为**单行内联控件组**：`[名称][Aa][.*][.ext][运算符▾][输入框][添加]`，与原型一致；不再使用竖排表单块。
- 筛选栏为两行：第一行模式切换 + 名称条件 + 添加；第二行搜索子文件夹、深度、计数、清除所有筛选，窄窗口不再裁切。
- 类型/扩展名/标签使用紧凑「选择…」浮层；大小/日期条件同样单行内联。
- 列头弹窗按类型统一设计：文本（文本/正则页签 + 运算符 + 输入 + 共用的“区分大小写/包含扩展名”）、时间（预设 + 从/至）、枚举（值列表 + 全选/清空 + 包含空值）；单行紧凑编辑器只用于顶部筛选栏。
- 允许的 WinUI 控件差异：`.*`/`.ext` 以 ToggleButton 呈现、作用域用紧凑下拉替代 `.ext` 图标开关、添加按钮文案为“添加”。
