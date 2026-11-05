# 文件系统链接可视化设计

日期：2026-11-03
状态：已批准（2026-11-04）
视觉参考：`.superpowers/brainstorm/1108-1793804187/content/link-visualization-v1.html`（可交互高保真原型）

## 1. 背景与意图

Files 现在把“链接”压成了两种表现：

- 文件符号链接被枚举成 `ShortcutItem`（`src/Files.App/Utils/Storage/Enumerators/Win32StorageEnumerator.cs:305-355`），类型文字是“快捷方式”，和 `.lnk` 混在一起。
- 目录符号链接和目录联接（junction）走 `GetFolder`（同文件 `:166-249`），完全按普通文件夹显示。
- 硬链接没有检测，按普通文件显示。
- `.lnk` / `.url` 仍是“快捷方式”。

用户要求：链接对象要能**一眼分辨**，同时使用**图标角标**和**文件类型文字**，不再统一显示成文件夹或快捷方式。

本次只做可视化：识别、角标、类型文字、属性页。不改变创建、编辑、删除、复制和导航语义。

## 2. 目标 / 非目标 / 反目标

### 目标（v1）

- 识别四类文件系统链接：符号链接（文件/目录）、目录联接、硬链接、卷挂载点；`.lnk` / `.url` 保持现有快捷方式表现。
- 图标角标用**形状 + 颜色**区分类型，灰度或色觉障碍下也能靠形状区分。
- 类型列显示“真实类型 · 链接类型”，例如 `文本文档 · 符号链接`。
- 属性页新增“链接”页：类型、目标、相对/绝对、目标是否存在、打开目标位置、复制目标路径；硬链接显示链接数。
- 双击、复制、删除、重命名、搜索遍历行为保持不变。

### 非目标（v1 明确不做）

- 创建链接（符号链接 / junction / 硬链接）的 UI。
- 编辑链接目标；改目标仍然只能删除后重建。
- “复制链接本身还是复制目标”的策略。
- 删除和递归遍历的链接安全策略。
- 网络共享、MTP、FTP、压缩包内的链接识别。
- OneDrive 云占位符的重新分类；继续由现有 `IconOverlay` 表示。
- 跟随/不跟随链接的导航设置。

### 反目标（做了就算失败）

- 只给一个笼统的“链接”角标，用户仍要打开属性才能知道类型。
- 把符号链接继续塞进 `ShortcutItem`，导致“以管理员身份运行”“快捷方式”属性页等误用。
- 为了识别硬链接，在目录枚举时对每个文件 `CreateFile`，把大目录拖慢。
- 用颜色作为唯一区分手段。
- 改动复制/删除语义，或让递归删除穿过 junction。

## 3. 链接分类与判定

| 类型 | 判定依据 | 现在的显示 | 目标显示 |
|---|---|---|---|
| `.lnk` 快捷方式 | 扩展名 `.lnk` + `IShellLink` | 快捷方式 | 保持快捷方式 |
| `.url` Internet 快捷方式 | 扩展名 `.url` | Internet 快捷方式 | 保持 Internet 快捷方式 |
| 符号链接（文件） | `FILE_ATTRIBUTE_REPARSE_POINT` + `IO_REPARSE_TAG_SYMLINK` + 非目录 | 快捷方式（`ShortcutItem`） | 符号链接，链环角标 |
| 符号链接（目录） | 同上 + `FILE_ATTRIBUTE_DIRECTORY` | 普通文件夹 | 符号链接，链环角标 |
| 目录联接 junction | `IO_REPARSE_TAG_MOUNT_POINT`，目标不是卷 GUID 路径 | 普通文件夹 | 目录联接，文件夹箭头角标 |
| 卷挂载点 | `IO_REPARSE_TAG_MOUNT_POINT`，目标以 `\??\Volume{` 开头 | 普通文件夹/驱动器 | 卷挂载点，驱动器箭头角标 |
| 硬链接 | 不是重解析点；`GetFileInformationByHandle` 的 `nNumberOfLinks > 1` | 普通文件 | 硬链接，叠页角标 |
| 其他重解析点 | 云占位符、`AppExecLink` 等未知 tag | 普通对象 | 不添加角标，不猜测类型 |

判定细节：

- 枚举时只读 `WIN32_FIND_DATA.dwReserved0`，不打开文件；`dwReserved0` 在 `FILE_ATTRIBUTE_REPARSE_POINT` 存在时就是 reparse tag。
- `IO_REPARSE_TAG_SYMLINK` 和 `IO_REPARSE_TAG_MOUNT_POINT` 需要加入 `src/Files.Storage/NativeMethods.txt`，由 CsWin32 生成常量。
- junction 和卷挂载点共用 `IO_REPARSE_TAG_MOUNT_POINT`，需要读目标：目标以 `\??\Volume{` 开头时按卷挂载点处理；其余 `MOUNT_POINT`（包括手工建立的、指向 `D:\` 的 junction）一律按目录联接处理。这样不会把“指向卷根的 junction”误判成卷挂载点。
- 符号链接不要求目标存在；坏链接仍然显示角标和类型，目标状态只在属性页标记。
- 硬链接不是重解析点，必须通过文件句柄的链接计数判断，不能靠 tag。

## 4. 视觉规范

### 4.1 图标角标

新增 `LinkBadge` 用户控件，替换现有五个 `ShortcutGlyphElement`：

| 链接类型 | 形状 | 颜色 | 说明 |
|---|---|---|---|
| `.lnk` / `.url` | 现有快捷方式箭头 | 现有 accent | 保留用户已经熟悉的视觉，不改 |
| 符号链接 | 两个相扣的链环 | 蓝 | 文件和目录符号链接共用；文件/目录由类型文字区分 |
| 目录联接 | 文件夹轮廓 + 回折箭头 | 紫 | 与符号链接明显不同的轮廓 |
| 硬链接 | 两张叠在一起的纸 | 琥珀 | 与“链环”区分 |
| 卷挂载点 | 驱动器轮廓 + 箭头 | 青 | 复用联接点的箭头语义，靠轮廓区分 |

- 角标位置沿用现有 `ShortcutGlyphElement` 的位置：详情/列表左下、网格左下、磁贴右上、分栏左下。
- 尺寸：详情和分栏 10–12px，网格 16px，磁贴 16px；内部图形用 `Viewbox` 缩放。
- 形状优先、颜色辅助、类型文字兜底；不把颜色作为唯一编码。
- 高对比度主题只保留形状和系统前景色，不依赖自定义填充色。
- 角标设置 `IsTabStop="False"`，不拦截选择；`AutomationProperties.Name` 使用链接类型文字。
- 工具提示只显示类型（硬链接附带链接数），不在角标上异步解析目标路径。

### 4.2 类型文字

规则：`真实类型 · 链接类型`，中间使用本地化分隔符 `" · "`。

| 对象 | 类型文字 |
|---|---|
| 普通文件 | `文本文档`（不变） |
| 普通文件夹 | `文件夹`（不变） |
| `.lnk` | `快捷方式`（不变） |
| `.url` | `Internet 快捷方式`（不变） |
| 文件符号链接 | `文本文档 · 符号链接` |
| 目录符号链接 | `文件夹 · 符号链接` |
| 目录联接 | `文件夹 · 目录联接` |
| 硬链接 | `文本文档 · 硬链接` |
| 卷挂载点 | `卷挂载点` |

- 保留真实类型，避免“硬链接”把 `.txt` 之类的信息吞掉；类型列过窄时由现有 `TextTrimming` 截断，完整文字进入 `ItemTooltipText`。
- `ItemTooltipText` 已包含 `ItemType`，自动获得链接类型；有目标路径时追加一行“链接目标”。
- 排序继续按 `ItemType` 字符串；硬链接的异步更新不触发重新排序，只更新显示。这是 v1 的明确取舍。

### 4.3 属性页

新增“链接”属性页（`LinkPage`），仅在单选文件系统链接时出现：

- 链接类型：符号链接（文件/目录）、目录联接、硬链接、卷挂载点。
- 目标：只读文本框，可选中复制；符号链接显示“相对/绝对”，并显示解析后的绝对路径。
- 目标状态：存在 / 不存在 / 无权访问。
- 操作：打开目标位置、复制目标路径。
- 硬链接：显示链接数；其他路径列表推迟到 v2（`FindFirstFileNameW` 的 NT 路径转换成本较高）。

`.lnk` / `.url` 继续使用现有“快捷方式”页，不合并。

## 5. 数据模型与检测

### 5.1 枚举

新增 `src/Files.App/Data/Enums/LinkKind.cs`：

```csharp
public enum LinkKind
{
    None,
    ShellShortcut,
    InternetShortcut,
    SymbolicLink,
    Junction,
    HardLink,
    VolumeMountPoint,
}
```

`ListedItem` 增加：

- `LinkKind LinkKind { get; set; }`
- `string? LinkTargetPath { get; set; }`
- `uint? HardLinkCount { get; set; }`
- `string? BaseItemType { get; set; }`
- `FileAttributes ItemFileAttributes { get; set; }`
- `bool IsFileSystemLink => LinkKind is SymbolicLink or Junction or HardLink or VolumeMountPoint;`

新增 `LinkItem : ListedItem`，用于符号链接、junction、硬链接、卷挂载点。`LinkItem` **不实现** `IShortcutItem`，因此：

- `ListedItem.IsShortcut` 继续只对 `.lnk` / `.url` 为 true。
- “以管理员身份运行”“快捷方式属性页”等快捷方式专属逻辑不会误作用到文件系统链接。
- `Name` 使用基类逻辑，符号链接保留自己的扩展名。

`ShortcutItem` 增加 `LinkKind` 赋值：`.lnk` → `ShellShortcut`，`.url` → `InternetShortcut`。

### 5.2 枚举器

`Win32StorageEnumerator`：

- `GetFile`：`isReparsePoint && reparseTag == IO_REPARSE_TAG_SYMLINK` 时创建 `LinkItem`，`PrimaryItemAttribute = File`，`LinkKind = SymbolicLink`，类型文字用 `LinkTypeHelper.Compose`。
- `GetFolder`：检测 `dwReserved0`。`SYMLINK` → `SymbolicLink`；`MOUNT_POINT` → 读目标区分 `Junction` / `VolumeMountPoint`。创建 `LinkItem`，`PrimaryItemAttribute = Folder`。
- `.lnk` / `.url` 分支保持 `ShortcutItem`，补上 `LinkKind`。

git 工作区需要单独处理：现有代码对 git 仓库中的符号链接返回 `GitShortcutItem`（同文件 `:311-331`），保留 git 状态列和“按目标判断可执行”的语义。改为新增 `GitLinkItem : GitItem`，同样设置 `LinkKind` / `LinkTargetPath`，但**不实现** `IShortcutItem`；这样既保留 git 能力，又不会触发快捷方式专属动作。`GetFolder` 的 git 分支同样处理目录符号链接。

`FolderSearch`：

- `GetListedItemAsync(string, WIN32_FIND_DATAW)`：与 `Win32StorageEnumerator` 共用同一个 `LinkTypeHelper` / tag 判定，文件和目录都要处理。
- `GetListedItemAsync(IStorageItem)`：本地路径调用 `Win32Helper.GetWin32FindDataForPath` 补 tag；失败时不加角标，不猜测。

### 5.3 重解析点解析

把 `Win32Helper.ParseSymLink`（`Win32Helper.Storage.cs:731-767`）泛化为 `GetReparsePointInfo`：

- 返回 `Tag`、`TargetPath`、`IsRelative`。
- 对 `MOUNT_POINT` 去掉 `\??\` 前缀；对 `SYMLINK` 保留现有相对路径解析逻辑。
- 枚举阶段只对 `MOUNT_POINT` 调用（为了区分 junction 和卷挂载点）；符号链接在属性页打开时再解析目标，避免枚举时为每个链接做 IO。

### 5.4 硬链接

新增 `HardLinkHelper`：

- 只处理本地固定磁盘上的文件；目录、网络、MTP、FTP、压缩包直接返回 `null`。
- 跳过任何带 `FILE_ATTRIBUTE_REPARSE_POINT` 的文件（云占位符、未知 tag），以及 `FILE_ATTRIBUTE_OFFLINE` / `RECALL_ON_OPEN` / `RECALL_ON_DATA_ACCESS`，避免打开 OneDrive 占位符触发下载。为此在 `ListedItem` 上记录枚举时的 `FileAttributes`。
- 用 `CreateFile`（`FILE_READ_ATTRIBUTES`，共享读写删）+ `GetFileInformationByHandle` 读 `nNumberOfLinks`；`> 1` 时设置 `HardLinkCount` 和 `LinkKind.HardLink`。
- 结果按路径缓存；刷新目录时清空。缓存未命中才发 IO。

接入点：

- `BaseLayoutPage.RefreshItem`（`:1386-1406`）已经对每个实现项做延迟加载；在这里追加 `EnsureLinkInfoAsync(listedItem)`，与图标/扩展属性一样走 `EnqueueItemLoad`。
- 只对 `LinkKind.None && PrimaryItemAttribute == File` 的项执行；已有 tag 的链接不重复查询。
- 并发限制为 4；滚动中的项可以被取消/跳过，滚动停止后再补。

## 6. 性能边界

- 符号链接/junction：枚举只读 `dwReserved0`，不打开文件；`MOUNT_POINT` 的目标解析只在目录项上发生，数量很少。
- 硬链接：不在枚举阶段做；只对已实现、可见的本地文件做，带缓存和并发上限。10k 文件目录不滚动时不会一次性查询全部。
- `ItemType` 的异步更新只改显示，不触发重新排序。
- 角标控件无位图、无额外 IO；`x:Load` 依据 `LinkKind`，普通项不创建视觉树。

## 7. 影响面

新增：

- `src/Files.App/Data/Enums/LinkKind.cs`
- `src/Files.App/Data/Items/LinkItem.cs`
- `src/Files.App/Helpers/LinkTypeHelper.cs`
- `src/Files.App/Helpers/HardLinkHelper.cs`
- `src/Files.App/UserControls/LinkBadge.xaml` / `.xaml.cs`
- `src/Files.App/Views/Properties/LinkPage.xaml` / `.xaml.cs`

修改：

- `src/Files.App/Data/Items/ListedItem.cs`
- `src/Files.App/Utils/Storage/Enumerators/Win32StorageEnumerator.cs`
- `src/Files.App/Utils/Storage/Search/FolderSearch.cs`
- `src/Files.App/Helpers/Win32/Win32Helper.Storage.cs`
- `src/Files.App/ViewModels/ShellViewModel.cs`
- `src/Files.App/Views/Layouts/BaseLayoutPage.cs`
- `src/Files.App/Views/Layouts/DetailsLayoutPage.xaml`
- `src/Files.App/Views/Layouts/GridLayoutPage.xaml`（三套模板）
- `src/Files.App/Views/Layouts/ColumnLayoutPage.xaml`
- `src/Files.App.Controls/ThemedIcon/Styles/Icons.Common.xaml`
- 主题资源文件（新增 `LinkBadge.*` 画刷）
- `src/Files.App/Data/Enums/PropertiesNavigationViewItemType.cs`
- `src/Files.App/Data/Factories/PropertiesNavigationItemsFactory.cs`
- `src/Files.App/ViewModels/Properties/MainPropertiesViewModel.cs`
- `src/Files.App/ViewModels/Properties/Items/FileProperties.cs`
- `src/Files.App/ViewModels/Properties/Items/FolderProperties.cs`
- `src/Files.App/ViewModels/Properties/SelectedItemsPropertiesViewModel.cs`
- `src/Files.App/Strings/en-US/Resources.resw`
- `src/Files.App/Strings/zh-Hans/Resources.resw`
- `src/Files.Storage/NativeMethods.txt`

## 8. 边界情况

- 坏符号链接：角标和类型照常显示；属性页目标状态显示“不存在”。
- 相对符号链接：角标相同；属性页显示“相对”及解析后的绝对路径。
- UNC 目标：属性页原样显示；不做网络可达性探测。
- junction 指向另一个卷（包括卷根 `D:\`）：正常识别为 junction，不是卷挂载点。
- 卷挂载点：只有目标为 `\??\Volume{GUID}\` 形态时识别为卷挂载点。
- 未知 reparse tag、云占位符、`AppExecLink`：不加角标，避免误报。
- 回收站、Zip、FTP、MTP：v1 不识别，保持现状。
- 搜索结果的非 Win32 路径：取不到 tag 时不加角标。
- 重命名/刷新：角标随 `LinkKind` 保留；硬链接缓存清空后重新检测。
- 多选属性：不显示“链接”页，避免不同目标混在一起。

## 9. 验证矩阵

构建：

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

手工用例：

```cmd
mklink file-link.txt target.txt
mklink /D dir-link target-dir
mklink /J junction target-dir
mklink /H hard.txt target.txt
```

- 详情、列表、磁贴、网格、分栏五种布局。
- 浅色、深色、高对比度。
- 坏链接（删除目标）、相对链接、UNC 链接、跨卷 junction、卷挂载点。
- 搜索结果、重命名、刷新、10k 文件目录滚动。
- `.lnk` / `.url` 仍显示现有快捷方式箭头和“快捷方式”属性页。
- 硬链接文件仍能正常打开、复制、删除；角标只是显示层。

## 10. 我的决定（用户可推翻）

1. 角标采用“形状 + 颜色”，不是单一通用链接图标；`.lnk` 保留现有箭头。
2. 类型文字采用“真实类型 · 链接类型”，保留真实类型信息。
3. 用新的 `LinkItem` 承载文件系统链接，不再复用 `ShortcutItem`；这是“不再统一显示”的前提。
4. 新增独立“链接”属性页，不复用“快捷方式”页。
5. 硬链接角标采用惰性检测 + 缓存，不在枚举阶段做；v1 不加设置开关。
6. v1 不做创建、编辑、删除语义和“其他硬链接路径”。
7. 只补 en-US 和 zh-Hans 文案；其他语言按现有回退机制显示英文。
8. git 工作区里的符号链接保留 `GitItem` 能力，用 `GitLinkItem` 承载，不降级为普通 `LinkItem`。
9. 硬链接探测跳过所有 reparse point 文件和离线/按需召回文件，避免触发 OneDrive 下载。
10. 卷挂载点只认 `\??\Volume{GUID}\` 目标；指向卷根的 junction 仍是目录联接。
11. 实施顺序为 A（模型 + tag 判定 + 角标 + 类型文字）→ B（硬链接惰性探测）→ C（链接属性页）。三者都属于 v1；如果 B 的性能验证不通过，可以单独回退 B，不影响 A/C。

## 11. 请确认

是否批准按此设计进入实现计划？上面 11 条决定都可以推翻，尤其是硬链接同期上线、属性页纳入 v1、`.lnk` 保留现有箭头这三项。
