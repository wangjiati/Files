# 文件系统链接可视化 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在 Files 的详情/列表/磁贴/网格/分栏中，用独立角标和“真实类型 · 链接类型”文字区分符号链接、目录联接、硬链接、卷挂载点，并为文件系统链接提供只读“链接”属性页；`.lnk` / `.url` 保持现有快捷方式行为。

**架构：** 纯逻辑 `LinkClassifier` / `ReparsePointParser` / `LinkTypeHelper` 放在 `Utils/Storage/Links/` 并单测覆盖；新增 `LinkItem` / `GitLinkItem` 承载文件系统链接，不再复用 `ShortcutItem`；Win32 枚举只读 reparse tag，硬链接在实现项上惰性探测并缓存；`LinkBadge` 用户控件统一角标，`LinkPage` 复用现有属性页基础设施。

**技术栈：** C#/.NET 8、WinUI 3 (WindowsAppSDK)、MSTest、CsWin32 (`PInvoke`)、现有 `Win32Helper` / `Win32PInvoke` 包装。

**规格：** `docs/superpowers/specs/2026-11-03-link-visualization-design.md`（执行者必读）；视觉参考 `.superpowers/brainstorm/1108-1793804187/content/link-visualization-v1.html`

## 全局约束

- 构建：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`；不并行构建；msbuild 不可用时进 VS DevShell。
- 单测：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links"`（MSTest，`[TestClass]/[TestMethod]`）。
- 用户可见字符串只补 `src/Files.App/Strings/en-US/Resources.resw` 和 `zh-Hans/Resources.resw`；其他语言沿用现有回退。XAML 用 `{helpers:ResourceString}`，代码用 `Strings.Xxx.GetLocalizedResource()`。
- 角标固定“填充”样式，不新增用户设置；类型文字分隔符固定为 `" · "`。
- 硬链接探测：只对本地固定卷 NTFS/ReFS 上的文件、最多 4 并发、按路径缓存到刷新；跳过 reparse point、`Offline`、`RECALL_ON_OPEN`、`RECALL_ON_DATA_ACCESS`。
- 禁止 `DllImport` / `ComImport` / 运行时反射；不改 CsWin32 生成代码；优先用 `Win32Helper` / `Win32PInvoke` 现有包装。
- 只动必要文件；`.editorconfig`；保持 CRLF；不提交未请求的生成文件或临时原型。
- v1 不做创建/编辑/删除链接，不改变双击、复制、删除、重命名和搜索遍历语义。

## 审查重点（Review Focus）

1. **坏链接 / 相对链接解析错误**：目标不存在或相对路径解析错会显示错误目标，甚至让属性页崩溃 → 任务 2 的 `ReparsePointParserTests` + 任务 8 手工用例。
2. **云占位符被硬链接探测打开**：OneDrive 占位符被 `CreateFile` 触发下载，滚动卡顿或产生网络流量 → 任务 5 的 `HardLinkProbePolicyTests` + 任务 8 手工用例。
3. **junction 与卷挂载点误判**：指向卷根的 junction 被显示成“卷挂载点”，类型和属性页都错 → 任务 1 的 classifier 测试 + 任务 4 手工用例。
4. **git 工作区符号链接回归**：把 git 符号链接降级为 `LinkItem` 会丢失 git 状态列和“按目标判断可执行” → 任务 4 的 `GitLinkItem` 分支 + 任务 8 手工用例。
5. **符号链接仍触发快捷方式专属逻辑**：继续复用 `ShortcutItem` 会让“以管理员身份运行”“快捷方式”属性页误作用到文件系统链接 → 任务 3 的 `LinkItemTests` + 任务 7 手工用例。

---

### 任务 1：链接分类与类型文字

**文件：**
- 创建：`src/Files.App/Data/Enums/LinkKind.cs`
- 创建：`src/Files.App/Utils/Storage/Links/LinkClassifier.cs`
- 创建：`src/Files.App/Utils/Storage/Links/LinkTypeHelper.cs`
- 修改：`src/Files.App/Strings/en-US/Resources.resw`、`src/Files.App/Strings/zh-Hans/Resources.resw`
- 测试：`tests/Files.App.UnitTests/Links/LinkClassifierTests.cs`、`tests/Files.App.UnitTests/Links/LinkTypeHelperTests.cs`

- [ ] **步骤 1：写失败测试**

```csharp
// LinkClassifierTests
Assert.AreEqual(LinkKind.SymbolicLink, LinkClassifier.FromReparseTag(0xA000000C, false, null));
Assert.AreEqual(LinkKind.Junction, LinkClassifier.FromReparseTag(0xA0000003, true, @"D:\Archive\2026"));
Assert.AreEqual(LinkKind.VolumeMountPoint, LinkClassifier.FromReparseTag(0xA0000003, true, @"\\?\Volume{8f2a4c1e-7b3d-4a2e-9f10-2c6d8e4a1b77}\"));
Assert.AreEqual(LinkKind.VolumeMountPoint, LinkClassifier.FromReparseTag(0xA0000003, true, @"\??\Volume{8f2a4c1e-7b3d-4a2e-9f10-2c6d8e4a1b77}\"));
Assert.AreEqual(LinkKind.None, LinkClassifier.FromReparseTag(0x9000001A, false, null));

// LinkTypeHelperTests
Assert.AreEqual("文本文档 · 符号链接", LinkTypeHelper.Compose("文本文档", "符号链接"));
Assert.AreEqual("文件夹 · 目录联接", LinkTypeHelper.Compose("文件夹", "目录联接"));
Assert.AreEqual("符号链接", LinkTypeHelper.Compose(null, "符号链接"));
Assert.AreEqual("文本文档", LinkTypeHelper.Compose("文本文档", null));
```

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links"`
预期：FAIL，`LinkKind` / `LinkClassifier` / `LinkTypeHelper` 不存在。

- [ ] **步骤 3：实现**

- `LinkKind`：`None, ShellShortcut, InternetShortcut, SymbolicLink, Junction, HardLink, VolumeMountPoint`。
- `LinkClassifier.FromReparseTag(uint reparseTag, bool isDirectory, string? targetPath)`：
  - `0xA000000C` → `SymbolicLink`；
  - `0xA0000003` → 目标是 `\??\Volume{` 或 `\\?\Volume{` 开头时 `VolumeMountPoint`，否则 `Junction`；
  - 其他 → `None`。
- `LinkTypeHelper.Compose(string? baseType, string? linkLabel)`：两边都空返回空；只有一边返回另一边；否则 `$"{baseType} · {linkLabel}"`。
- `LinkTypeHelper.GetLabel(LinkKind kind, bool isDirectory)` 用 `Strings.LinkTypeSymbolicFile/Folder/Junction/HardLink/VolumeMountPoint`；`ShellShortcut` 用 `Strings.Shortcut`，`InternetShortcut` 用 `Strings.ShortcutWebLinkFileType`。
- 资源键（en-US / zh-Hans）：

| Key | en-US | zh-Hans |
|---|---|---|
| `Link` | Link | 链接 |
| `LinkTypeSymbolicLink` | Symbolic link | 符号链接 |
| `LinkTypeSymbolicFile` | Symbolic link (file) | 符号链接（文件） |
| `LinkTypeSymbolicFolder` | Symbolic link (folder) | 符号链接（文件夹） |
| `LinkTypeJunction` | Junction | 目录联接 |
| `LinkTypeHardLink` | Hard link | 硬链接 |
| `LinkTypeVolumeMountPoint` | Volume mount point | 卷挂载点 |
| `LinkPathTypeRelative` | Relative | 相对路径 |
| `LinkPathTypeAbsolute` | Absolute | 绝对路径 |
| `LinkTargetExists` | Target exists | 目标存在 |
| `LinkTargetMissing` | Target missing | 目标不存在 |
| `LinkTargetNoAccess` | Access denied | 无权访问 |
| `LinkOpenTarget` | Open target location | 打开目标位置 |
| `LinkCopyTarget` | Copy target path | 复制目标路径 |
| `LinkHardLinkCount` | Link count | 链接数 |
| `LinkItemAutomation` | Link item | 链接项 |

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links"`
预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/Data/Enums/LinkKind.cs src/Files.App/Utils/Storage/Links src/Files.App/Strings/en-US/Resources.resw src/Files.App/Strings/zh-Hans/Resources.resw tests/Files.App.UnitTests/Links
git commit -m "Add link kind classification"
```

### 任务 2：重解析点解析器

**文件：**
- 创建：`src/Files.App/Utils/Storage/Links/ReparsePointParser.cs`
- 修改：`src/Files.App/Helpers/Win32/Win32Helper.Storage.cs:731-767`
- 修改：`src/Files.App/Helpers/Win32/Win32PInvoke.Consts.cs`（新增 `FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000`）
- 测试：`tests/Files.App.UnitTests/Links/ReparsePointParserTests.cs`

- [ ] **步骤 1：写失败测试**

测试内写 `BuildBuffer(uint tag, string substitute, string print, bool relative = false)`：
- 头部 8 字节；symlink 在偏移 16 写 `Flags`（relative=1），路径缓冲区从 20 开始；mount point 路径缓冲区从 16 开始。
- 每个用例放独立 `[TestMethod]`，避免重复的 `info` 局部变量。
- 断言：

```csharp
// 绝对符号链接
var info = Parse(0xA000000C, @"\??\C:\Target\file.txt", @"C:\Target\file.txt", relative: false, linkPath: @"D:\Projects\link.txt");
Assert.AreEqual(@"C:\Target\file.txt", info.TargetPath);
Assert.IsFalse(info.IsRelative);

// 相对符号链接，相对链接所在目录解析
var info = Parse(0xA000000C, @"..\shared\config.yaml", @"..\shared\config.yaml", relative: true, linkPath: @"D:\Projects\2026\config.yaml");
Assert.AreEqual(@"D:\Projects\shared\config.yaml", info.TargetPath);
Assert.IsTrue(info.IsRelative);

// junction
var info = Parse(0xA0000003, @"\??\D:\Archive\2026", @"D:\Archive\2026", relative: false, linkPath: @"D:\Projects\archive");
Assert.AreEqual(@"D:\Archive\2026", info.TargetPath);

// 卷挂载点
var info = Parse(0xA0000003, @"\??\Volume{8f2a4c1e-7b3d-4a2e-9f10-2c6d8e4a1b77}\", @"", relative: false, linkPath: @"D:\Data");
Assert.AreEqual(@"\\?\Volume{8f2a4c1e-7b3d-4a2e-9f10-2c6d8e4a1b77}\", info.TargetPath);

// 非法缓冲区
Assert.IsFalse(ReparsePointParser.TryParse(new byte[8], "C:\\link", out _));
Assert.IsFalse(ReparsePointParser.TryParse(new byte[24], "C:\\link", out _));
```

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links.ReparsePointParserTests"`
预期：FAIL。

- [ ] **步骤 3：实现**

- `public readonly record struct ReparsePointInfo(uint Tag, string TargetPath, bool IsRelative);`
- `public static bool TryParse(ReadOnlySpan<byte> buffer, string linkPath, out ReparsePointInfo info)`：
  - 校验 `buffer.Length >= 16`；
  - symlink 路径起点 20，mount point 路径起点 16；
  - `SubstituteNameOffset/Length` 在 8/10，`PrintNameOffset/Length` 在 12/14；
  - symlink 的 `Flags` 在偏移 16，`SYMLINK_FLAG_RELATIVE = 1`；
  - 解析相对路径用 substitute（规范名），显示优先用 print name（空时用 substitute）；
  - 绝对路径去掉 `\??\`；`\??\Volume{...}\` 转成 `\\?\Volume{...}\`；
  - 相对 symlink 用 `Path.GetDirectoryName(linkPath)` + substitute 做 `Path.GetFullPath`；
  - `tag` 不是 `SYMLINK` / `MOUNT_POINT`、目标为空、偏移或长度越界时返回 false；
  - 任何异常返回 false，不抛出。
- `Win32Helper.GetReparsePointInfo(string path)`：用 `OpenFileForRead(path, false, Win32PInvoke.FILE_FLAG_OPEN_REPARSE_POINT)` + `FSCTL_GET_REPARSE_POINT`，调用 `ReparsePointParser.TryParse`。
- 保留 `ParseSymLink(string path)` 作为 `GetReparsePointInfo(path)?.TargetPath` 的兼容包装。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links.ReparsePointParserTests"`
预期：PASS。

- [ ] **步骤 5：构建 + Commit**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
git add src/Files.App/Utils/Storage/Links/ReparsePointParser.cs src/Files.App/Helpers/Win32/Win32Helper.Storage.cs src/Files.App/Helpers/Win32/Win32PInvoke.Consts.cs tests/Files.App.UnitTests/Links/ReparsePointParserTests.cs
git commit -m "Add reparse point parser"
```

### 任务 3：链接项模型

**文件：**
- 修改：`src/Files.App/Data/Items/ListedItem.cs:30`（新增属性）、`:608-632`（ShortcutItem）、`:803-818`（GitShortcutItem）
- 创建：`src/Files.App/Data/Items/LinkItem.cs`
- 测试：`tests/Files.App.UnitTests/Links/LinkItemTests.cs`

- [ ] **步骤 1：写失败测试**

```csharp
var item = new LinkItem(LinkKind.Junction) { PrimaryItemAttribute = StorageItemTypes.Folder };
Assert.IsTrue(item.IsFileSystemLink);
Assert.IsFalse(item.IsShortcut);
Assert.AreEqual(LinkKind.Junction, item.LinkKind);

var gitItem = new GitLinkItem(LinkKind.SymbolicLink);
Assert.IsTrue(gitItem is GitItem);
Assert.IsFalse(gitItem.IsShortcut);
```

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links.LinkItemTests"`
预期：FAIL，`LinkItem` / `GitLinkItem` 不存在。

- [ ] **步骤 3：实现**

- `ListedItem` 增加：
  - `LinkKind LinkKind { get; set; }`（`SetProperty`）；
  - `string? LinkTargetPath { get; set; }`；
  - `uint? HardLinkCount { get; set; }`；
  - `string? BaseItemType { get; set; }`；
  - `FileAttributes ItemFileAttributes { get; set; }`；
  - `bool IsFileSystemLink => LinkKind is SymbolicLink or Junction or HardLink or VolumeMountPoint;`。
- `ListedItem.ItemType` 的 setter（`:254-263`）在 `SetProperty` 后追加 `OnPropertyChanged(nameof(ItemTooltipText))`，让硬链接异步更新类型后工具提示同步刷新。
- `LinkItem : ListedItem`：构造函数 `LinkItem(LinkKind linkKind)`，只设置 `LinkKind`。
- `GitLinkItem : GitItem`：同上；不实现 `IShortcutItem`。
- `ShortcutItem` / `GitShortcutItem` 不新增 `IShortcutItem` 成员；`LinkKind` 由枚举器赋值。
- 不要删除 `IShortcutItem.IsSymLink`；保留兼容，但文件系统链接不再走 `ShortcutItem`。
- `ListedItem.ToString()`（`:446-467`）在 `IsShortcut` 分支前增加 `else if (IsFileSystemLink) suffix = Strings.LinkItemAutomation...;`，避免读屏把链接读成文件夹。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links.LinkItemTests"`
预期：PASS。

- [ ] **步骤 5：构建 + Commit**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
git add src/Files.App/Data/Items/ListedItem.cs src/Files.App/Data/Items/LinkItem.cs tests/Files.App.UnitTests/Links/LinkItemTests.cs
git commit -m "Add link item model"
```

### 任务 4：Win32 枚举与搜索结果识别链接

**文件：**
- 修改：`src/Files.App/Utils/Storage/Enumerators/Win32StorageEnumerator.cs:166-249`（GetFolder）、`:305-355`（GetFile 符号链接分支）
- 修改：`src/Files.App/Utils/Storage/Search/FolderSearch.cs:577-604`（搜索循环）、`:677-761`（WIN32_FIND_DATA 分支）、`:763-868`（IStorageItem 分支）

- [ ] **步骤 1：实现文件枚举**

`GetFile`：
- 读 `FileAttributes` 和 `reparseTag = isReparsePoint ? findData.dwReserved0 : 0`。
- `reparseTag == Win32PInvoke.IO_REPARSE_TAG_SYMLINK` → `LinkItem(LinkKind.SymbolicLink)`；设置 `PrimaryItemAttribute = File`、`BaseItemType = itemType`、`ItemFileAttributes = (FileAttributes)findData.dwFileAttributes`、`ItemType = LinkTypeHelper.Compose(itemType, LinkTypeHelper.GetLabel(LinkKind.SymbolicLink, false))`。
- `isGitRepo` 时用 `GitLinkItem`，保留 git 状态字段。
- 删除现有 `ShortcutItem` 符号链接分支；`.lnk` / `.url` 分支保留，并设置 `LinkKind = ShellShortcut` / `InternetShortcut`。
- 普通文件分支同样设置 `ItemFileAttributes = (FileAttributes)findData.dwFileAttributes`。

- [ ] **步骤 2：实现目录枚举**

`GetFolder`：
- 读 `attributes = (FileAttributes)findData.dwFileAttributes` 和 `reparseTag`。
- 在现有 `if (isGitRepo)` 返回 `GitItem` 之前插入 reparse 判定。
- `SYMLINK` → `LinkKind.SymbolicLink`。
- `MOUNT_POINT` → `var target = Win32Helper.GetReparsePointInfo(itemPath)?.TargetPath; LinkClassifier.FromReparseTag(reparseTag, true, target)`。
- 非 `None` 时返回 `LinkItem`（git 仓库用 `GitLinkItem`），设置 `PrimaryItemAttribute = Folder`、`BaseItemType = folderTypeTextLocalized`、`ItemFileAttributes = attributes`、`LinkTargetPath = target`（mount point 已解析，symlink 留空待属性页解析）、`ItemType = Compose(folderType, GetLabel(kind, true))`。卷挂载点的类型文字只用 `GetLabel(VolumeMountPoint, true)`，不加“文件夹 ·”前缀。
- 保留现有 `desktop.ini` 跳过逻辑（`:203`）。

- [ ] **步骤 3：实现搜索结果**

`FolderSearch`：
- `GetListedItemAsync(string, WIN32_FIND_DATAW)`：用 `dwReserved0` 做同样的 tag 判定；文件/目录都创建 `LinkItem`，设置 `BaseItemType`、`ItemType`、`ItemFileAttributes`；`MOUNT_POINT` 解析并设置 `LinkTargetPath`；`.lnk` / `.url` 仍走 `GetShortcutItem`。
- `GetListedItemAsync(IStorageItem)`：本地路径用 `Win32Helper.GetWin32FindDataForPath(item.Path, out var findData)` 补 tag；失败时不加链接信息。
- `GetShortcutItem` 设置 `LinkKind = isUrl ? InternetShortcut : ShellShortcut`；`GetListedItemAsync(IStorageItem)` 里内联创建 `ShortcutItem` 的分支同样设置。
- 所有分支（普通文件/目录、链接、快捷方式）都设置 `ItemFileAttributes`；普通项也要设置，否则任务 5 的硬链接探测无法判断占位符/离线属性。

- [ ] **步骤 4：构建**

运行：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：成功。

- [ ] **步骤 5：手工冒烟**

在临时目录创建 `mklink` 文件/目录链接、`mklink /J`、`mklink /H`、`.lnk`；在详情和搜索中各看一次，确认类型文字和角标数据已经设置（角标本身在任务 6 接入）。记录结果，不通过则修。

- [ ] **步骤 6：Commit**

```powershell
git add src/Files.App/Utils/Storage/Enumerators/Win32StorageEnumerator.cs src/Files.App/Utils/Storage/Search/FolderSearch.cs
git commit -m "Detect file system links during enumeration"
```

### 任务 5：硬链接惰性探测

**文件：**
- 创建：`src/Files.App/Utils/Storage/Links/HardLinkProbePolicy.cs`
- 创建：`src/Files.App/Utils/Storage/Links/HardLinkHelper.cs`
- 修改：`src/Files.App/ViewModels/ShellViewModel.cs:2408`（RefreshItems）
- 修改：`src/Files.App/Views/Layouts/BaseLayoutPage.cs:1386-1406`
- 测试：`tests/Files.App.UnitTests/Links/HardLinkProbePolicyTests.cs`

- [ ] **步骤 1：写失败测试**

```csharp
Assert.IsTrue(HardLinkProbePolicy.ShouldProbe(FileAttributes.Normal, isFolder: false, isLocalNtfsOrReFs: true));
Assert.IsFalse(HardLinkProbePolicy.ShouldProbe(FileAttributes.Normal, isFolder: true, isLocalNtfsOrReFs: true));
Assert.IsFalse(HardLinkProbePolicy.ShouldProbe(FileAttributes.ReparsePoint, isFolder: false, isLocalNtfsOrReFs: true));
Assert.IsFalse(HardLinkProbePolicy.ShouldProbe(FileAttributes.Offline, isFolder: false, isLocalNtfsOrReFs: true));
Assert.IsFalse(HardLinkProbePolicy.ShouldProbe((FileAttributes)0x00040000, isFolder: false, isLocalNtfsOrReFs: true));
Assert.IsFalse(HardLinkProbePolicy.ShouldProbe((FileAttributes)0x00400000, isFolder: false, isLocalNtfsOrReFs: true));
Assert.IsFalse(HardLinkProbePolicy.ShouldProbe(FileAttributes.Normal, isFolder: false, isLocalNtfsOrReFs: false));
```

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links.HardLinkProbePolicyTests"`
预期：FAIL。

- [ ] **步骤 3：实现 policy + helper**

- `HardLinkProbePolicy.ShouldProbe(FileAttributes attributes, bool isFolder, bool isLocalNtfsOrReFs)`：按上面断言返回。
- `HardLinkHelper.GetLinkCount(string path)`：`Win32Helper.OpenFileForRead(path)` + `PInvoke.GetFileInformationByHandleEx(handle, FILE_INFO_BY_HANDLE_CLASS.FileStandardInfo, ...)`；返回 `FILE_STANDARD_INFO.NumberOfLinks`，失败返回 `null`。
- `HardLinkHelper.IsLocalNtfsOrReFs(string path)`：`new DriveInfo(Path.GetPathRoot(path))`，`DriveType.Fixed` 且 `DriveFormat is "NTFS" or "ReFS"`；异常返回 false。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links.HardLinkProbePolicyTests"`
预期：PASS。

- [ ] **步骤 5：接入 ShellViewModel**

- 增加 `ConcurrentDictionary<string, uint?> hardLinkCache`、`ConcurrentDictionary<string, bool> volumeCache`、`SemaphoreSlim(4)`。
- `public async Task EnsureLinkInfoAsync(ListedItem item)`：
  - 只处理 `LinkKind.None && PrimaryItemAttribute == File`；
  - `HardLinkProbePolicy.ShouldProbe(item.ItemFileAttributes, false, HardLinkHelper.IsLocalNtfsOrReFs(item.ItemPath))`；
  - 缓存命中直接使用；未命中 `await semaphore.WaitAsync()` → `Task.Run(() => HardLinkHelper.GetLinkCount(path))` → 写缓存；
  - `count > 1` 时回到 UI 线程设置 `HardLinkCount`、`LinkKind = HardLink`、`ItemType = Compose(item.BaseItemType ?? item.ItemType, GetLabel(HardLink, false))`。
- `RefreshItems(string?, Action?)` 开头清空 `hardLinkCache`。
- `BaseLayoutPage.RefreshItem` 在现有 `ItemPropertiesInitialized` 分支旁追加：
  - 仅当 `LinkKind == None && PrimaryItemAttribute == File && HardLinkProbePolicy.ShouldProbe(item.ItemFileAttributes, false, true)` 时 `EnqueueItemLoad(..., vm => vm.EnsureLinkInfoAsync(item))`。

- [ ] **步骤 6：构建 + Commit**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
git add src/Files.App/Utils/Storage/Links/HardLinkProbePolicy.cs src/Files.App/Utils/Storage/Links/HardLinkHelper.cs src/Files.App/ViewModels/ShellViewModel.cs src/Files.App/Views/Layouts/BaseLayoutPage.cs tests/Files.App.UnitTests/Links/HardLinkProbePolicyTests.cs
git commit -m "Add lazy hard link detection"
```

### 任务 6：LinkBadge 角标控件与布局接入

**文件：**
- 创建：`src/Files.App/UserControls/LinkBadge.xaml` / `LinkBadge.xaml.cs`
- 创建：`src/Files.App/Styles/LinkBadgeStyles.xaml`
- 修改：`src/Files.App/App.xaml:114-116`（合并资源字典）
- 修改：`src/Files.App/Views/Layouts/DetailsLayoutPage.xaml:1006-1014`
- 修改：`src/Files.App/Views/Layouts/GridLayoutPage.xaml:133-146`、`:318-326`、`:461-474`
- 修改：`src/Files.App/Views/Layouts/ColumnLayoutPage.xaml:272-280`

- [ ] **步骤 1：实现 LinkBadge 控件**

`LinkBadge.xaml`：`Grid` + `Border x:Name="BadgeBackground"` + `Path x:Name="Glyph"`；`Path` 使用 `Stretch="Uniform"`、`StrokeThickness="1.5"`、`StrokeLineJoin="Round"`、`StrokeStartLineCap="Round"`、`StrokeEndLineCap="Round"`；`Stroke="{x:Bind GlyphBrush, Mode=OneWay}"`，`Background="{x:Bind BadgeBrush, Mode=OneWay}"`。

`LinkBadge.xaml.cs`：
- DP：`LinkKind Kind`（避免属性名与枚举类型同名）、`Brush? BadgeBrush`、`Brush? GlyphBrush`。
- `Kind` 变化时：
  - `None` → `Visibility.Collapsed`；
  - `ShellShortcut` / `InternetShortcut` → `Style = LinkBadgeShortcutStyle`，`BadgeBackground.Visibility = Collapsed`；
  - `SymbolicLink` / `Junction` / `HardLink` / `VolumeMountPoint` → 对应 `Style`，`BadgeBackground.Visibility = Visible`；
  - 设置 `Glyph.Data` 为对应 `Geometry` 资源。
- 资源键：`LinkGlyphShortcut`、`LinkGlyphSymbolic`、`LinkGlyphJunction`、`LinkGlyphHardLink`、`LinkGlyphMount`；样式：`LinkBadgeShortcutStyle`、`LinkBadgeSymbolicStyle`、`LinkBadgeJunctionStyle`、`LinkBadgeHardLinkStyle`、`LinkBadgeVolumeMountStyle`。

- [ ] **步骤 2：实现主题资源**

`Styles/LinkBadgeStyles.xaml` 的 `ThemeDictionaries`：

| Brush | Light | Dark | HighContrast |
|---|---|---|---|
| `LinkBadgeSymbolicBrush` | `#0F6CBD` | `#4CC2FF` | `SystemColorHighlightColor` |
| `LinkBadgeJunctionBrush` | `#8764B8` | `#B49BE0` | `SystemColorHighlightColor` |
| `LinkBadgeHardLinkBrush` | `#9D5D00` | `#FCE100` | `SystemColorHighlightColor` |
| `LinkBadgeVolumeMountBrush` | `#038387` | `#4CC2FF` | `SystemColorHighlightColor` |
| `LinkBadgeGlyphBrush` | `#FFFFFF` | `#001E28` | `SystemColorHighlightTextColor` |
| `LinkBadgeShortcutBrush` | `Transparent` | `Transparent` | `Transparent` |

- Shortcut 样式用 `GlyphBrush = AccentTextFillColorPrimaryBrush`，无背景。
- 几何路径直接复制原型 `.superpowers/brainstorm/1108-1793804187/content/link-visualization-v1.html` 中 `badgeSvgs` 的 5 个 16×16 路径，转成 XAML `Geometry`。

- [ ] **步骤 3：接入五个模板**

把每个 `ShortcutGlyphElement` Viewbox 替换为：

```xml
<uc:LinkBadge
    Width="14"
    Height="14"
    HorizontalAlignment="Left"
    VerticalAlignment="Bottom"
    Kind="{x:Bind LinkKind, Mode=OneWay}" />
```

- 网格/磁贴里的两处用 `Width="18" Height="18"`（磁贴右上保持现有 margin 位置）。
- 分栏用 `Width="14" Height="14"`。
- 保留原 `Viewbox` 上的 `x:Phase`、`Margin`、`HorizontalAlignment`、`VerticalAlignment`；只替换控件本体。
- 保留 `IconOverlayElement` 和 `ShieldOverlay`；不要改 `NavigationToolbar.xaml` 的当前文件夹快捷方式图标。

- [ ] **步骤 4：构建 + 视觉检查**

运行：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：成功。手工对比原型，检查浅色/深色/高对比和五种布局的角标位置、尺寸、遮挡。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/UserControls/LinkBadge.xaml src/Files.App/UserControls/LinkBadge.xaml.cs src/Files.App/Styles/LinkBadgeStyles.xaml src/Files.App/App.xaml src/Files.App/Views/Layouts/DetailsLayoutPage.xaml src/Files.App/Views/Layouts/GridLayoutPage.xaml src/Files.App/Views/Layouts/ColumnLayoutPage.xaml
git commit -m "Add link badge control"
```

### 任务 7：链接属性页

**文件：**
- 修改：`src/Files.App/Data/Enums/PropertiesNavigationViewItemType.cs`
- 修改：`src/Files.App/Data/Factories/PropertiesNavigationItemsFactory.cs:17-37`、`:59-107`
- 修改：`src/Files.App/ViewModels/Properties/MainPropertiesViewModel.cs:85-97`
- 修改：`src/Files.App/Data/Models/SelectedItemsPropertiesViewModel.cs:546-661`
- 修改：`src/Files.App/ViewModels/Properties/Items/FileProperties.cs:29-50`
- 修改：`src/Files.App/ViewModels/Properties/Items/FolderProperties.cs:32-60`
- 创建：`src/Files.App/Utils/Storage/Links/LinkPropertiesHelper.cs`
- 创建：`src/Files.App/Views/Properties/LinkPage.xaml` / `LinkPage.xaml.cs`
- 修改：`src/Files.App/Strings/en-US/Resources.resw`、`src/Files.App/Strings/zh-Hans/Resources.resw`（补 `PropertiesLink*.Text`）

- [ ] **步骤 1：扩展属性 ViewModel**

在 `SelectedItemsPropertiesViewModel` 增加：
- `bool IsFileSystemLink`
- `string? LinkType`
- `string? LinkTargetPath`
- `string? LinkPathType`
- `bool IsLinkPathTypeVisible`
- `string? LinkTargetStatus`
- `bool IsLinkTargetMissing`
- `bool IsHardLink`
- `bool IsLinkTargetVisible`
- `uint? LinkHardLinkCount`
- `string? LinkHardLinkCountText`
- `RelayCommand? OpenLinkTargetCommand`
- `RelayCommand? CopyLinkTargetCommand`

- [ ] **步骤 2：实现 LinkPropertiesHelper**

`public static void Populate(SelectedItemsPropertiesViewModel viewModel, ListedItem item)`：
- `if (!item.IsFileSystemLink) return;`
- `var info = Win32Helper.GetReparsePointInfo(item.ItemPath);`
- `viewModel.LinkType = LinkTypeHelper.GetLabel(item.LinkKind, item.IsFolder);`
- `viewModel.LinkTargetPath = item.LinkTargetPath ?? info?.TargetPath;`
- 符号链接：`LinkPathType = info?.IsRelative == true ? Strings.LinkPathTypeRelative : Strings.LinkPathTypeAbsolute`；相对时 `LinkTargetPath` 用 `info.TargetPath`（已解析）；`IsLinkPathTypeVisible = true`。junction / 卷挂载点设为 false。
- 目标状态：`Directory.Exists / File.Exists` → `LinkTargetExists`；都不存在 → `LinkTargetMissing`；访问异常 → `LinkTargetNoAccess`；设置 `IsLinkTargetMissing`。
- `IsHardLink = item.LinkKind == LinkKind.HardLink`；`LinkHardLinkCount = item.HardLinkCount`；`LinkHardLinkCountText = item.HardLinkCount?.ToString()`；`IsLinkTargetVisible = !IsHardLink`。
- `OpenLinkTargetCommand`：目标是目录 → `NavigationHelpers.OpenPathInNewTab(target, true)`；目标是文件 → 打开父目录；目标不存在 → 不执行。
- `CopyLinkTargetCommand`：`DataPackage.SetText(target)` + `Clipboard.SetContent`。

- [ ] **步骤 3：接入 FileProperties / FolderProperties**

在 `GetBaseProperties` 设置 `ViewModel.ItemType` 之后、`if (!Item.IsShortcut) return;` 之前调用 `LinkPropertiesHelper.Populate(ViewModel, Item);`。`FolderProperties` 同样在 shortcut 分支之前调用。

- [ ] **步骤 4：注册“链接”页**

- `PropertiesNavigationViewItemType` 增加 `Link`。
- `PropertiesNavigationItemsFactory` 增加 `linkItem`（文字 `Strings.Link`，图标复用 `App.ThemedIcons.Shortcut`），加入集合；对多选/驱动器/非 `IsFileSystemLink` 项移除。
- `MainPropertiesViewModel` switch 增加 `PropertiesNavigationViewItemType.Link => typeof(LinkPage)`。

- [ ] **步骤 5：实现 LinkPage**

`LinkPage.xaml`（`Tag="Link"`，`Loaded="Properties_Loaded"`）：
- 卡片一：`PropertiesLinkType.Text` + `ViewModel.LinkType`；`PropertiesLinkTarget.Text` + 只读 `TextBox` 绑定 `ViewModel.LinkTargetPath`；`PropertiesLinkPathType.Text` + `ViewModel.LinkPathType`；`PropertiesLinkTargetStatus.Text` + `ViewModel.LinkTargetStatus`。
- 卡片二（硬链接）：`x:Load="{x:Bind ViewModel.IsHardLink, Mode=OneWay}"`；`PropertiesLinkHardLinkCount.Text` + `ViewModel.LinkHardLinkCountText`；v1 不显示“其他路径”，留到 v2。
- 目标、状态两行以及两个操作卡绑定 `x:Load="{x:Bind ViewModel.IsLinkTargetVisible, Mode=OneWay}"`；路径类型行额外绑定 `x:Load="{x:Bind ViewModel.IsLinkPathTypeVisible, Mode=OneWay}"`；硬链接只显示类型和链接数。
- 操作：两个 `wctcontrols:SettingsCard`，`x:Load="{x:Bind ViewModel.IsLinkTargetVisible, Mode=OneWay}"`，分别绑定 `OpenLinkTargetCommand` / `CopyLinkTargetCommand`。
- 资源键补：

| Key | en-US | zh-Hans |
|---|---|---|
| `PropertiesLinkType.Text` | Link type | 链接类型 |
| `PropertiesLinkTarget.Text` | Target | 目标 |
| `PropertiesLinkPathType.Text` | Path type | 路径类型 |
| `PropertiesLinkTargetStatus.Text` | Status | 状态 |
| `PropertiesLinkHardLinkCount.Text` | Link count | 链接数 |

`LinkPage.xaml.cs`：`SaveChangesAsync` 返回 `Task.FromResult(true)`；`Dispose` 空。

- [ ] **步骤 6：构建 + 手工验证**

运行：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：成功。对每个链接类型打开属性页，确认“链接”页只在文件系统链接出现，`.lnk` 仍走“快捷方式”页。

- [ ] **步骤 7：Commit**

```powershell
git add src/Files.App/Data/Enums/PropertiesNavigationViewItemType.cs src/Files.App/Data/Factories/PropertiesNavigationItemsFactory.cs src/Files.App/ViewModels/Properties/MainPropertiesViewModel.cs src/Files.App/Data/Models/SelectedItemsPropertiesViewModel.cs src/Files.App/ViewModels/Properties/Items/FileProperties.cs src/Files.App/ViewModels/Properties/Items/FolderProperties.cs src/Files.App/Utils/Storage/Links/LinkPropertiesHelper.cs src/Files.App/Views/Properties/LinkPage.xaml src/Files.App/Views/Properties/LinkPage.xaml.cs src/Files.App/Strings/en-US/Resources.resw src/Files.App/Strings/zh-Hans/Resources.resw
git commit -m "Add link properties page"
```

### 任务 8：整体验证与收尾

- [ ] **步骤 1：完整单测**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj --filter "FullyQualifiedName~Files.App.UnitTests.Links"`
预期：全部 PASS。

- [ ] **步骤 2：构建**

运行：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：成功，无新增错误。

- [ ] **步骤 3：手工验证矩阵**

在临时目录：

```cmd
mklink file-link.txt target.txt
mklink /D dir-link target-dir
mklink /J junction target-dir
mklink /H hard.txt target.txt
```

验证：
- 详情、列表、磁贴、网格、分栏：角标和类型文字正确；`.lnk` / `.url` 仍是快捷方式箭头。
- 浅色 / 深色 / 高对比：角标可见；高对比下形状仍可区分。
- 坏符号链接（删除目标）、相对符号链接、UNC 目标、跨卷 junction、卷挂载点（如可创建）。
- git 工作区内的符号链接：git 状态列仍存在。
- 属性页：类型、目标、相对/绝对、目标状态、打开目标位置、复制目标路径、硬链接链接数。
- `.lnk` 属性页仍是“快捷方式”，不出现“链接”页。
- 双击目录链接仍进入链接路径；复制/删除/重命名行为与改动前一致。
- 10k 文件目录滚动：不因硬链接探测明显卡顿；OneDrive 目录滚动不触发下载。
- 搜索结果显示正确的链接类型。

- [ ] **步骤 4：检查 diff 与格式**

```powershell
git diff --check
git status --short
```

只保留本计划相关文件；不提交 `.superpowers/` 原型、临时测试文件或无关改动。

- [ ] **步骤 5：最终 Commit（仅当有验证修复）**

```powershell
git add <修复文件>
git commit -m "Fix link visualization verification issues"
```
