# 全视图筛选 — 打包件 UI 验收报告

日期：2026-11-15
分支：`filter-bar-implementation`（commit `4388f87b4`）
打包件：`artifacts/internal/4.3.3.3/portable/Files.Internal_4.3.3.3_x64-portable.zip`
SHA-256：`7A2562917E8D7DF674D53A8F982BEC6F42B30EBE713CEE8EEB2CA688EF69FE67`
构建：`.github/scripts/Build-Internal.ps1 -PortableOnly -ReplaceExistingRelease`（单元测试 548/548 通过）

## 测试环境

| 虚拟机 | 系统 | 地址 | 角色 |
| --- | --- | --- | --- |
| Win10 | Windows 10 IoT 企业版 LTSC 19044 | `192.168.221.128` | 主验收 |
| Win11 | Windows 11 IoT 企业版 LTSC 26100 | `192.168.221.130` | 跨系统验收（重建的未加密配置） |

## 自动化流水线

`tools/ui-pipeline/`：

- `FilesFilterUiTests`：C# 控制台测试器（UI Automation，无 PowerShell、无截屏）。
- `run-pipeline.ps1`：宿主机编排器——本机解压打包件、`robocopy /MT` 推送、WinSimpleSSH 服务模式在 VM 桌面会话启动测试器、回传结果。
- 测试夹具 `C:\FilterTest`：2 个根级 zip、2 个文本、`sub\c.zip`、`sub\deep\d.zip`，用于确定性断言。

复现：

```powershell
$env:FILES_VM_PASSWORD = '<vm password>'
powershell -File tools/ui-pipeline/run-pipeline.ps1 `
  -ArtifactZip artifacts/internal/4.3.3.3/portable/Files.Internal_4.3.3.3_x64-portable.zip
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
| `column-header-append` | 列头漏斗追加条件并切到高级 | ✅ | ✅ |

原始结果：`artifacts/ui-pipeline/results/20261115-202549/`（`summary.json`、`run.log`、`pipeline-summary.json`）。

## 说明

- 打包件与测试夹具均为临时目录，不随仓库提交；测试结果目录同样不提交。
- Win11 原始 VM 为 VMware 加密且密码未知，验收使用重建的未加密配置（`Win11-Rebuilt.vmx`，新 IP `.130`，新快照 `initial`）。
- 本次验收覆盖筛选栏的功能与本地化；未覆盖：地址栏往返（Join/Length/Scope/条件级 Regex，已知 P2）、选择类取反（P2）、递归进度 UI。
## 界面结构验收（对照高保真原型）

- 高级模式的条件为**单行内联控件组**：`[名称][Aa][.*][.ext][运算符▾][输入框][添加]`，与原型一致；不再使用竖排表单块。
- 筛选栏为两行：第一行模式切换 + 名称条件 + 添加；第二行搜索子文件夹、深度、计数、清除所有筛选，窄窗口不再裁切。
- 类型/扩展名/标签使用紧凑「选择…」浮层；大小/日期条件同样单行内联。
- 允许的 WinUI 控件差异：`.*`/`.ext` 以 ToggleButton 呈现、作用域用紧凑下拉替代 `.ext` 图标开关、添加按钮文案为“添加”。
