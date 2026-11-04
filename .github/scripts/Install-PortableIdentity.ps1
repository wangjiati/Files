# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
Registers (or removes) the optional identity package of this portable Files build.

.DESCRIPTION
Files ships without package identity when it runs from a folder. On Windows builds where
unpackaged WinUI 3 applications lose the IME candidate window (for example Windows 11
build 26300), registering this small payload-free identity package restores it. The
binaries stay in this folder; only a few kilobytes of package registration are added.

The launcher does this automatically on first start. Run this script when the launcher
could not register the package (for example because the signing certificate is not
trusted yet).

.PARAMETER Root
Portable folder that contains lib\Files.exe. Defaults to the folder of this script.

.PARAMETER TrustCertificate
Import the signing certificate into LocalMachine\TrustedPeople. Requires an elevated
shell. Only needed once per machine for self-signed internal builds.

.PARAMETER Uninstall
Remove the identity package registration.
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$TrustCertificate,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($Root)) { $Root = (Get-Location).Path }
}

$identityName = 'Files.Internal.Portable.Identity'
$identityFile = Join-Path $Root "$identityName.msix"

function Fail([string]$message) {
    Write-Host "error: $message" -ForegroundColor Red
    exit 1
}

if ($Uninstall) {
    $packages = @(Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue)
    if ($packages.Count -eq 0) {
        Write-Host 'The identity package is not registered.'
        return
    }

    foreach ($package in $packages) { Remove-AppxPackage -Package $package.PackageFullName }
    Write-Host 'The identity package was removed. Files keeps working without package identity.'
    return
}

if (-not (Test-Path -LiteralPath $identityFile -PathType Leaf)) {
    Fail "Identity package not found: $identityFile"
}

$signature = Get-AuthenticodeSignature -LiteralPath $identityFile
if ($signature.Status -ne 'Valid') {
    Write-Host ("The identity package signature is '{0}'." -f $signature.Status) -ForegroundColor Yellow
    if ($TrustCertificate) {
        $isElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        if (-not $isElevated) { Fail 'TrustCertificate requires an elevated PowerShell window.' }
        if (-not $signature.SignerCertificate) { Fail 'The package has no signer certificate to trust.' }

        $certificatePath = Join-Path $Root 'Files.Internal.cer'
        if (Test-Path -LiteralPath $certificatePath) {
            Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        }
        else {
            Export-Certificate -Cert $signature.SignerCertificate -FilePath $certificatePath | Out-Null
            Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        }
        Write-Host 'The signing certificate was added to LocalMachine\TrustedPeople.'
    }
    else {
        Write-Host 'Re-run with -TrustCertificate from an elevated shell, or deploy the certificate through your normal channel.' -ForegroundColor Yellow
        Write-Host 'Files still runs without identity; only the IME candidate window stays affected on the impacted Windows builds.'
        exit 1
    }
}

$existing = @(Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue)
foreach ($package in $existing) {
    Write-Host ("Removing the previous registration ({0}) ..." -f $package.Version)
    Remove-AppxPackage -Package $package.PackageFullName
}

Write-Host ("Registering {0} for {1} ..." -f $identityName, $Root)
try {
    Add-AppxPackage -Path $identityFile -ExternalLocation $Root -ErrorAction Stop
}
catch {
    $message = $_.Exception.Message
    if ($message -match '0x800B0109') { Fail 'The signing certificate is not trusted. Run this script with -TrustCertificate from an elevated shell.' }
    if ($message -match '0x80073D06|0x80073CFB') { Fail 'A different version is still registered. Uninstall it first with -Uninstall.' }
    Fail "Registration failed: $message"
}

$package = Get-AppxPackage -Name $identityName | Select-Object -First 1
Write-Host ("Registered: {0} ({1})" -f $package.PackageFullName, $package.Version) -ForegroundColor Green
Write-Host 'Restart Files to pick up the identity. IME candidate windows should now work.'
