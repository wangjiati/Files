# 视图地址（地址栏筛选/分组/视图参数）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让地址栏显示并携带临时的筛选、分组、排序、布局参数，复制完整字符串后粘贴即可恢复当时的视图，同时不改变目录的已保存偏好。

**架构：** 新增纯逻辑的地址编解码器；地址栏文本就是「路径 + 一个空格 + `?filter=...&group=...&sort=...&layout=...`」，复用原生 `TextBox` 的复制/粘贴；导航时通过 `NavigationArguments.DirectoryFilter` 恢复筛选，通过 `LayoutPreferencesManager` 的临时覆盖层应用分组/排序/布局，覆盖不写盘。

**技术栈：** C# / .NET、WinUI 3、MSTest。

**规格：** `docs/superpowers/specs/2026-11-06-view-address-design.md`

## 全局约束

- 遵循 `.editorconfig`；改动文本文件使用 CRLF；缩进使用制表符。
- 不新增 `DllImport`/`ComImport`；需要原生能力时沿用 CsWin32 或现有封装。
- 用户可见文案走 `Resources.resw`，至少补 `en-US` 与 `zh-Hans`。
- 地址参数只做临时覆盖：不写目录偏好、不写数据库/ADS、不进路径历史。
- 不修改搜索模式、命令面板模式的既有行为。
- 构建命令：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
- 不跑并行构建；每个任务结束先构建受影响项目。

## 审查重点（Review Focus）

- **带分隔符的值**：名称/标签/正则里出现 `;`、`&`、`,`、`"` 或中文时，`Format` → `TryParse` 必须逐字节还原。
- **看起来像参数的路径**：`\\?\C:\...`、FTP URL、`tag:` 路径不能被误判成「路径 + 参数」。
- **默认复制不回归**：激活地址栏后直接 Ctrl+C 只能得到路径；Ctrl+A 后的 Ctrl+C 才得到完整地址。
- **粘贴不拼接旧参数**：粘贴带参数的地址时必须替换整段文本，不能出现两个 `?filter=`。
- **覆盖不落盘**：应用地址里的分组/排序/布局后，设置文件、布局数据库、ADS 都不能出现这些值。

---

### 任务 1：地址值对象与编解码

**文件：**
- 创建：`src/Files.App/Utils/Storage/Filtering/DirectoryViewOverrides.cs`
- 创建：`src/Files.App/Utils/Storage/Filtering/DirectoryFilterAddress.cs`
- 测试：`tests/Files.App.UnitTests/Filtering/DirectoryFilterAddressTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
[TestClass]
public sealed class DirectoryFilterAddressTests
{
	[TestMethod]
	public void TryParse_NoParameters_ReturnsPlainPath()
	{
		Assert.IsTrue(DirectoryAddressCodec.TryParse(@"C:\Work", out var address, out _));
		Assert.AreEqual(@"C:\Work", address.Path);
		Assert.IsTrue(address.Filter.IsEmpty);
		Assert.IsTrue(address.View.IsEmpty);
	}

	[TestMethod]
	public void RoundTrip_AllFilterFields()
	{
		var filter = new DirectoryFilterSpec
		{
			NameContains = "log",
			Columns =
			[
				new DirectoryColumnFilter { Column = DirectoryColumnKind.Type, Values = ["Text Document"] },
				new DirectoryColumnFilter { Column = DirectoryColumnKind.Extension, Values = [".txt"], IncludeEmpty = true },
				new DirectoryColumnFilter { Column = DirectoryColumnKind.Tags, Values = ["uid-1"] },
				new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = 10 * 1024 * 1024, MaxSize = 100 * 1024 * 1024 },
				new DirectoryColumnFilter { Column = DirectoryColumnKind.ModifiedDate, MinDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
				new DirectoryColumnFilter { Column = DirectoryColumnKind.CreatedDate, MaxDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) },
			],
		};

		var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
		Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), error.ToString());
		Assert.AreEqual(@"C:\Work", parsed.Path);
		Assert.AreEqual("log", parsed.Filter.NameContains);
		Assert.AreEqual(6, parsed.Filter.Columns.Count);
		Assert.AreEqual(DirectoryColumnKind.Type, parsed.Filter.Columns[0].Column);
		CollectionAssert.AreEqual(new[] { "Text Document" }, parsed.Filter.Columns[0].Values.ToArray());
		Assert.AreEqual(10L * 1024 * 1024, parsed.Filter.Columns[3].MinSize);
		Assert.AreEqual(100L * 1024 * 1024, parsed.Filter.Columns[3].MaxSize);
		Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), parsed.Filter.Columns[4].MinDate);
	}

	[TestMethod]
	public void RoundTrip_QuotedValuesWithSeparatorsAndChinese()
	{
		var filter = new DirectoryFilterSpec
		{
			Columns = [new DirectoryColumnFilter { Column = DirectoryColumnKind.Name, Text = "a;b&c,\"日志\"", Operator = DirectoryTextOperator.Equals }],
		};

		var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
		Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), error.ToString());
		Assert.AreEqual("a;b&c,\"日志\"", parsed.Filter.Columns[0].Text);
	}

	[TestMethod]
	public void RoundTrip_Regex()
	{
		var filter = new DirectoryFilterSpec
		{
			Regex = new DirectoryRegexOptions("^a;b$", DirectoryRegexTarget.FullPath, false),
		};

		var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
		Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out _));
		Assert.AreEqual(filter.Regex, parsed.Filter.Regex);
	}

	[TestMethod]
	public void RoundTrip_ViewOverrides()
	{
		var view = new DirectoryViewOverrides
		{
			GroupOption = GroupOption.DateModified,
			GroupDirection = SortDirection.Descending,
			GroupByDateUnit = GroupByDateUnit.Month,
			SortOption = SortOption.Size,
			SortDirection = SortDirection.Ascending,
			LayoutMode = FolderLayoutModes.ListView,
		};

		var text = DirectoryAddressCodec.Format(@"C:\Work", DirectoryFilterSpec.Empty, view);
		Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), error.ToString());
		Assert.AreEqual(view, parsed.View);
	}

	[TestMethod]
	public void TryParse_GroupWithoutDirection_LeavesDirectionNull()
	{
		Assert.IsTrue(DirectoryAddressCodec.TryParse(@"C:\Work ?group=size", out var address, out _));
		Assert.AreEqual(GroupOption.Size, address.View.GroupOption);
		Assert.IsNull(address.View.GroupDirection);
	}

	[TestMethod]
	public void TryParse_EmptyOnSize_ReturnsError()
	{
		Assert.IsFalse(DirectoryAddressCodec.TryParse(@"C:\Work ?filter=empty=size", out _, out var error));
		Assert.AreEqual(DirectoryAddressError.UnsupportedEmptyColumn, error);
	}

	[TestMethod]
	public void TryParse_DevicePath_IsTreatedAsPlainPath()
	{
		Assert.IsTrue(DirectoryAddressCodec.TryParse(@"\\?\C:\Work", out var address, out _));
		Assert.AreEqual(@"\\?\C:\Work", address.Path);
		Assert.IsTrue(address.Filter.IsEmpty);
	}

	[TestMethod]
	public void TryParse_UnknownKey_IsIgnored()
	{
		Assert.IsTrue(DirectoryAddressCodec.TryParse(@"C:\Work ?future=1&filter=name contains ""a""", out var address, out _));
		Assert.AreEqual("a", address.Filter.NameContains);
	}

	[TestMethod]
	public void TryParse_InvalidClause_ReturnsError()
	{
		Assert.IsFalse(DirectoryAddressCodec.TryParse(@"C:\Work ?filter=size >> 10MB", out _, out var error));
		Assert.AreEqual(DirectoryAddressError.InvalidOperator, error);
	}

	[TestMethod]
	public void GetPathPart_StripsParameters()
	{
		Assert.AreEqual(@"C:\Work", DirectoryAddressCodec.GetPathPart(@"C:\Work ?filter=name contains ""a"""));
		Assert.AreEqual(@"C:\Work", DirectoryAddressCodec.GetPathPart(@"C:\Work"));
	}
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter DirectoryFilterAddressTests`
预期：FAIL，类型不存在。

- [ ] **步骤 3：实现值对象与编解码器**

```csharp
public sealed record DirectoryViewOverrides
{
	public static DirectoryViewOverrides Empty { get; } = new();

	public GroupOption? GroupOption { get; init; }
	public SortDirection? GroupDirection { get; init; }
	public GroupByDateUnit? GroupByDateUnit { get; init; }
	public SortOption? SortOption { get; init; }
	public SortDirection? SortDirection { get; init; }
	public FolderLayoutModes? LayoutMode { get; init; }

	public bool IsEmpty =>
		GroupOption is null && GroupDirection is null && GroupByDateUnit is null &&
		SortOption is null && SortDirection is null && LayoutMode is null;
}

public enum DirectoryAddressError
{
	None,
	TooLong,
	MissingValue,
	InvalidQuotes,
	UnsupportedEmptyColumn,
	InvalidOperator,
	InvalidValue,
}

public sealed record DirectoryAddress(string Path, DirectoryFilterSpec Filter, DirectoryViewOverrides View)
{
	public bool HasParameters => !Filter.IsEmpty || !View.IsEmpty;
}

public static class DirectoryAddressCodec
{
	public const string ParameterPrefix = " ?";
	public const int MaxLength = 4096;

	public static bool TryParse(string text, out DirectoryAddress address, out DirectoryAddressError error);
	public static string Format(string path, DirectoryFilterSpec filter, DirectoryViewOverrides view);
	public static string FormatSuffix(DirectoryFilterSpec filter, DirectoryViewOverrides view);
	public static string GetPathPart(string text);
}
```

实现要点：

- 只识别第一个 `" ?"`；参数段里存在 `filter=`、`group=`、`sort=` 或 `layout=` 时视为地址，否则整串按路径处理。
- 参数段按 `&` 拆 key；未知 key 忽略。
- 筛选按 `;` 拆子句；值用 `""` 转义；空值/非法引号返回错误。
- `name contains` 写入 `NameContains`；其他 name 运算符写入 `Columns`。
- `type`/`ext`/`tag` 用 `in`；`empty=` 只接受 `type`/`ext`/`tag`。
- `size`/`modified`/`created` 只接受 `>=`/`<=`/`=`。
- `regex` 输出 `regex[target,i|c]=/pattern/`；解析时校验目标、标志、pattern 长度。
- 大小解析支持 `B`/`KB`/`MB`/`GB`/`TB`（1024 进制）；输出用最紧凑的整数或一位小数形式。
- 日期用 `DateTimeOffset.ToString("O")` 输出，解析用 `CultureInfo.InvariantCulture`。
- 视图 token（稳定英文，大小写不敏感）：
  - `group`：`none|name|modified|created|size|type|sync|tag|folder|deleted|path`，可跟 `,asc|desc` 和 `,day|month|year`。
  - `sort`：同 `group` 去掉 `none`，可跟 `,asc|desc`。
  - `layout`：`details|list|cards|columns|grid`；`adaptive` 不写入地址，解析到 `adaptive` 时忽略。
- `FormatSuffix` 返回**包含前导 `?`** 的参数段，顺序 `filter` → `group` → `sort` → `layout`；`Format` 在后缀非空时用单个空格连接路径与后缀。
- `GetPathPart`：可识别参数段时返回分隔符之前的路径，否则原样返回；供补全和路径解析使用。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter DirectoryFilterAddressTests`
预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/Utils/Storage/Filtering/DirectoryFilterAddress.cs src/Files.App/Utils/Storage/Filtering/DirectoryViewOverrides.cs tests/Files.App.UnitTests/Filtering/DirectoryFilterAddressTests.cs
git commit -m "Add view address codec"
```

---

### 任务 2：工具栏合成、默认选区、提交与粘贴

**文件：**
- 修改：`src/Files.App.Controls/Omnibar/Omnibar.cs`
- 修改：`src/Files.App.Controls/Omnibar/Omnibar.Events.cs`
- 修改：`src/Files.App/UserControls/NavigationToolbar.xaml.cs`
- 修改：`src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs`
- 修改：`src/Files.App/Strings/en-US/Resources.resw`
- 修改：`src/Files.App/Strings/zh-Hans/Resources.resw`

- [ ] **步骤 1：Omnibar 暴露两个小入口**

```csharp
public void SelectTextRange(int start, int length)
{
	var textLength = _textBox.Text.Length;
	start = Math.Clamp(start, 0, textLength);
	length = Math.Clamp(length, 0, textLength - start);
	_textBox.Select(start, length);
}

public event EventHandler<OmnibarPasteRequestedEventArgs>? PasteRequested;
```

```csharp
public sealed class OmnibarPasteRequestedEventArgs(string text) : EventArgs
{
	public string Text { get; } = text;
}
```

在 `OnApplyTemplate` 里订阅 `_textBox.Paste`：读取剪贴板文本（先检查 `StandardDataFormats.Text`），`e.Handled = true`，在 UI 线程触发 `PasteRequested`。

- [ ] **步骤 2：合成地址与默认选区**

`NavigationToolbar.xaml.cs`：

```csharp
private string GetDefaultPathText()
{
	var shellPage = ContentPageContext.ShellPage;
	var shellViewModel = shellPage?.ShellViewModel;
	if (shellPage is null || shellViewModel is null)
		return Constants.UserEnvironmentPaths.HomePath;

	var path = string.IsNullOrEmpty(shellViewModel.WorkingDirectory)
		? Constants.UserEnvironmentPaths.HomePath
		: shellViewModel.WorkingDirectory;

	return DirectoryAddressCodec.Format(path, shellViewModel.DirectoryFilter, ViewModel?.GetViewOverrides(shellPage) ?? DirectoryViewOverrides.Empty);
}

// 放到 NavigationToolbarViewModel，供工具栏和摘要共用。
public DirectoryViewOverrides GetViewOverrides(IShellPage shellPage)
{
	var folder = shellPage.InstanceViewModel.FolderSettings;
	var defaults = UserSettingsService.LayoutSettingsService;
	var overrides = DirectoryViewOverrides.Empty;

	if (folder.DirectoryGroupOption != defaults.DefaultGroupOption ||
		folder.DirectoryGroupDirection != defaults.DefaultDirectoryGroupDirection ||
		folder.DirectoryGroupByDateUnit != defaults.DefaultGroupByDateUnit)
	{
		overrides = overrides with
		{
			GroupOption = folder.DirectoryGroupOption,
			GroupDirection = folder.DirectoryGroupDirection,
			GroupByDateUnit = folder.DirectoryGroupByDateUnit,
		};
	}

	if (folder.DirectorySortOption != defaults.DefaultSortOption ||
		folder.DirectorySortDirection != defaults.DefaultDirectorySortDirection)
	{
		overrides = overrides with
		{
			SortOption = folder.DirectorySortOption,
			SortDirection = folder.DirectorySortDirection,
		};
	}

	if (!folder.IsAdaptiveLayoutEnabled &&
		folder.LayoutMode != defaults.DefaultLayoutMode &&
		folder.LayoutMode != FolderLayoutModes.Adaptive)
		overrides = overrides with { LayoutMode = folder.LayoutMode };

	return overrides;
}
```

在 `Omnibar_ModeChanged` 与 `Omnibar_IsFocusedChanged` 里，设置 `PathText` 之后：

```csharp
var pathLength = DirectoryAddressCodec.GetPathPart(viewModel.PathText ?? string.Empty).Length;
Omnibar.SelectTextRange(0, pathLength);
```

- [ ] **步骤 3：提交时解析**

`Omnibar_QuerySubmitted` 的路径分支：

```csharp
if (!DirectoryAddressCodec.TryParse(args.Text, out var address, out _))
{
	await DialogDisplayHelper.ShowDialogAsync(
		Strings.FilterAddressErrorTitle.GetLocalizedResource(),
		Strings.FilterAddressErrorContent.GetLocalizedResource());
	return;
}

if (!await viewModel.HandleItemNavigationAsync(address))
{
	RestoreOmnibarText(viewModel, OmnibarPathMode, args.Text);
	return;
}
```

`NavigationToolbarViewModel` 增加：

```csharp
public Task<bool> HandleItemNavigationAsync(DirectoryAddress address)
	=> HandleItemNavigationAsync(address.Path, address.Filter, address.View);

private async Task<bool> HandleItemNavigationAsync(string path, DirectoryFilterSpec filter, DirectoryViewOverrides view)
```

保留现有 `HandleItemNavigationAsync(string path)` 作为 `DirectoryFilterSpec.Empty` + `DirectoryViewOverrides.Empty` 的包装。所有 `shellPage.NavigateToPath(...)` 调用补上参数：

```csharp
shellPage.NavigateToPath(pathToNavigate, new NavigationArguments
{
	DirectoryFilter = filter.IsEmpty ? null : filter,
	ViewOverrides = view.IsEmpty ? null : view,
});
```

- [ ] **步骤 4：补全只用路径部分**

`PopulateOmnibarSuggestionsForPathMode` 开头：

```csharp
var pathText = DirectoryAddressCodec.GetPathPart(this.PathText ?? string.Empty);
```

路径历史仍只保存纯路径（`SavePathToHistory` 不改）。

- [ ] **步骤 5：粘贴替换**

订阅 `Omnibar.PasteRequested`：

```csharp
if (DirectoryAddressCodec.TryParse(args.Text, out var pasted, out _) && pasted.HasParameters)
{
	viewModel.PathText = DirectoryAddressCodec.Format(pasted.Path, pasted.Filter, pasted.View);
	Omnibar.SelectTextRange(0, pasted.Path.Length);
}
else
{
	// 普通文本也替换整段：地址栏粘贴通常就是整段替换，避免与旧参数拼接。
	viewModel.PathText = args.Text;
	Omnibar.SelectTextRange(0, args.Text.Length);
}
```

- [ ] **步骤 6：新增错误文案**

`en-US`：

```xml
<data name="FilterAddressErrorTitle" xml:space="preserve"><value>Invalid address</value></data>
<data name="FilterAddressErrorContent" xml:space="preserve"><value>The filter or view part of this address is not valid. Fix it or remove everything after the space and question mark.</value></data>
```

`zh-Hans`：

```xml
<data name="FilterAddressErrorTitle" xml:space="preserve"><value>地址无效</value></data>
<data name="FilterAddressErrorContent" xml:space="preserve"><value>地址中的筛选或视图参数无法解析。请修正，或删除空格和问号之后的全部内容。</value></data>
```

- [ ] **步骤 7：构建并 Commit**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
git add src/Files.App.Controls/Omnibar src/Files.App/UserControls/NavigationToolbar.xaml.cs src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs src/Files.App/Strings/en-US/Resources.resw src/Files.App/Strings/zh-Hans/Resources.resw
git commit -m "Navigate from view addresses"
```

---

### 任务 3：临时视图覆盖层（不落盘）

**文件：**
- 修改：`src/Files.App/Data/EventArguments/NavigationArguments.cs`
- 修改：`src/Files.App/Helpers/Layout/LayoutPreferencesManager.cs`
- 修改：`src/Files.App/Views/Shells/BaseShellPage.cs`
- 修改：`src/Files.App/Views/Layouts/BaseLayoutPage.cs`

- [ ] **步骤 1：导航参数增加覆盖**

```csharp
/// <summary>
/// Transient group/sort/layout overrides carried by a view address. Never persisted.
/// </summary>
public DirectoryViewOverrides? ViewOverrides { get; set; }
```

- [ ] **步骤 2：覆盖层**

`LayoutPreferencesManager` 增加：

```csharp
private DirectoryViewOverrides transientOverrides = DirectoryViewOverrides.Empty;

public DirectoryViewOverrides GetTransientOverrides() => transientOverrides;

public void ApplyTransientOverrides(DirectoryViewOverrides overrides)
{
	transientOverrides = overrides ?? DirectoryViewOverrides.Empty;
	OnPropertyChanged(nameof(LayoutMode));
	OnPropertyChanged(nameof(DirectorySortOption));
	OnPropertyChanged(nameof(DirectorySortDirection));
	OnPropertyChanged(nameof(DirectoryGroupOption));
	OnPropertyChanged(nameof(DirectoryGroupDirection));
	OnPropertyChanged(nameof(DirectoryGroupByDateUnit));
	SortOptionPreferenceUpdated?.Invoke(this, DirectorySortOption);
	SortDirectionPreferenceUpdated?.Invoke(this, DirectorySortDirection);
	GroupOptionPreferenceUpdated?.Invoke(this, DirectoryGroupOption);
	GroupDirectionPreferenceUpdated?.Invoke(this, DirectoryGroupDirection);
	GroupByDateUnitPreferenceUpdated?.Invoke(this, DirectoryGroupByDateUnit);
}
```

- 各 getter 改为 `transientOverrides.X ?? LayoutPreferencesItem.X`。
- 各 setter 在写入前清掉对应覆盖：

```csharp
private void ClearTransientOverride(Func<DirectoryViewOverrides, DirectoryViewOverrides> update, string propertyName, Action? notify)
{
	var next = update(transientOverrides);
	if (next == transientOverrides)
		return;

	transientOverrides = next;
	OnPropertyChanged(propertyName);
	notify?.Invoke();
}
```

例如排序 setter 开头：

```csharp
ClearTransientOverride(o => o with { SortOption = null }, nameof(DirectorySortOption),
	() => SortOptionPreferenceUpdated?.Invoke(this, DirectorySortOption));
```

- `ApplyTransientOverrides` 不触发 `LayoutPreferencesUpdateRequired`，因此不会写库。
- `ReloadGroupAndSortPreferences` 先清空 `transientOverrides`，避免旧覆盖影响其他窗格的同步刷新。

- [ ] **步骤 3：布局类型选择考虑覆盖**

`GetLayoutType` 增加可选参数：

```csharp
public Type GetLayoutType(string? path, DirectoryViewOverrides? viewOverrides = null, bool changeLayoutMode = true)
```

- 用 `viewOverrides?.LayoutMode ?? preferencesItem.LayoutMode` 选择页类型。
- 只有 `viewOverrides?.LayoutMode is null` 时才走现有的 adaptive 预测。
- `BaseShellPage.NavigateToPath(string navigationPath, NavigationArguments? navArgs)` 调用 `GetLayoutType` 时传入 `navArgs?.ViewOverrides`；在真正导航之前先调用 `FolderSettings.ApplyTransientOverrides(navArgs?.ViewOverrides ?? DirectoryViewOverrides.Empty)`，保证页面构造时读到的就是覆盖后的视图。`OnNavigatedTo` 里再应用一次，覆盖前进/后退路径。

- [ ] **步骤 4：保存与恢复**

`BaseLayoutPage.OnNavigatedFrom`：

```csharp
navigationArguments.DirectoryFilter = shellViewModel.DirectoryFilter.IsEmpty ? null : shellViewModel.DirectoryFilter;
navigationArguments.ViewOverrides = ParentShellPageInstance.InstanceViewModel.FolderSettings.GetTransientOverrides() is { IsEmpty: false } overrides
	? overrides
	: null;
```

`BaseLayoutPage.OnNavigatedTo`，在恢复筛选之后：

```csharp
parentShellPage.InstanceViewModel.FolderSettings.ApplyTransientOverrides(args.ViewOverrides ?? DirectoryViewOverrides.Empty);
```

`SetWorkingDirectoryAsync` 切换目录时会清空筛选；覆盖层由 `OnNavigatedTo` 重新应用或清空。

- [ ] **步骤 5：验证不落盘**

手工步骤：

1. 在 `D:\Temp` 打开地址 `D:\Temp ?group=size,desc&sort=name,asc&layout=list`。
2. 关闭并重开应用，再打开 `D:\Temp`，确认分组/排序/布局恢复为目录原来的偏好。
3. 检查 `%LOCALAPPDATA%\Files\settings.json` 与布局数据库没有出现地址里的值。

- [ ] **步骤 6：构建并 Commit**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
git add src/Files.App/Data/EventArguments/NavigationArguments.cs src/Files.App/Helpers/Layout/LayoutPreferencesManager.cs src/Files.App/Views/Shells/BaseShellPage.cs src/Files.App/Views/Layouts/BaseLayoutPage.cs
git commit -m "Apply transient view overrides from addresses"
```

---

### 任务 4：未激活状态的面包屑摘要

**文件：**
- 修改：`src/Files.App/UserControls/NavigationToolbar.xaml`
- 修改：`src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs`
- 修改：`src/Files.App/Views/Shells/BaseShellPage.cs`
- 修改：`src/Files.App/Strings/en-US/Resources.resw`
- 修改：`src/Files.App/Strings/zh-Hans/Resources.resw`

- [ ] **步骤 1：视图模型增加摘要**

```csharp
private string _PathSuffixSummary = string.Empty;
public string PathSuffixSummary { get => _PathSuffixSummary; set => SetProperty(ref _PathSuffixSummary, value); }

public void RefreshAddressSuffix()
{
	var shellPage = ContentPageContext.ShellPage;
	var shellViewModel = shellPage?.ShellViewModel;
	if (shellPage is null || shellViewModel is null)
	{
		PathSuffixSummary = string.Empty;
		return;
	}

	var parts = new List<string>(2);
	if (!shellViewModel.DirectoryFilter.IsEmpty)
		parts.Add(Strings.FilterAddressSummary.GetLocalizedResource());
	if (!GetViewOverrides(shellPage).IsEmpty)
		parts.Add(Strings.ViewAddressSummary.GetLocalizedResource());

	PathSuffixSummary = string.Join(" · ", parts);
}
```

- [ ] **步骤 2：面包屑旁显示暗色摘要**

把 `BreadcrumbBar` 包进 `Grid`，右侧增加：

```xml
<TextBlock
	Margin="8,0,12,0"
	VerticalAlignment="Center"
	Foreground="{ThemeResource TextFillColorSecondaryBrush}"
	Text="{x:Bind ViewModel.PathSuffixSummary, Mode=OneWay}"
	TextTrimming="CharacterEllipsis" />
```

- [ ] **步骤 3：刷新时机**

- `BaseShellPage.UpdatePathUIToWorkingDirectoryAsync` 完成后调用 `ToolbarViewModel.RefreshAddressSuffix()`。
- `BaseShellPage` 订阅 `ShellViewModel.PropertyChanged`，在 `DirectoryFilter` 变化时调用 `ToolbarViewModel.RefreshAddressSuffix()`。
- `BaseLayoutPage.OnNavigatedTo` 在 `ApplyTransientOverrides` 之后调用 `ParentShellPageInstance.ToolbarViewModel.RefreshAddressSuffix()`。
- [ ] **步骤 4：新增摘要文案**

`en-US`：

```xml
<data name="FilterAddressSummary" xml:space="preserve"><value>Filtered</value></data>
<data name="ViewAddressSummary" xml:space="preserve"><value>Custom view</value></data>
```

`zh-Hans`：

```xml
<data name="FilterAddressSummary" xml:space="preserve"><value>已筛选</value></data>
<data name="ViewAddressSummary" xml:space="preserve"><value>自定义视图</value></data>
```

- [ ] **步骤 5：构建并 Commit**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
git add src/Files.App/UserControls/NavigationToolbar.xaml src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs src/Files.App/Views/Shells/BaseShellPage.cs src/Files.App/Views/Layouts/BaseLayoutPage.cs src/Files.App/Strings/en-US/Resources.resw src/Files.App/Strings/zh-Hans/Resources.resw
git commit -m "Show a dim view summary in the breadcrumb"
```

---

### 任务 5：整体验证

**文件：** 无新增。

- [ ] **步骤 1：构建**

```powershell
msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

预期：无 error。

- [ ] **步骤 2：运行编解码单元测试**

```powershell
dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter DirectoryFilterAddressTests
```

预期：全部 PASS。若测试运行器不可用，记录该事实并以构建通过为准。

- [ ] **步骤 3：手工回归**

1. 打开有筛选的目录，Ctrl+L，Ctrl+C → 纯路径。
2. Ctrl+L，Ctrl+A，Ctrl+C → 完整地址（含筛选/分组/排序/布局）。
3. 新窗口粘贴完整地址并回车 → 视图一致。
4. 粘贴裸路径 → 筛选/视图覆盖被清空。
5. 粘到记事本再粘回 → 结果一致（含中文与引号）。
6. 打开 `D:\Temp ?filter=bogus` → 报错且不导航。
7. 前进/后退到带参数的条目 → 参数恢复；重启后不保留。
8. 应用地址里的分组/排序后，检查设置文件与布局数据库没有新增这些值。

- [ ] **步骤 4：提交收尾**

```powershell
git status --short
git diff --check
```

只提交本功能相关文件；如有用户未提交的改动，保持不动。
