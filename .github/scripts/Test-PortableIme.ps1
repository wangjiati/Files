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

Requires an unlocked interactive desktop: Windows refuses foreground activation for
background processes, and IME composition only reaches the focused window. The script
clicks the window title bar and falls back to AttachThreadInput to obtain the foreground.

Exit code: 0 when the measured result matches -ExpectCandidates (or when no expectation
is given), 1 otherwise, 2 when the environment cannot run the check (locked desktop,
missing files).

.PARAMETER PortableRoot
Extracted portable folder that contains Files.exe and lib\Files.exe.

.PARAMETER ReportPath
Optional path for the JSON result. Defaults next to the portable root.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PortableRoot,
    [ValidateSet('Identity', 'Plain')][string]$Mode = 'Identity',
    [string]$ReportPath = '',
    [ValidateSet('Auto', 'Yes', 'No')][string]$ExpectCandidates = 'Auto',
    [int]$StartupTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path ([System.IO.Path]::GetTempPath()) "files-ime-$($Mode.ToLowerInvariant()).json"
}

$identityPackageName = 'Files.Internal.Portable.Identity'
$result = [ordered]@{
    Mode             = $Mode
    PortableRoot     = $PortableRoot
    Started          = $false
    Foreground       = $false
    CompositionText  = $null
    CandidateVisible = $false
    CandidateWindows = @()
    WindowTitle      = $null
    Identity         = $null
    ExpectCandidates = $null
    Pass             = $false
    Skipped          = $false
    Error            = $null
}

function Fail-Result([string]$message, [int]$exitCode) {
    $result.Error = $message
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
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION u; }
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);

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

    public static string[] CandidateWindows()
    {
        var list = new List<string>();
        EnumWindows((h, l) =>
        {
            var cls = new StringBuilder(256);
            GetClassName(h, cls, cls.Capacity);
            var title = new StringBuilder(256);
            GetWindowText(h, title, title.Capacity);
            var name = cls.ToString();
            var text = title.ToString();
            bool interesting =
                name.IndexOf("WeType", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("wetype", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Mozc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Cicero", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Candidate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("candidate", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!interesting) return true;

            RECT r;
            GetWindowRect(h, out r);
            int cloaked = -1;
            try { DwmGetWindowAttribute(h, 14, out cloaked, sizeof(int)); } catch { }
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            list.Add(string.Format(
                "class='{0}' title='{1}' pid={2} visible={3} rect=({4},{5})-({6},{7}) size={8}x{9}",
                name, text, pid, IsWindowVisible(h), r.Left, r.Top, r.Right, r.Bottom, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }
}
'@
}
[FilesImeTest]::FreezeDpi()

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

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

$sampleFolder = Join-Path ([System.IO.Path]::GetTempPath()) 'files-ime-sample'
if (Test-Path -LiteralPath $sampleFolder) { Remove-Item -LiteralPath $sampleFolder -Recurse -Force }
New-Item -ItemType Directory -Path $sampleFolder | Out-Null
Set-Content -LiteralPath (Join-Path $sampleFolder 'sample.txt') -Value 'ime sample' -Encoding UTF8

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
Start-Sleep -Seconds 5

$package = Get-AppxPackage -Name $identityPackageName -ErrorAction SilentlyContinue
$result.Identity = if ($package) { $package.PackageFullName } else { 'not registered' }

$hwnd = $process.MainWindowHandle
$result.Foreground = [FilesImeTest]::TryTakeForeground($hwnd)
if (-not $result.Foreground) {
    Fail-Result 'The Files window could not be activated. This usually means the desktop is locked or another window keeps the foreground.' 2
}

# --- select the sample item and start rename -----------------------------------------
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$itemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
if ($items.Count -gt 0) {
    try {
        $selection = $items.Item(0).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
    }
    catch { }
}
Start-Sleep -Milliseconds 400

[FilesImeTest]::Key(0x71, $false)   # F2
Start-Sleep -Milliseconds 200
[FilesImeTest]::Key(0x71, $true)
Start-Sleep -Milliseconds 1200

$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
$editCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edits = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
$edit = $null
if ($focused -and $focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
    $edit = $focused
}
elseif ($edits.Count -gt 0) {
    $edit = $edits.Item($edits.Count - 1)
}
if (-not $edit) { Fail-Result 'The rename text box did not appear after F2.' 1 }

try { $edit.SetFocus() } catch { }
Start-Sleep -Milliseconds 400

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
    $valuePattern = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $result.CompositionText = $valuePattern.Current.Value
}
catch { }

$windows = @([FilesImeTest]::CandidateWindows())
$result.CandidateWindows = $windows
$result.CandidateVisible = @($windows | Where-Object { $_ -match 'visible=True' -and $_ -notmatch 'size=0x0' }).Count -gt 0

# --- verdict -------------------------------------------------------------------------
$expected = switch ($ExpectCandidates) {
    'Yes' { $true }
    'No' { $false }
    default { $Mode -eq 'Identity' }
}
$result.ExpectCandidates = $expected
$result.Pass = ($result.CompositionText -ne $null) -and ($result.CandidateVisible -eq $expected)

Get-Process Files -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item Env:\FILES_DISABLE_PORTABLE_IDENTITY -ErrorAction SilentlyContinue

$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
$result | ConvertTo-Json -Depth 6
if ($result.Pass) { exit 0 } else { exit 1 }
