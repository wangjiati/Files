# 安装版与绿色便携版实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在不复制核心 UI 和业务代码的前提下，交付完整 MSIX 安装版与数据随目录迁移的 x64 绿色便携版。

**架构：** 使用 `Distribution=Msix|Portable` 控制构建，在启动最前端确定应用环境和数据目录；通过现有 DI 替换系统集成和 Server 客户端。便携核心 Server 与 MSIX Server 共享进程内系统数据提供器，不进行隐藏 COM 注册。

**技术栈：** .NET 10、WinUI 3、Windows App SDK、MSIX、现有 DI、System.Text.Json、MSTest。

**规格：** `docs/superpowers/specs/2026-10-24-files-internal-edition-design.md`

## 全局约束

- 首版只承诺 Windows x64；便携版是完整目录，不是单文件。
- Portable 不依赖预装运行时、管理员权限、MSIX/COM 注册、HKCU 或系统用户目录中的必要数据。
- 内部 MSIX 默认不接管独占系统集成，不访问官方更新源，采用内部证书和手动更新。
- 不修改无关上游代码；新增用户可见文本进入 `src/Files.App/Strings/en-US/Resources.resw`。
- 所有新增文本文件使用 CRLF；构建使用 x64 和 `-v:quiet -clp:ErrorsOnly`，构建命令不得并行执行。

## 审查重点（Review Focus）

- 第二个进程写同一 `Data`：应明确拒绝，不能破坏配置；由任务 3 的锁测试覆盖。
- `Data` 不可写：启动前明确失败且不能回退用户目录；由任务 2/3 的写探针测试和任务 8 冒烟覆盖。
- 路径含空格/中文、目录移动或盘符变化：必要数据仍可用；由任务 2/4 测试及任务 8 冒烟覆盖。
- 包 API 在运行环境初始化前执行：Portable 启动不能因此崩溃；由任务 2/5 的启动顺序检查与任务 8 干净系统验证覆盖。
- 外部程序在本产品关闭集成后又修改系统值：本产品不得覆盖外部修改；由任务 7 的条件恢复测试覆盖。

## 文件结构

- `src/Files.App/Files.App.csproj`：分发属性、Server 构建条件和发布内容。
- `src/Files.App/Properties/PublishProfiles/win-x64-portable.pubxml`：完整目录便携发布。
- `src/Files.App/Services/App/`：运行环境、便携锁、本地状态和手动更新服务。
- `src/Files.App/Program.cs`、`App.xaml.cs`、`Helpers/Application/AppLifecycleHelper.cs`：启动顺序和 DI。
- `src/Files.App/Services/Settings/`、`Utils/Serialization/`：设置路径与原子写入。
- `src/Files.App/Utils/FileTags/`、`Helpers/Layout/`：必要注册表数据的便携实现。
- `src/Files.App.Server.Core/`：MSIX Server 与 Portable 共享的核心系统数据逻辑。
- `tests/Files.App.UnitTests/`：不启动 WinUI 的纯逻辑测试。
- `.github/scripts/Build-Internal.ps1`：内部 MSIX、ZIP 和校验清单。

---

### 任务 1：分发构建和测试基线

**文件：** 修改 `Files.App.csproj`、`Files.slnx`、`Directory.Packages.props`；创建便携 pubxml、`tests/Files.App.UnitTests/Files.App.UnitTests.csproj` 和 `Properties/AssemblyInfo.cs`。

- [x] 在 csproj 定义默认 `Distribution=Msix`；Portable 设置 `WindowsPackageType=None`、`WindowsAppSDKSelfContained=true`、`SelfContained=true`、`PublishSingleFile=false`，非法值产生 MSBuild Error。
- [x] 将 Server `.winmd` 输入、COM 客户端引用和 `BuildFilesAppServer` 限定为 Msix，不删除 Msix 的发布目标。
- [x] 创建 MSTest 项目，目标框架使用仓库变量，引用 `Files.App` 和已有 MSTest 包；通过 `InternalsVisibleTo("Files.App.UnitTests")` 仅开放内部纯逻辑测试。
- [x] 运行 `dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -c Debug -p:Platform=x64`，预期发现 0 个测试且成功，作为基线。
- [x] 分别运行 Msix/Portable 的 x64 Debug 构建，预期无错误；此步骤只证明配置可构建，不宣称便携可运行。
- [x] 提交：`Add internal distribution build baseline`。

### 任务 2：启动前应用环境

**文件：** 创建 `IAppRuntimeEnvironment.cs`、`AppRuntime.cs`、`MsixAppRuntimeEnvironment.cs`、`PortableAppRuntimeEnvironment.cs`；修改 `Program.cs`、`Constants.cs`、`ViewModels/Settings/AboutViewModel.cs`。

接口固定为 `DistributionKind Distribution`、`ProductId`、`DisplayName`、`Version`、`InstallDirectory`、`DataDirectory`、`SettingsDirectory`、`LogDirectory`、`CacheDirectory`、`PackageFamilyName?`、`InstanceScope`、`SupportsSystemIntegration`。

- [x] 编写失败测试：Portable 的目录均位于传入根目录、包家族名为 null、两个不同根目录具有不同 `InstanceScope`；Msix 适配器只在构造时读取包 API。
- [x] 运行测试并确认因类型不存在而失败。
- [x] 实现 `AppRuntime.Initialize(DistributionKind, string? portableRoot = null)` 和上述接口；Portable 使用 `AppContext.BaseDirectory` 与程序集版本，默认数据根为 `Data`。
- [x] 把初始化移到 `Program.Main` 所有 `ApplicationData`、`AppInstance` 和 COM Server 访问之前；实例键包含 `ProductId + InstanceScope`。
- [x] 运行测试确认通过，再构建 Msix/Portable x64 Debug。
- [x] 提交：`Add distribution-aware application runtime`。

### 任务 3：本地状态和便携数据锁

**文件：** 创建 `ILocalStateStore.cs`、`ApplicationDataLocalStateStore.cs`、`JsonLocalStateStore.cs`、`PortableDataLease.cs`；修改 `Program.cs`、`ActiveSessionTracker.cs`、`WindowEx.cs`、拖放临时状态调用点。

`ILocalStateStore` 只支持 bool/int/double/string 的显式读写和 Remove，不引入通用反射序列化。

- [x] 编写失败测试：JSON 往返、删除、损坏文件、未来 schema、原子替换失败保留旧文件；锁未被释放时第二租约失败，Dispose 后可重新获取。
- [x] 实现同目录临时文件写入和替换；锁文件为 `Data/.instance.lock`，持有独占 FileStream，不依赖文件是否存在判断锁。
- [x] Portable 在 XAML 前创建必要目录并做真实写探针；失败使用已有 Win32 消息框报告且退出，不写替代目录。
- [x] 将启动 CWD、活动会话、窗口位置和拖放临时状态迁移到 `ILocalStateStore`，Msix 保持 ApplicationData 行为。
- [x] 运行相关测试和两种构建，预期通过。
- [x] 提交：`Add portable local state and data lease`。

### 任务 4：迁移设置、标签和布局数据

**文件：** 修改两个 SettingsService、`AppLifecycleHelper`、打开日志/设置动作、图标缓存、`DefaultSettingsSerializer`；为标签和布局提取与现有公共方法一致的接口并提供 Registry/Portable JSON 实现。

- [x] 编写失败测试：设置与标签 JSON schema、路径移动后的读写、未来版本拒绝、导入导出往返、原子写入失败不丢旧数据。
- [x] 将设置、日志、Sentry 缓存和图标临时文件改用 `AppRuntime`；仅修改设置序列化器为原子写入，不改变其他 Win32 写文件调用。
- [x] 在 `FileTagsHelper`、`LayoutPreferencesDatabaseManager` 按分发选择实现，保持公开方法和现有导入导出格式不变。
- [x] 全局搜索 `Package.Current` 与 `ApplicationData.Current`，将剩余核心使用点迁移；NaturalLanguageSearch 的包解锁和集成功能在 Portable 禁用，不伪造包身份。
- [x] 运行测试并构建；人工检查 Portable 不在 HKCU 写标签、布局和启动计数。
- [x] 提交：`Store internal edition data with its distribution`。

### 任务 5：共享核心 Server，便携版不启动 COM

**文件：** 创建 `src/Files.App.Server.Core`；移动/抽取 `Initialization`、`Cloud` 的共享逻辑；创建应用侧 `IAppServerService`、`PackagedAppServerService`、`InProcessAppServerService`；修改 `App.xaml.cs` 和核心管理器。

- [x] 为共享提供器编写失败测试：Libraries、Quick Access、WSL、ShellNew 和按 detector 组到达的 Cloud 结果；缓存目录由调用方传入。
- [x] 实现 `LocalSystemDataProvider`，移除对 Server `Program.Logger`、`ApplicationData.Current` 和 Tray 的依赖；DTO 映射留在宿主。
- [x] `PackagedAppServerService` 保留现有 COM 和生命周期，`InProcessAppServerService` 返回同等核心数据且 `SetWindowHandle` 无操作。
- [x] 删除 `App.xaml.cs` 静态 Server 创建，改为 DI 后初始化；Portable 不生成或启动 Server 宿主。
- [x] 运行提供器测试、两种构建；分别冒烟 Libraries、Quick Access、WSL、Cloud、ShellNew 和基本文件操作。
- [x] 提交：`Share core server data across distributions`。

### 任务 6：本地更新与集成开关

**文件：** 新增 `ManualUpdateService`、no-op JumpList/集成实现；修改 DI、`AdvancedPage.xaml(.cs)`、`AdvancedViewModel.cs`、重启/新窗口入口和 `Package.appxmanifest`。

- [x] 编写失败测试：手动更新所有操作均为本地无操作且不发 HTTP；Portable 集成调用不触发 StartupTask、协议或注册表。
- [x] 两版注册 ManualUpdateService，不能复用仍请求官方 release notes 的 Dummy 实现；Portable 隐藏启动项、Tray、后台驻留和接管设置。
- [x] 将内部包 Identity、显示名、协议、alias、Server 名与原版隔离；新窗口/重启使用实际 `Environment.ProcessPath`，不硬编码 `files-dev`。
- [x] 运行测试、构建并检查运行日志/网络跟踪，预期无 `cdn.files.community`、官方 appinstaller 或 Store 更新请求。
- [x] 提交：`Disable hosted updates and portable integrations`。

### 任务 7：独占接管的显式状态与条件恢复

**文件：** 修改默认文件管理器、打开/保存对话框相关服务和 `.reg`/原生启动器配置；新增纯逻辑状态存储测试。

- [x] 编写失败测试：首次安装无动作；启用前保存原值；关闭时仅当当前值仍属于内部产品才恢复；外部再次修改后不覆盖。
- [x] 将原生组件中的共享 `%LocalAppData%/Files` 与 `files-dev` 改为内部独立目录和协议，保存接管前状态。
- [x] 在 Advanced 设置中默认关闭并明确显示“当前由谁接管”；卸载只撤销仍归本产品所有的修改。
- [x] 运行测试和 x64 原生/托管构建，按“原版→内部接管→外部修改→内部关闭”顺序验证。
- [x] 提交：`Isolate optional internal shell integrations`。

### 任务 8：内部发布和干净环境验收

**文件：** 创建 `.github/scripts/Build-Internal.ps1` 与简短内部发布说明。

- [x] 脚本只生成 x64 内部签名 MSIX、完整 Portable ZIP 和 SHA-256 清单，记录版本及上游 commit；私钥不进入产物。
- [x] 运行全部单元测试、Msix/Portable Debug 与 Release 构建及 Publish；预期全部成功。
- [ ] 在未安装额外 .NET/Windows App SDK 的干净 Windows VM，以非管理员从含中文和空格的路径及 U 盘启动 ZIP，验证设置、标签、布局、核心 Server 功能和基本文件操作。
- [ ] 验证 Data 不可写、第二实例、移动目录/改变盘符、不同便携副本及原版/内部安装版共存；检查无隐藏注册和官方更新请求。
> 环境验收（干净 VM、U 盘、正式证书、真实 Explorer 与网络跟踪）尚未执行；在完成前不得把本计划标记为全部验收通过。
- [ ] 只有上述证据齐全后才标记阶段完成；提交：`Add internal edition release outputs`。
