# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
Automated acceptance test for the internal portable release.

.DESCRIPTION
Extracts the portable ZIP and verifies, without user interaction:

  * payload completeness (launcher, lib, merged resource index, identity package, install script)
  * identity package signature and RELEASE-INFO contents
  * launcher capabilities (QA opt-out and package-repository fallback)
  * plain launch: app starts without package identity and without the startup error page
  * identity launch: the launcher registers the package and the app process carries package identity

Add -IncludeUi to also run Test-PortableIme.ps1 for the Plain and Identity modes. That part
needs an unlocked interactive desktop; the headless checks work on a locked desktop as well.

Exit code 0 = all checks passed, 1 = a check failed, 2 = environment could not run the test.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [switch]$IncludeUi,
    [switch]$TrustCertificate,
    [string]$WorkDirectory = '',
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("files-release-test-" + [Guid]::NewGuid().ToString('N'))
}
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path ([System.IO.Path]::GetTempPath()) 'files-release-test.json'
}

$identityPackageName = 'Files.Internal.Portable.Identity'
$checks = New-Object System.Collections.ArrayList
$uiResults = New-Object System.Collections.ArrayList

function Add-Check([string]$name, [bool]$passed, [string]$detail) {
    [void]$checks.Add([ordered]@{ Name = $name; Passed = $passed; Detail = $detail })
    Write-Host ("[{0}] {1} - {2}" -f $(if ($passed) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

function Save-Report([int]$exitCode) {
    $report = [ordered]@{
        Zip        = $ZipPath
        WorkDir    = $WorkDirectory
        Checks     = @($checks)
        Ui         = @($uiResults)
        Passed     = (@($checks | Where-Object { -not $_.Passed }).Count -eq 0) -and (@($uiResults | Where-Object { -not $_.Pass }).Count -eq 0)
        ExitCode   = $exitCode
        Timestamp  = (Get-Date).ToString('o')
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    Write-Host ("Report: {0}" -f $ReportPath)
    exit $exitCode
}

if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    Write-Host "error: ZIP not found: $ZipPath" -ForegroundColor Red
    exit 2
}

if (-not ('ReleaseTestNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class ReleaseTestNative
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPackageFullName(IntPtr process, ref int length, StringBuilder name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    public static string IdentityOf(int pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
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
}
'@
}

# --- extract -------------------------------------------------------------------------
if (Test-Path -LiteralPath $WorkDirectory) { Remove-Item -LiteralPath $WorkDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $WorkDirectory | Out-Null
$portable = Join-Path $WorkDirectory 'portable'
Expand-Archive -LiteralPath $ZipPath -DestinationPath $portable -Force

$launcher = Join-Path $portable 'Files.exe'
$appExe = Join-Path $portable 'lib\Files.exe'
$pri = Join-Path $portable 'lib\Files.pri'
$identityMsix = Join-Path $portable "$identityPackageName.msix"
$installer = Join-Path $portable 'Install-PortableIdentity.ps1'
$releaseInfoPath = Join-Path $portable 'RELEASE-INFO.txt'

Add-Check 'launcher' (Test-Path -LiteralPath $launcher) $launcher
Add-Check 'app executable' (Test-Path -LiteralPath $appExe) $appExe
Add-Check 'identity package' (Test-Path -LiteralPath $identityMsix) $identityMsix
Add-Check 'install script' (Test-Path -LiteralPath $installer) $installer
Add-Check 'release info' (Test-Path -LiteralPath $releaseInfoPath) $releaseInfoPath

if (Test-Path -LiteralPath $pri) {
    $priSize = (Get-Item -LiteralPath $pri).Length
    # The merged index must include the WinUI theme resources; the broken merge is ~4.3 MB.
    Add-Check 'resource index' ($priSize -gt 5MB) ("{0:N0} bytes" -f $priSize)
}
else {
    Add-Check 'resource index' $false 'Files.pri missing'
}

$launcherText = [System.Text.Encoding]::Unicode.GetString([System.IO.File]::ReadAllBytes($launcher))
Add-Check 'launcher QA opt-out' ($launcherText.Contains('FILES_DISABLE_PORTABLE_IDENTITY')) 'FILES_DISABLE_PORTABLE_IDENTITY'
Add-Check 'launcher repository fallback' ($launcherText.Contains('family name resolved from the package repository')) 'package repository lookup'

$signatureStatus = 'missing'
if (Test-Path -LiteralPath $identityMsix) {
    $signatureStatus = (Get-AuthenticodeSignature -LiteralPath $identityMsix).Status
    Add-Check 'identity signature' ($signatureStatus -in 'Valid', 'UnknownError') $signatureStatus
}

if (Test-Path -LiteralPath $releaseInfoPath) {
    $releaseInfo = Get-Content -LiteralPath $releaseInfoPath
    $commit = ($releaseInfo | Where-Object { $_ -like 'Commit:*' }) -replace '^Commit:\s*', ''
    $tree = ($releaseInfo | Where-Object { $_ -like 'Working-Tree:*' }) -replace '^Working-Tree:\s*', ''
    Add-Check 'release metadata' ([bool]$commit) ("commit={0} tree={1}" -f $commit, $tree)
}

# --- plain launch --------------------------------------------------------------------
function Stop-Files { Get-Process Files -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 800 }

function Wait-Window([int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $process = Get-Process Files -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($process) { return $process }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

Stop-Files
Get-AppxPackage -Name $identityPackageName -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
$startupError = Join-Path $env:TEMP 'files-startup-error.txt'
Remove-Item -LiteralPath $startupError -ErrorAction SilentlyContinue

$env:FILES_DISABLE_PORTABLE_IDENTITY = '1'
Start-Process -FilePath $launcher -WorkingDirectory $portable | Out-Null
$plainProcess = Wait-Window 60
Add-Check 'plain launch window' ($null -ne $plainProcess) $(if ($plainProcess) { $plainProcess.MainWindowTitle } else { 'no window' })
Add-Check 'plain launch title' ([bool]($plainProcess -and -not [string]::IsNullOrWhiteSpace($plainProcess.MainWindowTitle))) $(if ($plainProcess) { $plainProcess.MainWindowTitle } else { '' })
Add-Check 'plain startup error' (-not (Test-Path -LiteralPath $startupError)) $(if (Test-Path -LiteralPath $startupError) { 'files-startup-error.txt present' } else { 'none' })
if ($plainProcess) {
    $plainIdentity = [ReleaseTestNative]::IdentityOf($plainProcess.Id)
    Add-Check 'plain has no identity' ($plainIdentity -eq 'none') $plainIdentity
}
Stop-Files
Remove-Item Env:\FILES_DISABLE_PORTABLE_IDENTITY -ErrorAction SilentlyContinue

# --- identity launch -----------------------------------------------------------------
$trusted = $false
try {
    $trusted = @(Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue | Where-Object { $_.Subject -eq 'CN=Files' }).Count -gt 0
}
catch { }

if (-not $trusted -and $TrustCertificate) {
    $isElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isElevated -and (Test-Path -LiteralPath $identityMsix)) {
        $signer = (Get-AuthenticodeSignature -LiteralPath $identityMsix).SignerCertificate
        if ($signer) {
            $exported = Join-Path $WorkDirectory 'identity.cer'
            Export-Certificate -Cert $signer -FilePath $exported | Out-Null
            Import-Certificate -FilePath $exported -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
            $trusted = $true
        }
    }
}

if ($trusted) {
    Get-AppxPackage -Name $identityPackageName -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath 'HKCU:\Software\Files\PortableIdentity' -Recurse -Force -ErrorAction SilentlyContinue

    Start-Process -FilePath $launcher -WorkingDirectory $portable | Out-Null
    $identityProcess = Wait-Window 60
    Add-Check 'identity launch window' ($null -ne $identityProcess) $(if ($identityProcess) { $identityProcess.MainWindowTitle } else { 'no window' })

    $package = $null
    for ($i = 0; $i -lt 20 -and $null -eq $package; $i++) {
        Start-Sleep -Milliseconds 250
        $package = Get-AppxPackage -Name $identityPackageName -ErrorAction SilentlyContinue
    }
    Add-Check 'identity package registered' ($null -ne $package) $(if ($package) { $package.PackageFullName } else { 'not registered' })

    if ($identityProcess) {
        $identity = [ReleaseTestNative]::IdentityOf($identityProcess.Id)
        Add-Check 'process identity' ($identity -like "$identityPackageName*") $identity
    }

    $launcherLog = Join-Path $env:TEMP 'files-launcher.log'
    if (Test-Path -LiteralPath $launcherLog) {
        $logTail = (Get-Content -LiteralPath $launcherLog -Tail 6) -join ' | '
        Add-Check 'launcher log' (-not ($logTail -match 'family name unavailable')) $logTail
    }
}
else {
    Add-Check 'identity package registered' $false 'certificate CN=Files is not in LocalMachine\TrustedPeople (use -TrustCertificate from an elevated shell)'
}
Stop-Files

# --- optional UI run -----------------------------------------------------------------
if ($IncludeUi) {
    $uiScript = Join-Path $PSScriptRoot 'Test-PortableIme.ps1'
    foreach ($mode in @('Plain', 'Identity')) {
        Write-Host ("`n==> UI test: {0}" -f $mode)
        $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $uiScript -PortableRoot $portable -Mode $mode -ReportPath (Join-Path $WorkDirectory "ui-$mode.json") 2>&1
        $exit = $LASTEXITCODE
        $json = $null
        try { $json = ($out | Where-Object { $_ -match '^\{' } | Select-Object -First 1) } catch { }
        $parsed = $null
        if ($json) { try { $parsed = $json | ConvertFrom-Json } catch { } }
        [void]$uiResults.Add([ordered]@{
            Mode             = $mode
            ExitCode         = $exit
            Pass             = ($exit -eq 0)
            CandidateVisible = if ($parsed) { $parsed.CandidateVisible } else { $null }
            CompositionText  = if ($parsed) { $parsed.CompositionText } else { $null }
            Error            = if ($parsed) { $parsed.Error } else { ($out -join ' ') }
        })
        Write-Host (($out | Select-Object -Last 25) -join "`n")
    }
}

$failed = @($checks | Where-Object { -not $_.Passed }).Count + @($uiResults | Where-Object { -not $_.Pass }).Count
Save-Report ($(if ($failed -eq 0) { 0 } else { 1 }))
