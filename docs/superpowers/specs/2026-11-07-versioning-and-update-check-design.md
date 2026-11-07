# Files 内部分支：子版本号与更新检查（精简设计）

日期：2026-11-07
状态：待批准
本分支仓库：`wangjiati/Files`

## 1. 意图

当前版本固定为 `4.3.1.0`，与上游同号，无法区分"上游 4.3.1"和"我们基于 4.3.1 的第几次内部发布"；用户也不知道是否有新版。

本次只做两件小事：

1. 让四段版本号承载上游版本和内部迭代号。
2. 启动时每日检查一次 GitHub Release，发现新版时提示，用户可跳转下载或跳过。

## 2. 设计原则

- **使用为主**：单维护者、个位数用户，先能稳定用起来。
- **能复用就不新增**：启动钩子、更新服务接口、工具栏入口、设置存储、About 卡片、对话框写法都已经存在。
- **只保留一条主路径**：发现新版 → 打开 GitHub Release 页面 → 用户手动下载安装。
- **不引入第二条事实源**：版本号仍写在现有两个文件里，不新增版本管理工具链。

## 3. 目标、非目标与反目标

### 3.1 目标

- 四段版本号同时表达上游版本和内部迭代号。
- 设置页同时显示两者。
- 关于页提供项目主页入口：`https://wangjiati.github.io/Files`。
- 启动时每日最多检查一次；发现新版弹窗展示更新内容。
- 用户可选择"下载更新"或"以后再说"；跳过该版本后不再主动提醒。
- 设置中可关闭检查，也可手动检查。
- 不引入服务器、遥测或自动安装。

### 3.2 非目标

- 不实现自动下载、静默安装、差量更新。
- 不做用户统计、遥测、设备指纹。
- 不做强制更新、灰度、多通道。
- 不接入 Store 或 Files 官方更新源。
- 不做 Gitee 镜像和双源切换。

### 3.3 反目标（技术上可行也算失败）

- 启动被网络请求阻塞，或离线时弹错误。
- 同一版本每天重复提醒。
- 为了统计用户加入任何客户端遥测。
- 引入版本管理脚本、Markdown 渲染控件或第三方更新 SDK。
- 破坏便携版的自包含与可迁移特性。

## 4. 已确认的产品决定

- 更新源先用 GitHub。
- 支持渠道是飞书群；仓库 Issues/Discussions 当前关闭。
- 每日只检查一次；启动时检查。
- 发现更新弹窗展示内容，用户可选"下载更新"或"以后再说"。
- "以后再说"表示这个版本不再提醒，直到出现更新版本；保留手动检查入口。
- 设置中可关闭检查更新。
- 内部迭代号在上游升级后从 1 重新计数。

## 5. 版本号

### 5.1 格式

```text
<upstreamMajor>.<upstreamMinor>.<upstreamBuild>.<forkIteration>
```

| 含义 | 版本 |
| --- | --- |
| 基于上游 4.3.1 的第 7 次内部发布 | `4.3.1.7` |
| 上游升级到 4.4.0 后首次内部发布 | `4.4.0.1` |

- 前三段等于同步到的上游版本。
- 第四段是内部迭代号，每次内部发布 +1；同步上游后重置为 1。
- 四段都必须 ≤ 65535，主版本不能为 0（MSIX 约束）。
- 第一个采用新方案的版本是 `4.3.1.1`（当前已发布的是 `4.3.1.0`）。

### 5.2 只改现有的两个文件

版本号只写在现有位置，不新增 `Version.props`、不新增版本脚本：

- `src/Files.App/Files.App.csproj` 的 `Version`、`AssemblyVersion`、`FileVersion`、`InformationalVersion`。
- `src/Files.App/Package.appxmanifest` 的 `Identity Version`。

发布时手改这两处。`Build-Internal.ps1` 已经校验清单版本与便携 exe 版本一致；如果它没有比对 csproj，补一条几行的比对即可，不扩展成版本管理工具。

`InformationalVersion` 保持四段数字，不追加 `+fork.7`：四段版本已经能区分，加后缀会牵涉 SDK 的源码提交号追加行为，收益很低。

### 5.3 显示

复用 `AboutViewModel.Version`，只改字符串：

```text
版本 4.3.1（内部迭代 7）
基于上游 4.3.1
```

- 上游版本就是版本号前三段，不额外持久化。
- 构建提交和上游提交继续留在 `RELEASE-INFO.txt` 和 Release 页面，不进入运行时 UI。
- 更新弹窗和 Release 标题使用完整四段号 `4.3.1.7`。

### 5.4 运行时读取

- MSIX：沿用包版本。
- 便携版：沿用程序集版本。
- `AppRuntime.Current.Version` 保持不变；上游版本和迭代号从四段号拆解。

## 6. 更新检查

### 6.1 复用的现有链路

- `AppLifecycleHelper.InitializeAppComponentsAsync()` 已经在后台调用 `CheckAppUpdate()`，启动钩子不用另建。
- `AppLifecycleHelper.CheckAppUpdate()` 已经调用 `IUpdateService`，并且已经有"等启动标签页稳定后再回 UI 线程"的写法。
- `NavigationToolbarViewModel.IsUpdateAvailable` 已经绑定工具栏更新图标。
- `BaseShellPage` 已经把工具栏更新按钮接到 `IUpdateService.DownloadUpdatesAsync()`。
- `AboutViewModel` 已经解析 `IUpdateService`。

所以只需要替换 `IUpdateService` 的实现，不新增生命周期、工具栏或导航代码。

### 6.2 新实现

新增 `GitHubUpdateService : IUpdateService`，替换 `AppLifecycleHelper` 里 `ManualUpdateService` 的单例注册。

`IUpdateService` 只增加最小成员：

```csharp
Version? AvailableVersion { get; }
string? AvailableReleaseNotes { get; }
Uri? ReleasePageUrl { get; }

Task CheckForUpdatesAsync(bool force = false);
```

- `IsUpdateAvailable`、`IsUpdating`、`UpdateProgress` 沿用现有语义。
- `AreReleaseNotesAvailable` 保持 `false`，避免触发上游发布说明页。
- `DownloadUpdatesAsync()` 打开 `ReleasePageUrl`，复用现有打开链接的方式。
- `DownloadMandatoryUpdatesAsync()`、`CheckForUpdatesAsync()` 的空实现语义保留。

检查流程：

1. 总开关关闭 → 不检查。
2. 非强制且今天已检查 → 跳过。
3. GET `https://api.github.com/repos/wangjiati/Files/releases/latest`，5 秒超时，不带认证。
4. 失败（网络、403、429、5xx、非法 JSON）→ 静默结束，记录当天已尝试。
5. 从 `tag_name` 提取 `v?(\d+(?:\.\d+){2,3})`，不足四段补 0；解析失败视为无更新。
6. 与 `AppRuntime.Current.Version` 做 `System.Version` 比较。
7. 新版本等于 `SkippedVersion` → 不弹窗，但保留工具栏入口。
8. 否则标记 `IsUpdateAvailable = true`，保存版本、`body`、`html_url`。

### 6.3 本版明确不做

- **不选资产**：不匹配 `.msix` / `-portable.zip`、不比较架构。用户从 Release 页面自己选。
- **不校验 digest**：不解析 `assets[].digest`；Release 页面已有 `SHA256SUMS.txt`。
- **不做 ETag**：个位数用户远低于匿名配额，省掉一个持久化字段和一套条件请求分支。
- **不解析 Markdown**：说明按纯文本展示。
- **不自动下载或安装**。

## 7. 界面

### 7.1 启动更新弹窗

新增一个简单 `ContentDialog`，沿用 `src/Files.App/Dialogs` 下现有写法：

- 标题：`发现新版本 4.3.1.7`
- 副标题：`基于上游 4.3.1 · 内部迭代 7`
- 正文：`body` 的前约 1500 字符，放在只读、可滚动的文本区域；不做 Markdown 渲染。
- 主要按钮：`下载更新` → 打开 `html_url`。
- 次要按钮：`以后再说` → 记录 `SkippedVersion` 并关闭。
- 额外链接：`查看完整说明` → 同样打开 `html_url`。

显示时机：在 `CheckAppUpdate()` 中，等 `MainPageViewModel.StartupTabsLoadedTask` 完成后回 UI 线程显示，复用现有模式。关闭弹窗不算跳过；只有"以后再说"才记录跳过。

### 7.2 设置 → 关于

- 复用现有版本卡片，只改描述字符串。
- 新增一个 `SettingsCard`：
  - `ToggleSwitch`：`启动时检查更新`，默认开启。
  - `Button`：`立即检查`，调用 `CheckForUpdatesAsync(force: true)`。
  - 一行状态文本：`已是最新版本` / `发现新版本 4.3.1.7` / `检查失败，稍后再试` / `已跳过 4.3.1.7`。
- 总开关关闭时禁用"立即检查"并说明，此时不发起任何请求。
- 新增"主页"设置卡片，打开 `https://wangjiati.github.io/Files`。
- 顺带修正明显指向上游的链接：`Constants.ExternalUrl.GitHubRepoUrl` 改为 `https://github.com/wangjiati/Files`，`DocumentationUrl` 改为项目主页。社区和赞助链接本次不动。

### 7.3 工具栏

不需要改 XAML：`IsUpdateAvailable` 已有绑定，`DownloadUpdatesAsync` 已接好。服务返回新版本时图标自然出现。

## 8. 持久化

全部复用现有存储：

- `IApplicationSettingsService`：`CheckForUpdates`（默认 `true`）、`SkippedVersion`（默认空）。
- `ILocalStateStore`：`LastUpdateCheckDate`，存本地日期字符串 `yyyy-MM-dd`。
- 不新增其他状态、不引入云同步。

## 9. 失败与隐私

- 超时 5 秒，不重试。
- 所有失败静默，只在调试日志记录原因。
- 只访问 `api.github.com`，不携带用户标识、机器名或路径；`User-Agent` 仅含产品名和版本号。
- 打开链接前校验协议为 `https`、主机为 `github.com`。
- 不自动执行下载内容。

## 10. 发布

本版不新增自动发布脚本。Release 由维护者手工创建（可用 `gh release create`），`Build-Internal.ps1` 继续负责校验和产物。

- 标签统一为 `v<四段版本>`，例如 `v4.3.1.1`。
- 标题：`Files Internal 4.3.1.1`。
- 正文保留现有小节：版本、平台、上游基线、构建提交、本版变化、包内容、安装、校验。
- `## 本版变化` 由维护者手工填写，是弹窗内容的来源。
- 资产沿用现有四项：MSIX、便携 ZIP、`SHA256SUMS.txt`、`RELEASE-INFO.txt`。
- 预发布构建标记 `prerelease`，避免被 `releases/latest` 选中。
- 发布前改好版本号并提交，再打包。
- `Constants.ReleaseNotesUrl` 本次不动：当前注册的 `ManualUpdateService` 使相关入口不生效；替换服务后继续保持 `AreReleaseNotesAvailable = false` 即可。

## 11. 测试

只测核心逻辑，不测已删除的分支：

- 版本解析：`v4.3.1.7`、`4.3.1`、`v4.3.1.7-portable`、非法标签。
- 版本比较：`4.3.1.7 < 4.4.0.1`；相等不算更新。
- 每日限制：同一天第二次跳过；跨天恢复。
- 跳过逻辑：跳过当前版本不弹窗；出现更高版本恢复。
- 总开关：关闭后不发起请求。
- 失败处理：超时、403、429、5xx、非法 JSON 均静默结束。

手工验证：

- MSIX 与便携版版本显示一致。
- Release 构建自动检查，Debug 构建不自动检查但可手动检查。
- 断网启动不卡顿、不报错，当天不重复请求。
- 关闭开关后无请求。
- "以后再说"后同日和次日都不弹同一版本。
- 下载按钮打开正确的 Release 页面。

构建验证：

- `msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64`
- 按项目要求运行 `.github/scripts/Build-Internal.ps1` 并做打包版 UI 冒烟。

## 12. 复用清单

| 需求 | 复用的现有实现 |
| --- | --- |
| 启动时触发 | `AppLifecycleHelper.InitializeAppComponentsAsync()` → `CheckAppUpdate()` |
| 更新状态与下载动作 | `IUpdateService` |
| 工具栏更新入口 | `NavigationToolbarViewModel.IsUpdateAvailable`、`BaseShellPage` |
| 版本显示 | `AboutViewModel.Version` |
| 用户设置 | `IApplicationSettingsService` |
| 本机状态 | `ILocalStateStore` |
| 对话框写法 | `src/Files.App/Dialogs/*.xaml` 的 `ContentDialog` 模式 |
| 打开链接 | 现有 `Launcher` / URL 打开写法 |
| 外部链接常量 | `Constants.ExternalUrl` |
| 发布产物与校验 | `.github/scripts/Build-Internal.ps1` 现有输出 |
| 版本一致性校验 | `Build-Internal.ps1` 现有清单/便携 exe 比对 |

## 13. 建造者可自行决定

- 更新服务和设置属性的具体命名。
- 弹窗、设置卡片的 XAML 布局和措辞。
- 说明正文的截断长度。
- 是否保留 `ManualUpdateService` 作为无网络环境的空实现。
- 是否在 `Build-Internal.ps1` 里补 csproj 版本比对。

## 14. 未决问题

- 无。若 GitHub 匿名配额或国内可达性真的影响体验，再单独评估备用源。
