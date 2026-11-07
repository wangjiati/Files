# 子版本号与更新检查 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 用四段版本号同时表达上游版本和内部迭代号，并在启动时每日检查一次 GitHub Release，发现新版时弹窗提示、可跳转下载或跳过。

**架构：** 纯逻辑拆成可单测的版本解析、Release JSON 解析和每日检查策略；`GitHubUpdateService` 实现现有 `IUpdateService`，复用已有启动钩子、工具栏入口和 `DynamicDialog`；版本号只改 `Files.App.csproj` 与 `Package.appxmanifest` 两处。

**技术栈：** C#/.NET 10、WinUI 3 (WindowsAppSDK)、MSTest、`System.Text.Json.JsonDocument`（AOT 安全）、现有 `DynamicDialog` / `Launcher` / `ILocalStateStore`。

**规格：** `docs/superpowers/specs/2026-11-07-versioning-and-update-check-design.md`（执行者必读）

## 全局约束

- 构建：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`；不并行构建；msbuild 不可用时进 VS DevShell。
- 单测：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~Files.App.UnitTests.Update"`（MSTest，`[TestClass]/[TestMethod]`）。
- 用户可见字符串只补 `src/Files.App/Strings/en-US/Resources.resw` 和 `zh-Hans/Resources.resw`；其他语言沿用回退。XAML 用 `{helpers:ResourceString}`，代码用 `Strings.Xxx.GetLocalizedResource()`。
- 单测不得访问网络；HTTP 用 `HttpMessageHandler` 桩。
- 不新增依赖；JSON 只用 `JsonDocument`，不用反射式 `JsonSerializer`；不引入 Markdown 渲染控件。
- 版本号只改 `Files.App.csproj` 和 `Package.appxmanifest`；不新增 props/脚本。
- 目标版本：`4.3.1.1`。
- 保持 CRLF、遵循 `.editorconfig`；只动必要文件。
- 不做资产选择、ETag、自动下载或 Release 自动发布。

## 审查重点（Review Focus）

1. **旧标签或非标准标签**：`v4.3.1.0-portable`、`4.3.1`、带前缀的标签必须仍能解析出版本；否则升级后所有历史 Release 都被判成"无更新" → 任务 1 的解析测试。
2. **网络失败**：离线、403、429、5xx、超时、非法 JSON 必须静默结束、不阻塞启动、当天不重复请求 → 任务 4 的失败测试。
3. **关闭开关**：用户关闭"检查更新"后必须零请求 → 任务 4 的 `Disabled_DoesNotRequest`。
4. **跳过语义**：点"以后再说"后同版本不再弹，但更高版本必须恢复提示 → 任务 3/4 的跳过测试 + 任务 6 手工验证。
5. **版本一致性**：便携版读程序集版本、MSIX 读包版本，两者必须一致；旧的 `4.3.1.0` 必须能判出 `4.3.1.1` 是更新 → 任务 1 的比较测试 + 现有 `AppRuntimeEnvironmentTests.Portable_VersionMatchesThePackageManifest` + 任务 7 手工验证。

---

### 任务 1：版本号解析与比较

**文件：**
- 创建：`src/Files.App/Services/Update/UpdateVersionHelper.cs`
- 测试：`tests/Files.App.UnitTests/Update/UpdateVersionHelperTests.cs`

- [ ] **步骤 1：写失败测试**

```csharp
[TestMethod]
public void TryParseVersion_AcceptsFourPartTag()
{
    Assert.IsTrue(UpdateVersionHelper.TryParseVersion("v4.3.1.7", out var version));
    Assert.AreEqual(new Version(4, 3, 1, 7), version);
}

[TestMethod]
public void TryParseVersion_PadsThreePartTag()
{
    Assert.IsTrue(UpdateVersionHelper.TryParseVersion("4.3.1", out var version));
    Assert.AreEqual(new Version(4, 3, 1, 0), version);
}

[TestMethod]
public void TryParseVersion_AcceptsHistoricalSuffix()
{
    Assert.IsTrue(UpdateVersionHelper.TryParseVersion("v4.3.1.0-portable", out var version));
    Assert.AreEqual(new Version(4, 3, 1, 0), version);
}

[TestMethod]
public void TryParseVersion_RejectsInvalidText()
{
    Assert.IsFalse(UpdateVersionHelper.TryParseVersion(null, out _));
    Assert.IsFalse(UpdateVersionHelper.TryParseVersion("v4", out _));
    Assert.IsFalse(UpdateVersionHelper.TryParseVersion("not-a-version", out _));
}

[TestMethod]
public void IsNewer_ComparesNumerically()
{
    Assert.IsTrue(UpdateVersionHelper.IsNewer(new Version(4, 3, 1, 7), new Version(4, 3, 1, 0)));
    Assert.IsTrue(UpdateVersionHelper.IsNewer(new Version(4, 4, 0, 1), new Version(4, 3, 1, 99)));
    Assert.IsFalse(UpdateVersionHelper.IsNewer(new Version(4, 3, 1, 0), new Version(4, 3, 1, 0)));
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~Files.App.UnitTests.Update"`
预期：FAIL，`UpdateVersionHelper` 不存在。

- [ ] **步骤 3：实现 `UpdateVersionHelper`**

```csharp
public static bool TryParseVersion(string? text, out Version version);
public static bool IsNewer(Version candidate, Version current);
```

规则：

- 用 `Regex.Match(text, @"v?(\d+(?:\.\d+){2,3})")` 提取第一段版本，匹配失败返回 `false`。
- 按 `.` 拆分，超过 4 段只取前 4 段；不足 4 段补 0。
- `IsNewer` 返回 `candidate > current`（`System.Version` 比较）。
- 正则用 `RegexOptions.CultureInvariant`，不设置超时以外的特殊选项；模式简单，无需 `Compiled`。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~Files.App.UnitTests.Update"`
预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/Services/Update/UpdateVersionHelper.cs tests/Files.App.UnitTests/Update/UpdateVersionHelperTests.cs
git commit -m "Add update version parsing"
```

### 任务 2：Release JSON 解析与说明截断

**文件：**
- 创建：`src/Files.App/Services/Update/GitHubReleaseParser.cs`
- 创建：`src/Files.App/Services/Update/UpdateNotesFormatter.cs`
- 测试：`tests/Files.App.UnitTests/Update/GitHubReleaseParserTests.cs`

- [ ] **步骤 1：写失败测试**

```csharp
private const string ValidJson = """
{
  "tag_name": "v4.3.1.7",
  "html_url": "https://github.com/wangjiati/Files/releases/tag/v4.3.1.7",
  "body": "## 本版变化\n- 测试"
}
""";

[TestMethod]
public void TryParseLatestRelease_ReadsVersionNotesAndUrl()
{
    Assert.IsTrue(GitHubReleaseParser.TryParseLatestRelease(ValidJson, out var release));
    Assert.AreEqual(new Version(4, 3, 1, 7), release.Version);
    Assert.AreEqual("https://github.com/wangjiati/Files/releases/tag/v4.3.1.7", release.PageUrl.ToString());
    Assert.IsTrue(release.Notes!.Contains("本版变化"));
}

[TestMethod]
public void TryParseLatestRelease_AcceptsHistoricalTag()
{
    var json = ValidJson.Replace("v4.3.1.7", "v4.3.1.0-portable");
    Assert.IsTrue(GitHubReleaseParser.TryParseLatestRelease(json, out var release));
    Assert.AreEqual(new Version(4, 3, 1, 0), release.Version);
}

[TestMethod]
public void TryParseLatestRelease_RejectsMissingUrl()
{
    var json = """{ "tag_name": "v4.3.1.7", "body": "" }""";
    Assert.IsFalse(GitHubReleaseParser.TryParseLatestRelease(json, out _));
}

[TestMethod]
public void TryParseLatestRelease_RejectsInvalidJson()
{
    Assert.IsFalse(GitHubReleaseParser.TryParseLatestRelease("{ not json", out _));
}

[TestMethod]
public void ToPreview_TruncatesLongNotes()
{
    Assert.AreEqual(string.Empty, UpdateNotesFormatter.ToPreview(null));
    Assert.AreEqual("abc", UpdateNotesFormatter.ToPreview("abc", 10));
    var preview = UpdateNotesFormatter.ToPreview(new string('a', 2000), 1500);
    Assert.AreEqual(1501, preview.Length);
    Assert.IsTrue(preview.EndsWith('…'));
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~Files.App.UnitTests.Update"`
预期：FAIL，`GitHubReleaseParser` / `UpdateNotesFormatter` 不存在。

- [ ] **步骤 3：实现**

```csharp
public sealed record GitHubReleaseInfo(Version Version, string? Notes, Uri PageUrl);

public static bool TryParseLatestRelease(string json, out GitHubReleaseInfo release);
public static string ToPreview(string? notes, int maxLength = 1500);
```

`GitHubReleaseParser` 规则：

- `JsonDocument.Parse(json)`；读 `tag_name`、`body`、`html_url`。
- `tag_name` 用 `UpdateVersionHelper.TryParseVersion` 解析，失败返回 `false`。
- `html_url` 必须能 `Uri.TryCreate(..., UriKind.Absolute, out var uri)` 且 `uri.Scheme == "https"`、`uri.Host == "github.com"`，否则返回 `false`。
- `body` 可以为空；内部异常返回 `false`，不抛出。

`UpdateNotesFormatter.ToPreview` 规则：

- `null` / 空白返回 `string.Empty`。
- 长度不超过 `maxLength` 原样返回。
- 超出时截断到 `maxLength`，`TrimEnd()` 后追加 `…`。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~Files.App.UnitTests.Update"`
预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/Services/Update/GitHubReleaseParser.cs src/Files.App/Services/Update/UpdateNotesFormatter.cs tests/Files.App.UnitTests/Update/GitHubReleaseParserTests.cs
git commit -m "Add GitHub release parsing"
```

### 任务 3：每日检查与跳过策略

**文件：**
- 创建：`src/Files.App/Services/Update/UpdateCheckPolicy.cs`
- 测试：`tests/Files.App.UnitTests/Update/UpdateCheckPolicyTests.cs`

- [ ] **步骤 1：写失败测试**

```csharp
[TestMethod]
public void ShouldCheckToday_OnlyChecksWhenDateDiffers()
{
    var today = new DateOnly(2026, 11, 7);
    Assert.IsTrue(UpdateCheckPolicy.ShouldCheckToday(null, today));
    Assert.IsTrue(UpdateCheckPolicy.ShouldCheckToday("2026-11-06", today));
    Assert.IsFalse(UpdateCheckPolicy.ShouldCheckToday("2026-11-07", today));
}

[TestMethod]
public void ShouldCheckToday_IgnoresInvalidStoredValue()
{
    Assert.IsTrue(UpdateCheckPolicy.ShouldCheckToday("not-a-date", new DateOnly(2026, 11, 7)));
}

[TestMethod]
public void IsSkipped_MatchesParsedVersion()
{
    Assert.IsTrue(UpdateCheckPolicy.IsSkipped(new Version(4, 3, 1, 7), "4.3.1.7"));
    Assert.IsFalse(UpdateCheckPolicy.IsSkipped(new Version(4, 3, 1, 8), "4.3.1.7"));
    Assert.IsFalse(UpdateCheckPolicy.IsSkipped(new Version(4, 3, 1, 7), null));
    Assert.IsFalse(UpdateCheckPolicy.IsSkipped(new Version(4, 3, 1, 7), "garbage"));
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~Files.App.UnitTests.Update"`
预期：FAIL，`UpdateCheckPolicy` 不存在。

- [ ] **步骤 3：实现**

```csharp
public const string DateFormat = "yyyy-MM-dd";

public static bool ShouldCheckToday(string? lastCheckDate, DateOnly today);
public static bool IsSkipped(Version available, string? skippedVersion);
```

规则：

- `ShouldCheckToday`：`DateOnly.TryParseExact(lastCheckDate, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)` 且 `parsed == today` 时返回 `false`；其他情况返回 `true`。
- `IsSkipped`：`skippedVersion` 为空或解析失败返回 `false`；否则比较四段版本相等。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~Files.App.UnitTests.Update"`
预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/Services/Update/UpdateCheckPolicy.cs tests/Files.App.UnitTests/Update/UpdateCheckPolicyTests.cs
git commit -m "Add update check policy"
```

### 任务 4：GitHubUpdateService 与接口扩展

**文件：**
- 创建：`src/Files.App/Data/Enums/UpdateCheckResult.cs`
- 创建：`src/Files.App/Services/App/GitHubUpdateService.cs`
- 修改：`src/Files.App/Data/Contracts/IUpdateService.cs`
- 修改：`src/Files.App/Data/Contracts/IApplicationSettingsService.cs`
- 修改：`src/Files.App/Services/Settings/ApplicationSettingsService.cs`
- 修改：`src/Files.App/Services/App/ManualUpdateService.cs`
- 修改：`src/Files.App/Services/App/AppUpdateNoneService.cs`
- 修改：`src/Files.App/Helpers/Application/AppLifecycleHelper.cs:405`
- 测试：`tests/Files.App.UnitTests/Update/GitHubUpdateServiceTests.cs`

- [ ] **步骤 1：写失败测试**

测试内提供三个替身：字典实现的 `FakeLocalState`、实现 `IApplicationSettingsService` 的 `FakeSettings`、返回固定状态码/正文并计数的 `StubHandler : HttpMessageHandler`。

```csharp
[TestMethod]
public async Task CheckForUpdatesAsync_Disabled_DoesNotRequest()
{
    var settings = new FakeSettings { CheckForUpdates = false };
    var handler = new StubHandler(HttpStatusCode.OK, NewerJson);
    var service = CreateService(settings, new FakeLocalState(), handler, new Version(4, 3, 1, 0));

    await service.CheckForUpdatesAsync();

    Assert.AreEqual(0, handler.Requests);
    Assert.AreEqual(UpdateCheckResult.Disabled, service.LastResult);
}

[TestMethod]
public async Task CheckForUpdatesAsync_HttpFailure_SetsFailedAndRecordsAttempt()
{
    var handler = new StubHandler(HttpStatusCode.Forbidden, string.Empty);
    var state = new FakeLocalState();
    var service = CreateService(new FakeSettings(), state, handler, new Version(4, 3, 1, 0));

    await service.CheckForUpdatesAsync();

    Assert.AreEqual(UpdateCheckResult.Failed, service.LastResult);
    Assert.IsFalse(service.IsUpdateAvailable);
    Assert.AreEqual(DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"), state.GetString(GitHubUpdateService.LastUpdateCheckDateKey));
}

[TestMethod]
public async Task CheckForUpdatesAsync_NewerRelease_SetsUpdateAvailable()
{
    var handler = new StubHandler(HttpStatusCode.OK, NewerJson);
    var service = CreateService(new FakeSettings(), new FakeLocalState(), handler, new Version(4, 3, 1, 0));

    await service.CheckForUpdatesAsync();

    Assert.AreEqual(UpdateCheckResult.UpdateAvailable, service.LastResult);
    Assert.AreEqual(new Version(4, 3, 1, 7), service.AvailableVersion);
    Assert.IsTrue(service.IsUpdateAvailable);
}

[TestMethod]
public async Task CheckForUpdatesAsync_SameVersion_SetsUpToDate()
{
    var service = CreateService(new FakeSettings(), new FakeLocalState(), new StubHandler(HttpStatusCode.OK, NewerJson), new Version(4, 3, 1, 7));
    await service.CheckForUpdatesAsync();
    Assert.AreEqual(UpdateCheckResult.UpToDate, service.LastResult);
    Assert.IsFalse(service.IsUpdateAvailable);
}

[TestMethod]
public async Task CheckForUpdatesAsync_SkippedVersion_SetsSkipped()
{
    var settings = new FakeSettings { SkippedVersion = "4.3.1.7" };
    var service = CreateService(settings, new FakeLocalState(), new StubHandler(HttpStatusCode.OK, NewerJson), new Version(4, 3, 1, 0));
    await service.CheckForUpdatesAsync();
    Assert.AreEqual(UpdateCheckResult.Skipped, service.LastResult);
    Assert.IsTrue(service.IsUpdateAvailable);
}

[TestMethod]
public async Task CheckForUpdatesAsync_CheckedToday_SkipsRequest()
{
    var handler = new StubHandler(HttpStatusCode.OK, NewerJson);
    var state = new FakeLocalState();
    state.SetString(GitHubUpdateService.LastUpdateCheckDateKey, DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"));
    var service = CreateService(new FakeSettings(), state, handler, new Version(4, 3, 1, 0));

    await service.CheckForUpdatesAsync();

    Assert.AreEqual(0, handler.Requests);
}

[TestMethod]
public async Task CheckForUpdatesAsync_Force_BypassesDailyGate()
{
    var handler = new StubHandler(HttpStatusCode.OK, NewerJson);
    var state = new FakeLocalState();
    state.SetString(GitHubUpdateService.LastUpdateCheckDateKey, DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"));
    var service = CreateService(new FakeSettings(), state, handler, new Version(4, 3, 1, 0));

    await service.CheckForUpdatesAsync(force: true);

    Assert.AreEqual(1, handler.Requests);
}
```

`NewerJson` 使用 `tag_name = v4.3.1.7`、`html_url = https://github.com/wangjiati/Files/releases/tag/v4.3.1.7`、`body = "## 本版变化\n- 测试"`。

测试辅助：`CreateService` 用 `new GitHubUpdateService(settings, state, new HttpClient(handler), () => currentVersion)` 构造服务。

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~Files.App.UnitTests.Update"`
预期：FAIL，`GitHubUpdateService` / `UpdateCheckResult` 不存在。

- [ ] **步骤 3：实现接口与设置字段**

`UpdateCheckResult`：

```csharp
public enum UpdateCheckResult
{
    NotChecked,
    UpToDate,
    UpdateAvailable,
    Skipped,
    Failed,
    Disabled,
}
```

`IUpdateService` 增加：

```csharp
Version? AvailableVersion { get; }
string? AvailableReleaseNotes { get; }
Uri? ReleasePageUrl { get; }
UpdateCheckResult LastResult { get; }

Task CheckForUpdatesAsync(bool force = false);
```

`IApplicationSettingsService` / `ApplicationSettingsService` 增加：

```csharp
bool CheckForUpdates { get; set; }      // 默认 true
string? SkippedVersion { get; set; }    // 默认 null
```

同步更新 `ManualUpdateService` 与 `DummyUpdateService`：新属性返回 `null` / `NotChecked`，`CheckForUpdatesAsync(bool force = false)` 保持空实现。

- [ ] **步骤 4：实现 `GitHubUpdateService`**

```csharp
internal sealed partial class GitHubUpdateService : ObservableObject, IUpdateService
{
    internal const string LastUpdateCheckDateKey = "LastUpdateCheckDate";
    private const string ReleaseApiUrl = "https://api.github.com/repos/wangjiati/Files/releases/latest";

    public GitHubUpdateService(IApplicationSettingsService applicationSettings, ILocalStateStore localState);
    internal GitHubUpdateService(IApplicationSettingsService applicationSettings, ILocalStateStore localState, HttpClient httpClient, Func<Version> currentVersion);
}
```

`CheckForUpdatesAsync(bool force = false)` 顺序：

1. `if (!applicationSettings.CheckForUpdates) { LastResult = Disabled; return; }`
2. `var today = DateOnly.FromDateTime(DateTime.Now);`
3. `if (!force && !UpdateCheckPolicy.ShouldCheckToday(localState.GetString(LastUpdateCheckDateKey), today)) return;`
4. `localState.SetString(LastUpdateCheckDateKey, today.ToString(UpdateCheckPolicy.DateFormat));`（无论成功失败都记录）
5. GET `ReleaseApiUrl`，请求头 `User-Agent: Files-Internal/<currentVersion>`、`Accept: application/vnd.github+json`；`HttpClient.Timeout = 5s`。
6. 非成功状态码或解析失败 → `LastResult = Failed`。
7. `UpdateVersionHelper.IsNewer(release.Version, currentVersion())` 为假 → `UpToDate`。
8. `UpdateCheckPolicy.IsSkipped(release.Version, applicationSettings.SkippedVersion)` 为真 → 设置可用版本/说明/URL、`IsUpdateAvailable = true`、`LastResult = Skipped`。
9. 否则设置三者和 `IsUpdateAvailable = true`、`LastResult = UpdateAvailable`。
10. 任何异常 → `App.Logger?.LogWarning(ex, ...)`，`LastResult = Failed`。

其他成员：

- `IsUpdating => false`、`UpdateProgress => 0`、`IsAppUpdated => AppLifecycleHelper.IsAppUpdated`、`AreReleaseNotesAvailable => false`。
- `DownloadUpdatesAsync()`：`ReleasePageUrl` 为有效 `https://github.com` 地址时打开；否则不做事。
- `DownloadMandatoryUpdatesAsync()` / `CheckForReleaseNotesAsync()`：`Task.CompletedTask`。
- `CheckAndUpdateFilesLauncherAsync()`：`Task.CompletedTask`。

- [ ] **步骤 5：注册服务**

把 `AppLifecycleHelper.cs:405` 的注册改为：

```csharp
services.AddSingleton<IUpdateService>(sp => new GitHubUpdateService(
    sp.GetRequiredService<IApplicationSettingsService>(),
    AppRuntime.LocalState));
```

- [ ] **步骤 6：运行测试验证通过**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~Files.App.UnitTests.Update"`
预期：PASS。

- [ ] **步骤 7：构建**

运行：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：无错误。

- [ ] **步骤 8：Commit**

```powershell
git add src/Files.App/Data/Enums/UpdateCheckResult.cs src/Files.App/Data/Contracts/IUpdateService.cs src/Files.App/Data/Contracts/IApplicationSettingsService.cs src/Files.App/Services/Settings/ApplicationSettingsService.cs src/Files.App/Services/App/GitHubUpdateService.cs src/Files.App/Services/App/ManualUpdateService.cs src/Files.App/Services/App/AppUpdateNoneService.cs src/Files.App/Helpers/Application/AppLifecycleHelper.cs tests/Files.App.UnitTests/Update/GitHubUpdateServiceTests.cs
git commit -m "Add GitHub release update service"
```

### 任务 5：更新弹窗与启动接线

**文件：**
- 创建：`src/Files.App/Helpers/Dialog/UpdateAvailableDialog.cs`
- 修改：`src/Files.App/Helpers/Application/AppLifecycleHelper.cs`（`CheckAppUpdate`）
- 修改：`src/Files.App/Strings/en-US/Resources.resw`、`src/Files.App/Strings/zh-Hans/Resources.resw`

- [ ] **步骤 1：实现弹窗，复用 `DynamicDialog`**

```csharp
namespace Files.App.Helpers;

internal static class UpdateAvailableDialog
{
    public static async Task ShowAsync(IUpdateService updateService);
}
```

- `availableVersion` 为 `null` 时直接返回。
- `TitleText = string.Format(Strings.UpdateAvailableTitle..., version)`。
- `SubtitleText = string.Format(Strings.UpdateAvailableSubtitle..., major, minor, build, revision)`。
- `DisplayControl`：`ScrollViewer { MaxHeight = 320, Content = new TextBlock { Text = UpdateNotesFormatter.ToPreview(notes), TextWrapping = TextWrapping.Wrap } }`。
- `PrimaryButtonText = Strings.DownloadUpdate...`，`PrimaryButtonAction = (vm, _) => { OpenReleasePage(updateService.ReleasePageUrl); vm.Hide(); }`。
- `SecondaryButtonText = Strings.RemindMeLater...`，`SecondaryButtonAction = (vm, _) => { Ioc.Default.GetRequiredService<IApplicationSettingsService>().SkippedVersion = version.ToString(); vm.Hide(); }`。
- `DynamicButtons = DynamicDialogButtons.Primary | DynamicDialogButtons.Secondary`。
- `OpenReleasePage` 只接受 `https` + `github.com` 的地址，用 `Launcher.LaunchUriAsync` 打开。
- 用 `using var dialog = new DynamicDialog(viewModel); await dialog.ShowAsync();`。

- [ ] **步骤 2：接到现有启动流程**

在 `AppLifecycleHelper.CheckAppUpdate()` 中，把原来的 `await updateService.CheckForUpdatesAsync();` 替换为：

```csharp
#if !DEBUG
await updateService.CheckForUpdatesAsync();

if (updateService.LastResult == UpdateCheckResult.UpdateAvailable)
{
    _ = Task.Run(async () =>
    {
        await MainPageViewModel.StartupTabsLoadedTask;

        await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
        {
            await UpdateAvailableDialog.ShowAsync(updateService);
        });
    });
}
#endif
```

不要新增生命周期入口，也不要改 `InitializeAppComponentsAsync`。

- [ ] **步骤 3：补资源键**

| Key | en-US | zh-Hans |
|---|---|---|
| `UpdateAvailableTitle` | New version {0} available | 发现新版本 {0} |
| `UpdateAvailableSubtitle` | Based on upstream {0}.{1}.{2} · internal build {3} | 基于上游 {0}.{1}.{2} · 内部迭代 {3} |
| `DownloadUpdate` | Download update | 下载更新 |
| `RemindMeLater` | Remind me later | 以后再说 |

- [ ] **步骤 4：构建**

运行：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：无错误。

- [ ] **步骤 5：Commit**

```powershell
git add src/Files.App/Helpers/Dialog/UpdateAvailableDialog.cs src/Files.App/Helpers/Application/AppLifecycleHelper.cs src/Files.App/Strings/en-US/Resources.resw src/Files.App/Strings/zh-Hans/Resources.resw
git commit -m "Show update available dialog"
```

### 任务 6：关于页版本显示、更新设置与主页入口

**文件：**
- 修改：`src/Files.App/Constants.cs`
- 修改：`src/Files.App/ViewModels/Settings/AboutViewModel.cs`
- 修改：`src/Files.App/Views/Settings/AboutPage.xaml`
- 修改：`src/Files.App/Strings/en-US/Resources.resw`、`src/Files.App/Strings/zh-Hans/Resources.resw`

- [ ] **步骤 1：外部链接常量**

`Constants.ExternalUrl` 增加并修正：

```csharp
public const string HomepageUrl = @"https://wangjiati.github.io/Files";
public const string GitHubRepoUrl = @"https://github.com/wangjiati/Files";
public const string DocumentationUrl = HomepageUrl;
```

社区和赞助链接本次不动。

- [ ] **步骤 2：AboutViewModel**

- 解析 `IApplicationSettingsService` 和 `IUpdateService`（与现有 `GeneralSettingsService` 同样的 `Ioc.Default` 写法）。
- `Version` 改为 `Strings.SettingsAboutVersionFormat` 格式化。
- 新增 `UpstreamVersion`（`Strings.SettingsAboutUpstreamFormat`）和 `VersionWithUpstream => $"{Version}\n{UpstreamVersion}"`。
- 新增 `CheckForUpdates`（读写 `ApplicationSettingsService.CheckForUpdates`，变更时 `OnPropertyChanged`）。
- 新增 `IsCheckForUpdatesEnabled => CheckForUpdates`。
- 新增 `CheckForUpdatesCommand`：`await UpdateService.CheckForUpdatesAsync(force: true); OnPropertyChanged(nameof(UpdateStatus));`。
- 新增 `UpdateStatus`，按 `UpdateService.LastResult` 返回：
  - `NotChecked` → 空字符串
  - `UpToDate` → `Strings.UpdateStatusUpToDate`
  - `UpdateAvailable` → `Strings.UpdateStatusAvailable` + 版本
  - `Skipped` → `Strings.UpdateStatusSkipped` + 版本
  - `Failed` → `Strings.UpdateStatusFailed`
  - `Disabled` → `Strings.UpdateStatusDisabled`
- 新增 `OpenHomepageCommand`：`Launcher.LaunchUriAsync(new Uri(Constants.ExternalUrl.HomepageUrl)).AsTask()`。

- [ ] **步骤 3：AboutPage.xaml**

现版本卡片：

```xml
Description="{x:Bind ViewModel.VersionWithUpstream}"
```

版本卡片下方新增更新卡片：

```xml
<wctcontrols:SettingsCard
    HorizontalAlignment="Stretch"
    Description="{helpers:ResourceString Name=CheckForUpdatesDescription}"
    Header="{helpers:ResourceString Name=CheckForUpdates}">
    <wctcontrols:SettingsCard.HeaderIcon>
        <FontIcon Glyph="&#xE895;" />
    </wctcontrols:SettingsCard.HeaderIcon>
    <StackPanel Spacing="4">
        <StackPanel Orientation="Horizontal" Spacing="8">
            <ToggleSwitch
                AutomationProperties.Name="{helpers:ResourceString Name=CheckForUpdates}"
                IsOn="{x:Bind ViewModel.CheckForUpdates, Mode=TwoWay}" />
            <Button
                Command="{x:Bind ViewModel.CheckForUpdatesCommand}"
                Content="{helpers:ResourceString Name=CheckNow}"
                IsEnabled="{x:Bind ViewModel.IsCheckForUpdatesEnabled, Mode=OneWay}" />
        </StackPanel>
        <TextBlock
            Style="{StaticResource CaptionTextBlockStyle}"
            Text="{x:Bind ViewModel.UpdateStatus, Mode=OneWay}" />
    </StackPanel>
</wctcontrols:SettingsCard>
```

在“Help and support”标题下新增主页卡片：

```xml
<wctcontrols:SettingsCard
    HorizontalAlignment="Stretch"
    Command="{x:Bind ViewModel.OpenHomepageCommand}"
    Header="{helpers:ResourceString Name=Homepage}"
    IsClickEnabled="True">
    <wctcontrols:SettingsCard.HeaderIcon>
        <FontIcon Glyph="&#xE80F;" />
    </wctcontrols:SettingsCard.HeaderIcon>
    <wctcontrols:SettingsCard.ActionIcon>
        <FontIcon Glyph="&#xE8A7;" />
    </wctcontrols:SettingsCard.ActionIcon>
</wctcontrols:SettingsCard>
```

- [ ] **步骤 4：补资源键**

| Key | en-US | zh-Hans |
|---|---|---|
| `SettingsAboutVersionFormat` | Version {0}.{1}.{2} (internal build {3}) | 版本 {0}.{1}.{2}（内部迭代 {3}） |
| `SettingsAboutUpstreamFormat` | Based on upstream {0}.{1}.{2} | 基于上游 {0}.{1}.{2} |
| `CheckForUpdates` | Check for updates | 检查更新 |
| `CheckForUpdatesDescription` | Check once a day at startup | 启动时每日检查一次 |
| `CheckNow` | Check now | 立即检查 |
| `Homepage` | Homepage | 主页 |
| `UpdateStatusUpToDate` | You're up to date | 已是最新版本 |
| `UpdateStatusFailed` | Couldn't check for updates | 检查失败，稍后再试 |
| `UpdateStatusSkipped` | Version {0} skipped | 已跳过 {0} |
| `UpdateStatusAvailable` | Version {0} is available | 发现新版本 {0} |
| `UpdateStatusDisabled` | Update checks are turned off | 检查更新已关闭 |

- [ ] **步骤 5：构建**

运行：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：无错误。

- [ ] **步骤 6：手工验证**

- 打开设置 → 关于：版本显示 `4.3.1.0（内部迭代 0）` 和 `基于上游 4.3.1`（版本切换前）。
- 主页卡片打开 `https://wangjiati.github.io/Files`。
- 关闭开关后"立即检查"禁用；重新打开后可用。
- 手动检查在离线时显示"检查失败，稍后再试"，不崩溃。

- [ ] **步骤 7：Commit**

```powershell
git add src/Files.App/Constants.cs src/Files.App/ViewModels/Settings/AboutViewModel.cs src/Files.App/Views/Settings/AboutPage.xaml src/Files.App/Strings/en-US/Resources.resw src/Files.App/Strings/zh-Hans/Resources.resw
git commit -m "Show version details and update settings"
```

### 任务 7：版本号切到 4.3.1.1 与发布文档

**文件：**
- 修改：`src/Files.App/Files.App.csproj:7-10`
- 修改：`src/Files.App/Package.appxmanifest`（`Identity Version`）
- 修改：`.github/scripts/INTERNAL-RELEASE.md`

- [ ] **步骤 1：改版本号**

两处都改为 `4.3.1.1`：

```xml
<Version>4.3.1.1</Version>
<AssemblyVersion>4.3.1.1</AssemblyVersion>
<FileVersion>4.3.1.1</FileVersion>
<InformationalVersion>4.3.1.1</InformationalVersion>
```

```xml
<Identity Version="4.3.1.1" />
```

- [ ] **步骤 2：跑现有版本一致性测试**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64 --filter "FullyQualifiedName~AppRuntimeEnvironmentTests"`
预期：PASS，`Portable_VersionMatchesThePackageManifest` 证明两处一致。

- [ ] **步骤 3：更新发布文档**

在 `.github/scripts/INTERNAL-RELEASE.md` 增加一小节：

- 标签：`v<四段版本>`，例如 `v4.3.1.1`，不再带分发后缀。
- 标题：`Files Internal 4.3.1.1`。
- 正文必须有 `## 本版变化`，由维护者手工填写，是客户端弹窗来源。
- 资产仍为 MSIX、便携 ZIP、`SHA256SUMS.txt`、`RELEASE-INFO.txt`。
- 预发布构建标记 `prerelease`。
- 上游升级后把第四段重置为 1，并更新前三段。

- [ ] **步骤 4：Commit**

```powershell
git add src/Files.App/Files.App.csproj src/Files.App/Package.appxmanifest .github/scripts/INTERNAL-RELEASE.md
git commit -m "Bump version to 4.3.1.1"
```

### 任务 8：完整验证与打包

- [ ] **步骤 1：跑全部单测**

运行：`dotnet test tests/Files.App.UnitTests/Files.App.UnitTests.csproj -p:Configuration=Debug -p:Platform=x64`
预期：PASS。

- [ ] **步骤 2：构建**

运行：`msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly`
预期：无错误。

- [ ] **步骤 3：打包**

先提交所有改动，再运行：

```powershell
pwsh -File .github/scripts/Build-Internal.ps1
```

预期：产出 `artifacts/internal/4.3.1.1/Files.Internal_4.3.1.1_x64.msix` 和 `Files.Internal_4.3.1.1_x64-portable.zip`，`RELEASE-INFO.txt` 记录 `Version: 4.3.1.1`。

- [ ] **步骤 4：启动打包版本做 UI 冒烟**

- 启动 MSIX 和便携版，确认关于页显示 `4.3.1.1（内部迭代 1）` 和 `基于上游 4.3.1`。
- 打开主页链接。
- 关闭更新开关，确认无请求。
- 用 `4.3.1.1` 构建创建一个临时的 `v4.3.1.2` Release（可用 `gh release create v4.3.1.2 --notes "## 本版变化\n- 测试"`，不上传资产），启动客户端手动检查，确认弹窗出现、说明可滚动、"下载更新"打开 Release 页、"以后再说"后再次检查显示"已跳过"；验证完删除该临时 Release。

- [ ] **步骤 5：无代码改动则结束**

本任务只做验证；若发现缺陷，回到对应任务修复并重新执行本任务。
