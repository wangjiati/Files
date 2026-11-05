# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
Runs the release acceptance test inside Windows Sandbox and returns the result.

.DESCRIPTION
Copies the portable ZIP and the test scripts into a payload folder, generates a Windows
Sandbox configuration that maps that folder and runs Test-ReleaseArtifacts.ps1 with the UI
leg enabled, then waits for the JSON result written back to the host.

The sandbox is a clean Windows environment with its own desktop, so the UI test can take
the foreground and drive the IME without a user present. The sandbox user is an
administrator, so the test certificate can be trusted automatically.

Requirements: Windows Sandbox feature enabled (Windows 11 Pro/Enterprise).

Exit code mirrors the inner test result (0 pass, 1 fail, 2 environment problem, 3 timeout).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [int]$TimeoutMinutes = 40,
    [switch]$IncludeUi,
    [string]$WorkDirectory = ''
)

$ErrorActionPreference = 'Stop'

$sandboxExe = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
if (-not (Test-Path -LiteralPath $sandboxExe)) {
    Write-Host 'Windows Sandbox is not available on this machine.' -ForegroundColor Red
    exit 2
}
if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    Write-Host "ZIP not found: $ZipPath" -ForegroundColor Red
    exit 2
}

if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("files-sandbox-" + [Guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $WorkDirectory) { Remove-Item -LiteralPath $WorkDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $WorkDirectory | Out-Null

$payload = Join-Path $WorkDirectory 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null

Copy-Item -LiteralPath $ZipPath -Destination (Join-Path $payload 'archive.zip') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-ReleaseArtifacts.ps1') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-PortableIme.ps1') -Destination $payload -Force

$resultPath = Join-Path $payload 'result.json'
Remove-Item -LiteralPath $resultPath -ErrorAction SilentlyContinue

$uiArgument = if ($IncludeUi) { ' -IncludeUi' } else { '' }
$wsbPath = Join-Path $WorkDirectory 'release-test.wsb'
$wsb = @"
<Configuration>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$payload</HostFolder>
      <SandboxFolder>C:\payload</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <Networking>Default</Networking>
  <MemoryInMB>8192</MemoryInMB>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\payload\Test-ReleaseArtifacts.ps1 -ZipPath C:\payload\archive.zip -TrustCertificate$uiArgument -ReportPath C:\payload\result.json</Command>
  </LogonCommand>
</Configuration>
"@
# Windows Sandbox requires UTF-16 for the configuration file.
[System.IO.File]::WriteAllText($wsbPath, $wsb, [System.Text.Encoding]::Unicode)

Write-Host ("Launching Windows Sandbox... payload: {0}" -f $payload)

Get-Process WindowsSandboxClient, WindowsSandbox -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $sandboxExe -ArgumentList "`"$wsbPath`"" | Out-Null

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $resultPath) { break }
    Start-Sleep -Seconds 2
}

if (-not (Test-Path -LiteralPath $resultPath)) {
    Write-Host 'Timed out waiting for the sandbox result.' -ForegroundColor Red
    exit 3
}

$report = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$report.Checks | ForEach-Object { Write-Host ("[{0}] {1} - {2}" -f $(if ($_.Passed) { 'PASS' } else { 'FAIL' }), $_.Name, $_.Detail) }
if ($report.Ui) {
    $report.Ui | ForEach-Object { Write-Host ("UI {0}: pass={1} candidates={2}" -f $_.Mode, $_.Pass, $_.CandidateVisible) }
}
Write-Host ("Result: {0} (report: {1})" -f $(if ($report.Passed) { 'PASS' } else { 'FAIL' }), $resultPath)
exit ([int]$report.ExitCode)
