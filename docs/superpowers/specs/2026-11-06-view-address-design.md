# 视图地址（地址栏中的临时视图参数）设计

日期：2026-11-06
状态：待批准

## 1. 意图与背景

用户想要一个「带筛选的地址」：地址栏显示「路径 + 筛选/分组/视图」，复制整串后，粘贴到地址栏即可恢复当时的视图。

现状是相反的：列筛选只是 `ShellViewModel.DirectoryFilter` 的临时状态，切换目录即清空（`src/Files.App/ViewModels/ShellViewModel.cs:268-272`），只有前进/后退时通过 `NavigationArguments.DirectoryFilter` 传回；排序、分组、布局则按目录持久化在 `LayoutPreferencesManager` 里。两者都没有「可携带的地址」概念。

本设计把地址扩展成「路径 + 临时视图参数」，但**不改变目录的已保存偏好**，也不新增任何持久化。普通用户不使用这个交互时，地址栏和现在完全一样。

## 2. 已确认的产品决定

以下来自与用户的对话，属于已定内容：

1. **显式模型**：只有带参数的地址才带筛选；打开裸路径不带。
2. **地址形态**：路径与参数之间一个空格，参数段以 `?` 开头，例如 `D:\food ?filter=...`。
3. **默认选择**：激活地址栏时只选中路径，不全选；用户自己 Ctrl+A 后再 Ctrl+C 才复制完整字符串。
4. **两种颜色**：路径保持当前前景色；筛选/分组/视图参数用更暗的次要色。
5. **临时性**：地址里的参数只影响本次查看，不写回目录偏好，不进入设置，不持久化。
6. **显式显示**：参数必须能在地址栏里看见，不能是隐形的隐藏状态。

## 3. 语义模型

地址由两部分组成：

- `Path`：真实路径，行为与今天完全一致。
- `ViewOverrides`：本次查看的临时覆盖，可包含筛选、分组、排序、布局。

应用顺序固定为：**目录已保存偏好 → 地址覆盖 → 视图渲染**。

规则：

- 地址覆盖的生命周期与导航条目一致：当前页面、以及前进/后退回到该条目时恢复（沿用 `NavigationArguments.DirectoryFilter` 的现有行为）；关闭标签或重启即丢弃。
- 前进/后退恢复该条目的完整地址状态（筛选 + 分组/排序/布局覆盖），但不写回目录偏好。
- 地址覆盖不写回 `LayoutPreferencesManager`，不写数据库，不写 ADS。
- 用户之后通过 UI 自己改分组/排序/布局，仍走现有持久化逻辑；该维度的地址覆盖随之失效。
- 筛选本身没有持久化，因此**筛选总是出现在地址里**；分组/排序/布局只在它们与全局默认不同时出现。这样默认视图的地址保持干净，非默认视图又能完整复制。
- 分组/排序/布局和筛选一样是临时覆盖，不写回目录偏好。
- 打开带参数的地址后，地址栏继续显示这些参数，直到用户清空或离开。

## 4. 地址语法

```text
address   := path [ SP "?" params ]
params    := param *( "&" param )
param     := "filter=" filter | "group=" group | "sort=" sort | "layout=" layout
```

### 4.1 筛选

```text
filter    := clause *( ";" clause )
clause    := column SP op SP value
           | "empty=" emptyColumn *( "," emptyColumn )
           | "regex" [ "[" regexTarget *( "," regexFlag ) "]" ] "=" "/" pattern "/"
column    := "name" | "type" | "ext" | "tag" | "size" | "modified" | "created"
op        := "contains" | "notcontains" | "startswith" | "endswith" | "equals"
           | "in" | ">=" | "<=" | "="
value     := quoted | list
list      := quoted *( "," quoted )
quoted    := '"' *( char / '""' ) '"'
emptyColumn := "type" | "ext" | "tag"
regexTarget := "rawname" | "displayname" | "fullpath"
regexFlag := "i" | "c"
```

- `name` 的 `contains` 对应现有 `NameContains`（名称框），其余运算符对应 `DirectoryColumnFilter`。
- `type`/`ext`/`tag` 使用 `in` 加列表；`empty` 表示 `IncludeEmpty`。
- `empty=` 只允许 `type`/`ext`/`tag`：求值器只在这三列读取 `IncludeEmpty`。其他列出现 `empty=` 视为语法错误，不静默忽略。
- `size` 值为字节数或带单位（`10MB`、`1.5GB`），内部归一化为字节。
- `size`/`modified`/`created` 只接受 `>=`、`<=`、`=`：模型只有含端点的 `MinSize`/`MaxSize`、`MinDate`/`MaxDate`，不引入严格大于/小于的近似语义。文本运算符只用于 `name`，`in` 只用于 `type`/`ext`/`tag`。
- `modified`/`created` 使用 ISO 8601（`2026-01-01` 或 `2026-01-01T10:30:00Z`）。
- `regex` 支持可选目标与标志：`regex[fullpath,i]=/pattern/`。`i` 表示忽略大小写，`c` 表示区分大小写；省略标志时默认 `i`（与界面默认一致），但规范形式总是写出有效标志，保证复制再粘贴不改变语义。
- `tag` 值使用标签 UID（稳定、无歧义）；`type` 值使用当前 `ListedItem.ItemType` 字符串（与筛选模型一致），因此跨界面语言不保证一致。标签名只用于未激活摘要和 Tooltip。
- 值里的 `"` 用 `""` 转义；分隔符放在引号内即为普通字符。

### 4.2 分组、排序、布局

```text
group     := groupOption [ "," direction [ "," unit ] ]
sort      := sortOption "," direction
layout    := "details" | "list" | "cards" | "columns" | "grid" | "adaptive"
direction := "asc" | "desc"
unit      := "day" | "month" | "year"
```

- 选项词使用稳定的英文 token，不随界面语言变化。
- `group=none` 表示显式关闭分组。
- 未知 key、未知枚举、当前布局不支持的分组一律降级忽略，不阻断导航。

### 4.3 规范形式

- key 顺序固定：`filter` → `group` → `sort` → `layout`。
- 筛选子句顺序固定：`name` → `type` → `ext` → `tag` → `size` → `modified` → `created` → `empty` → `regex`。
- 值尽量短：数字不带前导零，日期用最短可往返形式，布尔用存在性表达。
- 整串上限 4096 字符；筛选子句上限 64 条；正则长度上限沿用现有约束。

## 5. 交互设计

### 5.1 激活与选择

- 激活地址栏（Ctrl+L、Alt+D、点击）时，编辑框内容为完整地址（`Path` + 一个空格 + `?参数`），但默认只选中 `Path` 部分。
- 默认只选中 `Path`；参数部分不进入默认选区，Ctrl+C 只得到路径。
- 用户按 Ctrl+A 时，`TextBox` 原生全选，Ctrl+C 得到完整地址。
- 不需要额外的复制态标志：选区本身就是唯一的状态。

### 5.2 渲染

- 编辑态使用单一 `TextBox`：路径和参数同色，原生选区、输入法、撤销、复制粘贴全部保留。WinUI `TextBox` 不支持区间着色；逐字符双色需要换掉核心编辑器或引入测量覆盖层，成本与风险不成比例。
- 未激活状态：面包屑保持现状；存在筛选时，在面包屑右侧追加一段暗色摘要（例如 `筛选 2 项`，`TextFillColorSecondaryBrush`），Tooltip 显示完整地址。无筛选时不显示任何新元素。
- 因此「两种颜色」出现在用户阅读地址的常态（未激活）里，而不是编辑态。

### 5.3 复制与粘贴

- 复制：完全走 `TextBox` 原生行为。默认只选中路径，Ctrl+C 得到纯路径；Ctrl+A 后 Ctrl+C 得到完整地址。不做自定义复制、不做右键菜单、不做复制态状态机。
- 粘贴：默认走原生粘贴，插入到当前选区。唯一例外：当剪贴板文本包含可识别的参数段时，先全选再替换，避免「新地址 + 旧参数」拼成非法串。
- 粘贴到其他应用：完整字符串不是合法 Windows 路径，这是显式模型接受的边界，不做特殊兼容。

### 5.4 提交

- Enter 提交时先做一次地址解析：
  - 没有参数段：按现有路径逻辑导航，清空筛选覆盖。
  - 有合法参数段：导航到 `Path`，把筛选作为 `NavigationArguments.DirectoryFilter` 传入，把分组/排序/布局作为临时覆盖传入。
  - 参数段存在但语法错误：弹出错误对话框，不导航。静默丢弃筛选比报错更糟。
- 路径自动补全与路径历史只使用 `Path`，参数不参与路径解析。

### 5.5 错误与空值

- `filter=` 为空表示没有筛选，等同于不写该参数。
- 空列表、空引号值视为非法子句，报错。
- 参数段以 `?` 开头但后面没有可识别的 key：整串按普通路径处理，保持向后兼容。

## 6. 集成点

| 位置 | 责任 |
| --- | --- |
| 新增 `DirectoryFilterAddress.cs` | 地址编解码：`TryParse`、`Format`、`FormatSuffix`、`GetPathPart` |
| 新增 `DirectoryViewOverrides.cs` | 分组/排序/布局的临时覆盖模型 |
| `src/Files.App.Controls/Omnibar/Omnibar.cs`、`Omnibar.Events.cs` | 激活时选中路径；粘贴含参数时替换全文 |
| `src/Files.App/UserControls/NavigationToolbar.xaml(.cs)` | 合成地址、选中路径、提交时解析 |
| `src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs` | `PathText` 是完整地址；提交/补全前剥离参数；按有效视图生成参数 |
| `src/Files.App/Data/EventArguments/NavigationArguments.cs` | 增加临时视图覆盖参数 |
| `src/Files.App/Views/Shells/BaseShellPage.cs` | 布局类型选择携带覆盖；面包屑摘要；清除已失效的覆盖 |
| `src/Files.App/Views/Layouts/BaseLayoutPage.cs` | 筛选恢复沿用现有通路；应用视图覆盖 |
| `src/Files.App/Helpers/Layout/LayoutPreferencesManager.cs` | 临时覆盖层，初始应用不触发持久化 |
| `src/Files.App/ViewModels/ShellViewModel.cs` | 保存/重置视图覆盖 |
| `src/Files.App/Strings/zh-Hans/Resources.resw` 等 | 错误提示、摘要、Tooltip |

## 7. 非目标

- 不把参数写入目录偏好、布局数据库、ADS、路径历史或标签恢复参数。
- 不承诺完整字符串能被 Explorer、运行框、终端识别。
- 不把搜索模式、命令面板模式改成两种颜色。
- 不把列宽、列顺序、缩略图大小放进地址（字符串会失控）。
- v1 不做编辑态逐字符双色，也不做自定义复制/粘贴状态机。
- 不把 group/sort/layout 写进目录偏好；它们只作为地址覆盖存在。

## 8. 风险与权衡

1. **编辑态双色**：WinUI `TextBox` 不支持区间着色（`TextHighlighter` 只适用于 `TextBlock`/`RichTextBlock`）。v1 放弃编辑态双色，只保留未激活面包屑摘要；若后续必须编辑态双色，再单独评估 RichEditBox 或双字段方案。
2. **参数可被编辑**：单一 `TextBox` 里用户可能改坏参数。提交时严格解析并报错即可；这比引入只读后缀和自定义复制/粘贴更实用。
3. **临时覆盖与持久化的边界**：`LayoutPreferencesManager` 的 setter 会立即持久化。覆盖层必须独立于 `LayoutPreferencesItem`，并保证初始应用不落盘，否则「临时」失效。
4. **地址歧义**：`?` 在 Windows 文件名中非法，因此 `" ?"` 是安全的本地路径分隔符；但 `\\?\` 设备路径、FTP URL 需要单独判定，必要时直接按普通路径处理。
5. **可发现性**：默认只选路径意味着用户可能不知道参数存在。未激活态的面包屑摘要和 Tooltip 是主要的可发现性手段。

## 9. 测试策略

- 编解码单元测试：每种筛选字段、每种视图覆盖的往返；引号/转义、中文值、空值、重复子句、非法输入、长度上限。
- 规范形式测试：同样输入必须得到同样的字符串（顺序、数字、日期）。
- 地址合成测试：路径含空格、`\\?\`、UNC、`tag:`、FTP。
- 建议剥离测试：`GetPathPart` 只返回路径部分。
- 视图覆盖测试：覆盖不写回、用户后续修改清掉覆盖、离开页面丢弃。
- 导航集成手工验证：复制 → 新窗口粘贴 → 筛选/分组/视图一致；重启后不保留。

## 10. 交付顺序

- **任务 1**：编解码（筛选 + 视图参数）与单元测试。
- **任务 2**：地址合成、默认选区、提交解析、粘贴替换。
- **任务 3**：分组/排序/布局的临时覆盖层与持久化抑制。
- **任务 4**：未激活摘要与文案。
- **任务 5**：构建与手工回归。

全部任务在同一个版本内交付，不拆分发布。
