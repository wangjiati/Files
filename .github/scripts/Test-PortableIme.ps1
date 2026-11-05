# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
End-to-end IME check for the portable build: launches Files, renames a file and types
pinyin, then reports whether the IME candidate window appeared.

.DESCRIPTION
Two modes:
  Identity - register the shipped identity package and launch through the package identity.
             Expected to show the candidate window on affected Windows builds.
  Plain    - skip registration (FILES_DISABLE_PORTABLE_IDENTITY=1) and launch directly.
             Expected to show the composition but no candidate window on affected builds.

The measurement is deliberately narrow:

  * only the rename text box of the sample item is used (matched by automation id and by
    its initial value), never the address bar or the search box;
  * candidate windows are diffed against a baseline taken right before typing, so an IME
    window that was already visible does not count;
  * status bars, settings panels and other IME companions are ignored, only candidate
    windows count;
  * the composition is committed with Space afterwards, so the report shows both the
    composing text and the text that actually landed in the box.

Requires an unlocked interactive desktop: on a locked session SendInput goes to the
Winlogon desktop, the app receives nothing, and any "result" would be meaningless. The
script checks the input desktop up front and exits with 2 instead.

Exit code: 0 when the measured result matches -ExpectCandidates (or when no expectation
is given), 1 when a measurement or the expectation failed, 2 when the environment cannot
run the check (locked desktop, missing files).

.PARAMETER PortableRoot
Extracted portable folder that contains Files.exe and lib\Files.exe.

.PARAMETER ExpectCandidates
Expected candidate window state. Auto means: Yes for Identity, No for Plain.

.PARAMETER ReportPath
Optional path for the JSON result. Defaults to %TEMP%\files-ime-<mode>.json.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PortableRoot,
    [ValidateSet('Identity', 'Plain')][string]$Mode = 'Identity',
    [string]$ReportPath = '',
    [ValidateSet('Auto', 'Yes', 'No')][string]$ExpectCandidates = 'Auto',
    [int]$StartupTimeoutSeconds = 90,
    [string]$SampleName = 'sample'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path ([System.IO.Path]::GetTempPath()) "files-ime-$($Mode.ToLowerInvariant()).json"
}

$identityPackageName = 'Files.Internal.Portable.Identity'
$result = [ordered]@{
    Mode              = $Mode
    PortableRoot      = $PortableRoot
    InputDesktop      = $null
    ForegroundProcess = $null
    Started           = $false
    Foreground        = $false
    ItemFocused       = $false
    ProcessIdentity   = $null
    LaunchCommandLines = @()
    ImeState          = $null
    EditAutomationId  = $null
    EditCandidates    = @()
    OriginalText      = $null
    CompositionText   = $null
    CommittedText     = $null
    CandidateVisible  = $false
    CandidateWindows  = @()
    NewCandidateWindows = @()
    CandidateTexts    = @()
    WindowTitle       = $null
    Identity          = $null
    ExpectCandidates  = $null
    Pass              = $false
    Skipped           = $false
    Error             = $null
}

function Fail-Result([string]$message, [int]$exitCode) {
    $result.Error = $message
    $result.Skipped = $exitCode -eq 2
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 6
    exit $exitCode
}

if (-not (Test-Path -LiteralPath (Join-Path $PortableRoot 'Files.exe') -PathType Leaf)) {
    Fail-Result "Launcher not found: $(Join-Path $PortableRoot 'Files.exe')" 2
}

if (-not ('FilesImeTest' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

public static class FilesImeTest
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] static extern bool CloseDesktop(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    // Name of the process that owns the foreground window, e.g. "LockApp" on the lock screen.
    public static string ForegroundProcessName()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return "none";

        uint pid;
        GetWindowThreadProcessId(foreground, out pid);
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return "pid:" + pid;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size)) return "pid:" + pid;

            var path = buffer.ToString();
            var slash = path.LastIndexOf('\\');
            var name = slash >= 0 ? path.Substring(slash + 1) : path;
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPackageFullName(IntPtr process, ref int length, StringBuilder name);

    public static string ProcessIdentity(int pid)
    {
        var handle = OpenProcess(0x1000, false, (uint)pid);
        if (handle == IntPtr.Zero) return "open-failed";
        try
        {
            int length = 0;
            int hr = GetPackageFullName(handle, ref length, null);
            if (hr == 15700) return "none";
            if (length <= 0) return "hr:" + hr;

            var buffer = new StringBuilder(length);
            hr = GetPackageFullName(handle, ref length, buffer);
            return hr == 0 ? buffer.ToString() : "hr:" + hr;
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")] static extern bool ImmGetConversionStatus(IntPtr hImc, out int conversion, out int sentence);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hImc);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION u; }
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);

    const int IME_CMODE_NATIVE = 0x0001;

    public static void FreezeDpi() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }

    public static void Key(ushort vk, bool up)
    {
        var input = new INPUT { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    // "Default" while the interactive session is unlocked, "Winlogon" on the lock screen.
    public static string InputDesktopName()
    {
        var handle = OpenInputDesktop(0, false, 0x0100);
        if (handle == IntPtr.Zero) return "error:" + Marshal.GetLastWin32Error();
        try
        {
            var buffer = new StringBuilder(256);
            int needed;
            if (!GetUserObjectInformation(handle, 2, buffer, buffer.Capacity * 2, out needed))
                return "error:" + Marshal.GetLastWin32Error();
            return buffer.ToString();
        }
        finally { CloseDesktop(handle); }
    }

    public static string ImeState(IntPtr hWnd)
    {
        var context = ImmGetContext(hWnd);
        if (context == IntPtr.Zero) return "no-context";
        try
        {
            int conversion, sentence;
            if (!ImmGetConversionStatus(context, out conversion, out sentence)) return "status-failed";
            return ((conversion & IME_CMODE_NATIVE) != 0 ? "native" : "alphanumeric") + "/0x" + conversion.ToString("X");
        }
        finally { ImmReleaseContext(hWnd, context); }
    }

    public static bool TryTakeForeground(IntPtr hWnd)
    {
        if (GetForegroundWindow() == hWnd) return true;

        RECT r;
        if (GetWindowRect(hWnd, out r))
        {
            Click(r.Left + (r.Right - r.Left) / 2, r.Top + 8);
            System.Threading.Thread.Sleep(400);
            if (GetForegroundWindow() == hWnd) return true;
        }

        uint foregroundPid;
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out foregroundPid);
        var attached = AttachThreadInput(GetCurrentThreadId(), foregroundThread, true);
        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);
        if (attached) AttachThreadInput(GetCurrentThreadId(), foregroundThread, false);
        System.Threading.Thread.Sleep(400);
        return GetForegroundWindow() == hWnd;
    }

    static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
    static string TitleOf(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }

    // Status bars, settings panels, update windows and the like are not candidate windows.
    static bool IsCandidateClass(string cls)
    {
        if (cls.IndexOf("statusbar", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (cls.IndexOf("NativeCandidate", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (cls.IndexOf("Candidate", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (cls.IndexOf("Mozc", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (cls.IndexOf("CiceroUIWndFrame", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // Every visible candidate window, as "class#pid|rect".
    public static string[] VisibleCandidates()
    {
        var list = new List<string>();
        EnumWindows((h, l) =>
        {
            var cls = ClassOf(h);
            if (!IsCandidateClass(cls)) return true;
            if (!IsWindowVisible(h)) return true;

            RECT r;
            GetWindowRect(h, out r);
            if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return true;

            uint pid;
            GetWindowThreadProcessId(h, out pid);
            list.Add(string.Format("{0}#{1}|{2},{3}x{4}", cls, pid, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    public static IntPtr[] VisibleCandidateHandles()
    {
        var list = new List<IntPtr>();
        EnumWindows((h, l) =>
        {
            var cls = ClassOf(h);
            if (!IsCandidateClass(cls)) return true;
            if (!IsWindowVisible(h)) return true;

            RECT r;
            GetWindowRect(h, out r);
            if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return true;

            list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    // Broader diagnostic list: every IME related window with its state.
    public static string[] ImeWindows()
    {
        var list = new List<string>();
        EnumWindows((h, l) =>
        {
            var cls = ClassOf(h);
            var title = TitleOf(h);
            bool interesting =
                cls.IndexOf("WeType", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("Mozc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("Cicero", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("Candidate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("candidate", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!interesting) return true;

            RECT r;
            GetWindowRect(h, out r);
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            list.Add(string.Format(
                "class='{0}' title='{1}' pid={2} visible={3} rect=({4},{5})-({6},{7}) size={8}x{9}",
                cls, title, pid, IsWindowVisible(h), r.Left, r.Top, r.Right, r.Bottom, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
'@
}
[FilesImeTest]::FreezeDpi()

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# --- environment gates ---------------------------------------------------------------
$result.InputDesktop = [FilesImeTest]::InputDesktopName()
$result.ForegroundProcess = [FilesImeTest]::ForegroundProcessName()

if ($result.InputDesktop -ne 'Default' -or $result.ForegroundProcess -in @('LockApp', 'LogonUI')) {
    Fail-Result ("The interactive desktop is not unlocked (input desktop: {0}, foreground process: {1}). Unlock the session or run this inside Windows Sandbox." -f $result.InputDesktop, $result.ForegroundProcess) 2
}

# --- prepare state -------------------------------------------------------------------
Get-Process Files -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

if ($Mode -eq 'Plain') {
    Get-AppxPackage -Name $identityPackageName -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
    $env:FILES_DISABLE_PORTABLE_IDENTITY = '1'
}
else {
    Remove-Item Env:\FILES_DISABLE_PORTABLE_IDENTITY -ErrorAction SilentlyContinue
}

$sampleFolder = Join-Path ([System.IO.Path]::GetTempPath()) ("files-ime-sample-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
Remove-Item -LiteralPath (Join-Path ([System.IO.Path]::GetTempPath()) 'files-ime-sample') -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $sampleFolder | Out-Null
Set-Content -LiteralPath (Join-Path $sampleFolder "$SampleName.txt") -Value 'ime sample' -Encoding UTF8

# --- launch --------------------------------------------------------------------------
Start-Process -FilePath (Join-Path $PortableRoot 'Files.exe') -ArgumentList $sampleFolder -WorkingDirectory $PortableRoot | Out-Null

$process = $null
for ($i = 0; $i -lt ($StartupTimeoutSeconds * 4); $i++) {
    Start-Sleep -Milliseconds 250
    $process = Get-Process Files -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($process) { break }
}
if (-not $process) { Fail-Result 'Files did not create a window (check Data\logs and %TEMP%\files-startup-error.txt).' 1 }

$result.Started = $true
$result.WindowTitle = $process.MainWindowTitle
$result.LaunchCommandLines = @(Get-CimInstance Win32_Process -Filter "Name='Files.exe'" -ErrorAction SilentlyContinue | ForEach-Object { "pid=$($_.ProcessId) cmd=$($_.CommandLine)" })
Start-Sleep -Seconds 5
$result.ProcessIdentity = [FilesImeTest]::ProcessIdentity($process.Id)

$package = Get-AppxPackage -Name $identityPackageName -ErrorAction SilentlyContinue
$result.Identity = if ($package) { $package.PackageFullName } else { 'not registered' }

$hwnd = $process.MainWindowHandle
$result.Foreground = [FilesImeTest]::TryTakeForeground($hwnd)
if (-not $result.Foreground) {
    Fail-Result 'The Files window could not be activated. This usually means another window keeps the foreground.' 2
}

# --- locate the sample list item -----------------------------------------------------
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$listItemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)

$listItem = $null
$seenItems = @()
for ($i = 0; $i -lt 60 -and -not $listItem; $i++) {
    $items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItemCondition)
    $seenItems = @($items | ForEach-Object { $_.Current.Name })
    foreach ($item in $items) {
        if ($item.Current.Name -like "$SampleName*") { $listItem = $item; break }
    }
    if (-not $listItem) { Start-Sleep -Milliseconds 250 }
}
if (-not $listItem) {
    Fail-Result ("The sample item '{0}.txt' was not listed (window '{1}', identity '{2}', items: {3})." -f $SampleName, $process.MainWindowTitle, $result.ProcessIdentity, ($seenItems -join ', ')) 1
}

$editCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)

function Get-RenameEdit {
    param($Root, $SampleName)

    $edits = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
    $best = $null
    $bestScore = 0
    $diagnostics = New-Object System.Collections.ArrayList

    foreach ($edit in $edits) {
        $automationId = ''
        $value = $null
        $focused = $false
        $offscreen = $true
        try { $automationId = $edit.Current.AutomationId } catch { }
        try { $focused = $edit.Current.HasKeyboardFocus } catch { }
        try { $offscreen = $edit.Current.IsOffscreen } catch { }
        try {
            $pattern = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            $value = $pattern.Current.Value
        }
        catch { }

        $score = 0
        if ($automationId -like '*TextBoxItemName') { $score += 4 }
        if ($value -like "$SampleName*") { $score += 3 }
        if ($focused) { $score += 2 }
        if (-not $offscreen) { $score += 1 }

        [void]$diagnostics.Add("id='$automationId' value='$value' focused=$focused offscreen=$offscreen score=$score")
        if ($score -gt $bestScore) { $bestScore = $score; $best = $edit }
    }

    return @{ Edit = $best; Score = $bestScore; Diagnostics = @($diagnostics) }
}

# --- enter rename --------------------------------------------------------------------
$rename = $null
for ($attempt = 1; $attempt -le 3 -and -not $rename; $attempt++) {
    # A click selects and focuses the item; SetFocus alone does not give the list keyboard focus.
    $rect = $listItem.Current.BoundingRectangle
    if ($rect.Width -gt 0 -and $rect.Height -gt 0) {
        [FilesImeTest]::Click([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2))
    }
    Start-Sleep -Milliseconds 500
    if (-not $listItem.Current.HasKeyboardFocus) {
        try { $listItem.SetFocus() } catch { }
        Start-Sleep -Milliseconds 300
    }
    $result.ItemFocused = $listItem.Current.HasKeyboardFocus

    [FilesImeTest]::Key(0x71, $false)   # F2
    Start-Sleep -Milliseconds 120
    [FilesImeTest]::Key(0x71, $true)

    for ($i = 0; $i -lt 16; $i++) {
        Start-Sleep -Milliseconds 250
        $candidate = Get-RenameEdit -Root $root -SampleName $SampleName
        $result.EditCandidates = $candidate.Diagnostics
        # 4 points come from the item template name, 3 from the sample file name.
        if ($candidate.Edit -and $candidate.Score -ge 4) { $rename = $candidate; break }
    }
}

if (-not $rename) {
    Fail-Result ("The rename text box did not appear after F2. Edits seen: {0}" -f (@($result.EditCandidates) -join ' ; ')) 1
}

$edit = $rename.Edit
$result.EditAutomationId = $edit.Current.AutomationId
try { $edit.SetFocus() } catch { }
Start-Sleep -Milliseconds 400

try {
    $result.OriginalText = ($edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
}
catch { }

$result.ImeState = [FilesImeTest]::ImeState($hwnd)
$before = @([FilesImeTest]::VisibleCandidates())

# --- type pinyin and measure ----------------------------------------------------------
if (-not [FilesImeTest]::TryTakeForeground($hwnd)) {
    Fail-Result 'The Files window lost the foreground before typing.' 2
}

foreach ($vk in @(0x4E, 0x49, 0x48, 0x41, 0x4F)) {   # n i h a o
    [FilesImeTest]::Key([uint16]$vk, $false)
    Start-Sleep -Milliseconds 80
    [FilesImeTest]::Key([uint16]$vk, $true)
    Start-Sleep -Milliseconds 180
}
Start-Sleep -Milliseconds 900

try {
    $result.CompositionText = ($edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
}
catch { }

$result.CandidateWindows = @([FilesImeTest]::ImeWindows())
$after = @([FilesImeTest]::VisibleCandidates())
$beforeKeys = @($before | ForEach-Object { ($_ -split '\|')[0] })
$new = @($after | Where-Object { $beforeKeys -notcontains ($_ -split '\|')[0] })
$result.NewCandidateWindows = $new
$result.CandidateVisible = $new.Count -gt 0

# Read the candidate list text so the report shows what the IME offered.
if ($result.CandidateVisible) {
    $texts = New-Object System.Collections.ArrayList
    foreach ($candidateHandle in [FilesImeTest]::VisibleCandidateHandles()) {
        try {
            $element = [System.Windows.Automation.AutomationElement]::FromHandle($candidateHandle)
            $children = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($child in $children) {
                $name = $child.Current.Name
                if (-not [string]::IsNullOrWhiteSpace($name)) { [void]$texts.Add($name) }
                if ($texts.Count -ge 12) { break }
            }
        }
        catch { }
    }
    $result.CandidateTexts = @($texts | Select-Object -Unique)
}

# --- commit with the IME confirmation key --------------------------------------------
[FilesImeTest]::Key(0x20, $false)   # Space
Start-Sleep -Milliseconds 120
[FilesImeTest]::Key(0x20, $true)
Start-Sleep -Milliseconds 600

try {
    $result.CommittedText = ($edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
}
catch { }

# --- verdict -------------------------------------------------------------------------
$expected = switch ($ExpectCandidates) {
    'Yes' { $true }
    'No' { $false }
    default { $Mode -eq 'Identity' }
}
$result.ExpectCandidates = $expected

$textLanded = $result.CommittedText -and ($result.CommittedText -ne $result.OriginalText)
$result.Pass = [bool]($textLanded -and ($result.CandidateVisible -eq $expected))

Get-Process Files -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item Env:\FILES_DISABLE_PORTABLE_IDENTITY -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $sampleFolder -Recurse -Force -ErrorAction SilentlyContinue

$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
$result | ConvertTo-Json -Depth 6
if ($result.Pass) { exit 0 } else { exit 1 }
