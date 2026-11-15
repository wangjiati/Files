// Filter UI pipeline: drives the packaged Files app through UI Automation and
// writes a machine-readable result file. Runs inside the test VM as a scheduled
// task in the logged-on user's session. No screen capture, no shell tricks.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

namespace FilterUiTest;

internal sealed record TestResult(string Name, bool Pass, string Detail);

internal static class Program
{
    private static readonly List<TestResult> Results = [];
    private static string outDir = ".";
    private static string? folder;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr h);

    private static int Main(string[] args)
    {
        var exe = Arg(args, "--exe");
        folder = Arg(args, "--folder");
        outDir = Arg(args, "--out") ?? ".";
        Directory.CreateDirectory(outDir);

        if (exe is null || folder is null || !File.Exists(exe))
        {
            File.WriteAllText(Path.Combine(outDir, "error.txt"), $"bad args exe={exe} folder={folder}");
            return 2;
        }

        Log($"exe={exe}");
        Log($"folder={folder}");

        AutomationElement? root = null;
        Process? app = null;

        try
        {
            app = Process.Start(new ProcessStartInfo(exe, $"\"{folder}\"") { UseShellExecute = true });
            root = cachedRoot = WaitWindow(app, TimeSpan.FromSeconds(50));
            if (root is null)
            {
                Log("no main window after 50s");
                Fail("launch", "main window not found");
                return WriteAndExit(1);
            }

            Log($"window title={root.Current.Name}");
            Focus(root);
            Thread.Sleep(1500);

            WaitForBar(25);
            EnsureFolder(25);

            Test("launch", () => Assert(!string.IsNullOrWhiteSpace(root.Current.Name), $"title '{root.Current.Name}'"));
            Test("filter-bar", () =>
            {
                var kw = FindById("KeywordBox") ?? FindById("FilterTextBox");
                Assert(kw is not null, "keyword box");
                var basic = FindByName(["常规", "Basic"], ControlType.ListItem);
                var advanced = FindByName(["高级", "Advanced"], ControlType.ListItem);
                Assert(basic is not null && advanced is not null, "mode selector items");
            });

            Test("labels-zh", () =>
            {
                var basic = FindByName(["常规", "Basic"], ControlType.ListItem);
                var advanced = FindByName(["高级", "Advanced"], ControlType.ListItem);
                var recurse = FindByName(["搜索子文件夹", "Search subfolders"], ControlType.CheckBox);
                var clear = FindByName(["清除所有筛选", "Clear all filters"], ControlType.Button) ??
                            FindByName(["清除所有筛选", "Clear all filters"], null);
                var names = new[] { basic?.Current.Name, advanced?.Current.Name, recurse?.Current.Name, clear?.Current.Name };
                var observed = string.Join(" | ", names.Select(n => n ?? "<null>"));
                Assert(basic?.Current.Name == "常规" && advanced?.Current.Name == "高级" && recurse?.Current.Name == "搜索子文件夹",
                    $"observed: {observed}");
            });

            Test("basic-wildcard", () =>
            {
                SelectMode("常规", "Basic");
                var kw = WaitFor(() => FindById("KeywordBox"), 6);
                Assert(kw is not null, "keyword box");
                SetText(kw!, "*.zip");
                var count = WaitCount(6);
                Assert(count == 2, $"expected 2 zips, got {count}");
            });

            Test("advanced-editor", () =>
            {
                SelectMode("高级", "Advanced");
                var editor = WaitFor(() => FindById("FilterTextBox"), 6);
                Assert(editor is not null, "name condition editor");
                var combo = WaitFor(() => FindByControlType(ControlType.ComboBox), 4);
                Assert(combo is not null, "operator/scope combo");
            });

            Test("recursion-location", () =>
            {
                SelectMode("常规", "Basic");
                var kw = WaitFor(() => FindById("KeywordBox"), 6);
                Assert(kw is not null, "keyword box");
                SetText(kw!, "*.zip");
                WaitCount(6);
                var cb = FindByName(["搜索子文件夹", "Search subfolders"], ControlType.CheckBox);
                Assert(cb is not null, "recursion checkbox");
                Toggle(cb!);
                Thread.Sleep(2500);
                var location = FindByName(["位置", "Location"], null);
                Assert(location is not null, "location column");
                var count = GetCount();
                Assert(count == 4, $"expected 4 zips with 2 levels, got {count}");
            });

            Test("clear-all", () =>
            {
                var clear = FindByName(["清除所有筛选", "Clear all filters"], ControlType.Button) ??
                            FindByName(["清除所有筛选", "Clear all filters"], null);
                Assert(clear is not null, "clear button");
                Invoke(clear!);
                Thread.Sleep(1500);
                var count = GetCount();
                var total = Directory.EnumerateFileSystemEntries(folder).Count();
                Assert(count is null || count == total, $"after clear count={count}, entries={total}");
            });

            Test("column-header-append", () =>
            {
                var headerFilters = All().Where(e => e.Current.ControlType == ControlType.Button)
                    .Where(e => (e.Current.Name ?? "").Contains("筛选") || (e.Current.Name ?? "").Contains("Filter")
                             || (e.Current.AutomationId ?? "").Contains("Filter"))
                    .ToList();
                Log($"header filter buttons: {headerFilters.Count}");
                foreach (var candidate in headerFilters)
                {
                    var rect = candidate.Current.BoundingRectangle;
                    Log($"  header button name=[{candidate.Current.Name}] id=[{candidate.Current.AutomationId}] x={rect.X} y={rect.Y}");
                }
                if (headerFilters.Count < 2)
                {
                    throw new InvalidOperationException($"header filter buttons found: {headerFilters.Count}");
                }
                var headerButton = headerFilters.OrderBy(e => e.Current.BoundingRectangle.Y).Last();
                Invoke(headerButton);
                Thread.Sleep(1500);
                var advanced = FindByName(["高级", "Advanced"], ControlType.ListItem);
                var editor = WaitFor(() => FindById("FilterTextBox"), 5);
                Assert(advanced is not null && editor is not null, "advanced name editor after header click");
            });
        }
        catch (Exception ex)
        {
            Fail("pipeline-exception", ex.ToString());
        }
        finally
        {
            TryClose(app, root);
        }

        return WriteAndExit(Results.Any(r => !r.Pass) ? 1 : 0);
    }

    private static int WriteAndExit(int code)
    {
        var lines = Results.Select(r => $"{(r.Pass ? "PASS" : "FAIL")}|{r.Name}|{r.Detail}").ToList();
        File.WriteAllLines(Path.Combine(outDir, "results.txt"), lines);
        File.WriteAllText(Path.Combine(outDir, "summary.json"), JsonSerializer.Serialize(new
        {
            passed = Results.Count(r => r.Pass),
            failed = Results.Count(r => !r.Pass),
            results = Results,
            finishedUtc = DateTime.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Log($"done passed={Results.Count(r => r.Pass)} failed={Results.Count(r => !r.Pass)}");
        return code;
    }

    private static void Test(string name, Action body)
    {
        try
        {
            body();
            Results.Add(new TestResult(name, true, "ok"));
            Log($"PASS {name}");
        }
        catch (Exception ex)
        {
            Results.Add(new TestResult(name, false, ex.Message));
            Log($"FAIL {name}: {ex.Message}");
        }
    }

    private static void Assert(bool condition, string detail)
    {
        if (!condition)
        {
            throw new InvalidOperationException(detail);
        }
    }

    private static void Fail(string name, string detail)
    {
        Results.Add(new TestResult(name, false, detail));
        Log($"FAIL {name}: {detail}");
    }

    private static AutomationElement? WaitWindow(Process? app, TimeSpan timeout)    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    if ((app is not null && p.Id == app.Id) || (p.ProcessName.Contains("Files", StringComparison.OrdinalIgnoreCase)))
                    {
                        return AutomationElement.FromHandle(p.MainWindowHandle);
                    }
                }
                catch
                {
                    // process exited between enumeration and access
                }
            }

            Thread.Sleep(400);
        }

        return null;
    }

    private static void Focus(AutomationElement root)
    {
        var handle = root.Current.NativeWindowHandle;
        SetWindowPos(handle, new IntPtr(-1), 0, 0, 3840, 2100, 0x0040);
        SetForegroundWindow(handle);
        Thread.Sleep(600);
    }

    private static void WaitForBar(int seconds)
    {
        if (WaitBarReady(seconds))
        {
            return;
        }

        Log("filter bar hidden; trying Ctrl+Shift+F");
        System.Windows.Forms.SendKeys.SendWait("^+f");
        Thread.Sleep(2500);
        if (WaitBarReady(6))
        {
            return;
        }

        Log("still hidden; invoking toolbar filter toggle");
        var toggle = FindByName(["筛选", "Filter"], ControlType.Button);
        if (toggle is not null)
        {
            Invoke(toggle);
            Thread.Sleep(2500);
        }

        WaitBarReady(8);
    }

    private static bool WaitBarReady(int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            if (FindById("KeywordBox") is not null || FindById("FilterTextBox") is not null)
            {
                Log("filter bar ready");
                return true;
            }

            Thread.Sleep(300);
        }

        return false;
    }

    private static void EnsureFolder(int seconds)
    {
        var marker = Path.GetFileName(folder ?? string.Empty);
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            if (FindByName([marker], null) is not null)
            {
                Log($"folder view ready: {marker}");
                return;
            }

            Thread.Sleep(300);
        }

        var address = FindById("PART_TextBox");
        if (address is null)
        {
            Log("address bar not found; cannot navigate");
            return;
        }

        Log($"navigating to {folder}");
        address.SetFocus();
        SetText(address, folder ?? string.Empty);
        Thread.Sleep(300);
        System.Windows.Forms.SendKeys.SendWait("{ENTER}");
        Thread.Sleep(3000);
    }

    private static void TryClose(Process? app, AutomationElement? root)
    {
        try
        {
            if (root is not null && root.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern))
            {
                ((WindowPattern)pattern).Close();
                Thread.Sleep(800);
            }
        }
        catch
        {
            // closing is best effort
        }

        try
        {
            if (app is not null && !app.HasExited)
            {
                app.Kill();
            }
        }
        catch
        {
            // already gone
        }
    }

    private static void SelectMode(params string[] names)
    {
        var item = FindByName(names, ControlType.ListItem);
        Assert(item is not null, $"mode item {string.Join("/", names)}");
        Assert(item!.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern), "selection pattern");
        ((SelectionItemPattern)pattern).Select();
        Thread.Sleep(600);
    }

    private static AutomationElement? WaitFor(Func<AutomationElement?> find, int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            var element = find();
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(200);
        }

        return null;
    }

    private static int? WaitCount(int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        int? last = null;
        while (DateTime.UtcNow < end)
        {
            last = GetCount();
            if (last is not null)
            {
                return last;
            }

            Thread.Sleep(200);
        }

        return last;
    }

    private static int? GetCount()
    {
        foreach (var element in All())
        {
            if (element.Current.ControlType != ControlType.Text)
            {
                continue;
            }

            var match = Regex.Match(element.Current.Name ?? string.Empty, @"^(\d+)\s*(个匹配项|matching items)$");
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value);
            }
        }

        return null;
    }

    private static bool SetText(AutomationElement element, string text)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            ((ValuePattern)pattern).SetValue(text);
            return true;
        }

        return false;
    }

    private static bool Toggle(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
        {
            ((TogglePattern)pattern).Toggle();
            return true;
        }

        return false;
    }

    private static bool Invoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
        {
            ((InvokePattern)pattern).Invoke();
            return true;
        }

        return false;
    }

    private static AutomationElement? FindById(string id)
    {
        var root = Root();
        return root?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
    }

    private static AutomationElement? FindByControlType(ControlType type)
    {
        return Root()?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type));
    }

    private static AutomationElement? FindByName(IEnumerable<string> names, ControlType? type)
    {
        var root = Root();
        if (root is null)
        {
            return null;
        }

        foreach (var name in names)
        {
            var found = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));
            foreach (AutomationElement element in found)
            {
                if (type is null || element.Current.ControlType == type)
                {
                    return element;
                }
            }
        }

        return null;
    }

    private static IEnumerable<AutomationElement> All()
    {
        var root = Root();
        if (root is null)
        {
            yield break;
        }

        foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            yield return element;
        }
    }

    private static AutomationElement? Root()
    {
        return cachedRoot;
    }

    private static AutomationElement? cachedRoot;

    private static void Log(string message)
    {
        File.AppendAllText(Path.Combine(outDir, "run.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
    }

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
