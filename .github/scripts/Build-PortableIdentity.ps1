# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
Builds the payload-free identity package for the portable distribution.

.DESCRIPTION
The portable build ships without package identity. On Windows builds where unpackaged
WinUI 3 applications lose the IME candidate window, registration of this identity
package (packaging with external location) restores it while the binaries stay in the
portable folder.

The produced MSIX contains only AppxManifest.xml and assets. It is signed with the
same certificate as the internal MSIX and is placed at the root of the portable
folder, where the launcher registers it on first start.

.PARAMETER PortableRoot
Portable staging directory (the folder that contains lib\Files.exe and Files.exe).

.PARAMETER Version
Version written to the package identity. Must match the application version.

.PARAMETER CertificatePath
PFX/P12 signing certificate whose subject matches Package.appxmanifest Publisher.

.PARAMETER CertificatePassword
Certificate password. Defaults to FILES_INTERNAL_CERT_PASSWORD.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PortableRoot,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$CertificatePath,
    [string]$CertificatePassword = $env:FILES_INTERNAL_CERT_PASSWORD,
    [string]$PackageName = "Files.Internal.Portable.Identity",
    [string]$ApplicationId = "FilesPortable",
    [string]$Publisher = "CN=Files"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Keep in sync with Package.appxmanifest / AppxDefaultResourceQualifiers. Without the full
# list the packaged resource context falls back to a single language and the UI switches.
$Languages = @(
    "en-US", "af", "ar", "be-BY", "bg", "ca", "cs-CZ", "da", "de-DE", "el", "en-GB",
    "es-ES", "es-419", "fa-IR", "fi-FI", "fil-PH", "fr-FR", "he-IL", "hi-IN", "hr-HR",
    "hu-HU", "hy-AM", "id-ID", "it-IT", "ja-JP", "ka", "km-KH", "ko-KR", "lt-LT", "lv-LV",
    "ms-MY", "nb-NO", "nl-NL", "pl-PL", "pt-BR", "pt-PT", "ro-RO", "ru-RU", "sk-SK",
    "sq-AL", "sr-Cyrl", "sv-SE", "ta", "th-TH", "tr-TR", "uk-UA", "vi", "zh-Hans", "zh-Hant"
)

function Fail([string]$message) {
    Write-Host "error: $message" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path -LiteralPath $PortableRoot -PathType Container)) { Fail "Portable root not found: $PortableRoot" }
if (-not (Test-Path -LiteralPath (Join-Path $PortableRoot "lib\Files.exe") -PathType Leaf)) { Fail "Portable root has no lib\Files.exe: $PortableRoot" }
if ([string]::IsNullOrWhiteSpace($CertificatePassword)) { Fail "A signing certificate password is required." }
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { Fail "Certificate not found: $CertificatePath" }

function Find-WindowsSdkTool([string]$name) {
    $tool = Get-Command $name -ErrorAction SilentlyContinue
    if ($null -ne $tool) { return $tool.Source }

    $kitsRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)) "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $kitsRoot)) { Fail "$name was not found. Install the Windows SDK." }

    $candidate = Get-ChildItem -LiteralPath $kitsRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1

    if (-not $candidate) { Fail "$name was not found under $kitsRoot." }
    return $candidate
}

$makeappx = Find-WindowsSdkTool "makeappx.exe"
$signtool = Find-WindowsSdkTool "signtool.exe"

$layout = Join-Path ([System.IO.Path]::GetTempPath()) ("files-portable-identity-" + [Guid]::NewGuid().ToString("N"))
$assets = Join-Path $layout "Assets"
New-Item -ItemType Directory -Path $assets -Force | Out-Null

try {
    Add-Type -AssemblyName System.Drawing
    $logos = @{ "StoreLogo.png" = 50; "Square44x44Logo.png" = 44; "Square150x150Logo.png" = 150 }
    foreach ($logo in $logos.GetEnumerator()) {
        $size = [int]$logo.Value
        $bitmap = New-Object System.Drawing.Bitmap $size, $size
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::FromArgb(255, 0, 120, 200))
        $graphics.Dispose()
        $bitmap.Save((Join-Path $assets $logo.Key), [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
    }

    $resourceLines = ($Languages | ForEach-Object { "    <Resource Language=`"$_`" />" }) -join "`r`n"
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap10 rescap">
  <Identity
    Name="$PackageName"
    Publisher="$Publisher"
    Version="$Version"
    ProcessorArchitecture="neutral" />
  <Properties>
    <DisplayName>Files Internal Portable Identity</DisplayName>
    <PublisherDisplayName>Files</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
    <uap10:AllowExternalContent>true</uap10:AllowExternalContent>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
$resourceLines
  </Resources>
  <Applications>
    <Application Id="$ApplicationId" Executable="lib\Files.exe" uap10:TrustLevel="mediumIL" uap10:RuntimeBehavior="win32App">
      <uap:VisualElements
        AppListEntry="none"
        BackgroundColor="transparent"
        Description="Files - Internal Portable"
        DisplayName="Files - Internal Portable"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
    <rescap:Capability Name="unvirtualizedResources" />
  </Capabilities>
</Package>
"@
    [System.IO.File]::WriteAllText((Join-Path $layout "AppxManifest.xml"), $manifest, (New-Object System.Text.UTF8Encoding($false)))

    $output = Join-Path $PortableRoot "$PackageName.msix"
    Remove-Item -LiteralPath $output -ErrorAction SilentlyContinue

    & $makeappx pack /d $layout /p $output /o /nv | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "makeappx failed with exit code $LASTEXITCODE." }

    & $signtool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $output | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "signtool failed with exit code $LASTEXITCODE." }

    $signature = Get-AuthenticodeSignature -LiteralPath $output
    Write-Host ("    Portable identity package: {0} ({1} bytes, signature: {2})" -f (Split-Path $output -Leaf), (Get-Item -LiteralPath $output).Length, $signature.Status)
}
finally {
    Remove-Item -LiteralPath $layout -Recurse -Force -ErrorAction SilentlyContinue
}
