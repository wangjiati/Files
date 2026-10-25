# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
Builds the x64 Files - Internal release artifacts.

.DESCRIPTION
Produces exactly three release outputs in the output directory:
  - a signed x64 internal MSIX package
  - a complete portable ZIP archive
  - a SHA-256 checksum manifest

A release information file is also written next to the artifacts and included
in the portable ZIP so the package version, product commit, and upstream commit
travel with the artifacts.

The signing certificate and password must be supplied by the caller. The script
never creates, installs, or copies a private key. It fails before building when
signing parameters are missing or when the certificate does not match the
package publisher.

.PARAMETER OutputDirectory
Destination for the release artifacts. Defaults to
artifacts\internal\<package-version>.

.PARAMETER UpstreamCommit
Upstream commit recorded in the release metadata. When omitted, the merge base
with origin/main (or main) is used.

.PARAMETER CertificatePath
Path to the PFX/P12 signing certificate. The certificate must contain a private
key and its subject must exactly match Package.appxmanifest Identity Publisher.

.PARAMETER CertificatePassword
Password for the signing certificate. Prefer the FILES_INTERNAL_CERT_PASSWORD
environment variable to keep the password out of shell history and parameters.

.EXAMPLE
$securePassword = Read-Host -AsSecureString "Signing certificate password"
$env:FILES_INTERNAL_CERT_PASSWORD = (New-Object System.Net.NetworkCredential("", $securePassword)).Password
.\Build-Internal.ps1 -CertificatePath C:\keys\files-internal.pfx

.EXAMPLE
.\Build-Internal.ps1 `
    -CertificatePath C:\keys\files-internal.pfx `
    -CertificatePassword $env:FILES_INTERNAL_CERT_PASSWORD `
    -OutputDirectory C:\release\files-internal
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [string]$UpstreamCommit = "",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$configuration = "Release"
$platform = "x64"
$runtimeIdentifier = "win-x64"
$productId = "Files.Internal"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$appProject = Join-Path $repoRoot "src\Files.App\Files.App.csproj"
$appManifest = Join-Path $repoRoot "src\Files.App\Package.appxmanifest"
$portablePublishProfile = "win-x64-portable"

$tempRoot = $null
$certificate = $null

function Fail([string]$Message) {
    throw "Build-Internal: $Message"
}

function Write-Utf8File([string]$Path, [string]$Text) {
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Get-GitOutput([string[]]$Arguments) {
    $output = & git -C $repoRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        Fail "git $($Arguments -join ' ') failed: $(($output | Out-String).Trim())"
    }

    return (($output | Out-String).Trim())
}

function Invoke-MSBuild([string]$Step, [string[]]$Arguments) {
    Write-Host "==> $Step"
    $started = Get-Date
    & $msbuildPath @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Fail "$Step failed with exit code $exitCode."
    }

    $elapsed = (Get-Date) - $started
    Write-Host ("    {0} completed in {1:n1} minutes." -f $Step, $elapsed.TotalMinutes)
}

function Find-MSBuild {
    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $programFilesX86 = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ProgramFilesX86)
    $vswhere = Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        Fail "msbuild was not found. Run this script from a Visual Studio Developer PowerShell."
    }

    $installationPath = (& $vswhere -latest -products "*" -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($installationPath)) {
        Fail "Visual Studio with MSBuild was not found. Install the Desktop development with C++ workload."
    }

    $devShellModule = Join-Path $installationPath "Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
    if (Test-Path -LiteralPath $devShellModule) {
        $instanceId = (& $vswhere -latest -products "*" -requires Microsoft.Component.MSBuild -property instanceId | Select-Object -First 1)
        if (-not [string]::IsNullOrWhiteSpace($instanceId)) {
            Import-Module $devShellModule
            try {
                Enter-VsDevShell $instanceId -SkipAutomaticLocation -DevCmdArguments "-arch=$platform -host_arch=$platform" -ErrorAction Stop | Out-Null
            }
            catch {
                Write-Warning "Could not initialize the Visual Studio developer environment: $($_.Exception.Message)"
            }
        }
    }

    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidate = & $vswhere -latest -products "*" -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        Fail "msbuild was not found. Run this script from a Visual Studio Developer PowerShell."
    }

    return $candidate
}

function Assert-NoPrivateKeyFiles([string]$Root, [string]$Description) {
    $forbidden = @(".pfx", ".p12", ".pvk", ".snk", ".key")
    $matches = Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $forbidden -contains $_.Extension.ToLowerInvariant() }
    if ($matches) {
        Fail "$Description contains private key material: $(($matches | ForEach-Object FullName) -join ', ')"
    }
}

function Assert-ZipHasNoPrivateKeyFiles([string]$ZipPath, [string]$Description) {
    $forbidden = @(".pfx", ".p12", ".pvk", ".snk", ".key")
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $matches = $archive.Entries |
            Where-Object { $forbidden -contains [System.IO.Path]::GetExtension($_.FullName).ToLowerInvariant() }
        if ($matches) {
            Fail "$Description contains private key material: $(($matches | ForEach-Object FullName) -join ', ')"
        }
    }
    finally {
        $archive.Dispose()
    }
}

try {
    if ($env:OS -ne "Windows_NT") {
        Fail "Internal release artifacts can only be built on Windows."
    }

    if (-not (Test-Path -LiteralPath $appProject)) {
        Fail "Application project not found at '$appProject'."
    }

    if (-not (Test-Path -LiteralPath $appManifest)) {
        Fail "Package manifest not found at '$appManifest'."
    }

    if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
        Fail "A signing certificate is required. Pass -CertificatePath or set the release environment. No unsigned or pseudo-signed package was produced."
    }

    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
        Fail "Signing certificate '$CertificatePath' was not found. No unsigned or pseudo-signed package was produced."
    }

    if ([string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $CertificatePassword = $env:FILES_INTERNAL_CERT_PASSWORD
    }
    if ([string]::IsNullOrWhiteSpace($CertificatePassword)) {
        Fail "A signing certificate password is required. Pass -CertificatePassword or set FILES_INTERNAL_CERT_PASSWORD. No unsigned or pseudo-signed package was produced."
    }

    $certificatePathResolved = (Resolve-Path -LiteralPath $CertificatePath).Path
    try {
        $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            $certificatePathResolved,
            $CertificatePassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    }
    catch {
        Fail "Could not open signing certificate '$certificatePathResolved' with the supplied password: $($_.Exception.Message)"
    }

    if (-not $certificate.HasPrivateKey) {
        Fail "Signing certificate '$certificatePathResolved' does not contain a private key."
    }
    if ($certificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow) {
        Fail "Signing certificate '$certificatePathResolved' is not valid yet (NotBefore $($certificate.NotBefore.ToUniversalTime().ToString('u')))."
    }
    if ($certificate.NotAfter.ToUniversalTime() -lt [DateTime]::UtcNow) {
        Fail "Signing certificate '$certificatePathResolved' has expired (NotAfter $($certificate.NotAfter.ToUniversalTime().ToString('u')))."
    }

    [xml]$manifestXml = Get-Content -LiteralPath $appManifest -Raw
    $version = [string]$manifestXml.Package.Identity.Version
    $manifestProductId = [string]$manifestXml.Package.Identity.Name
    $publisher = [string]$manifestXml.Package.Identity.Publisher
    $displayName = [string]$manifestXml.Package.Properties.DisplayName

    if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch "^\d+\.\d+\.\d+\.\d+$") {
        Fail "Package.appxmanifest Identity Version '$version' is not a four-part numeric version."
    }
    if ($manifestProductId -ne $productId) {
        Fail "Package identity '$manifestProductId' is not the expected internal identity '$productId'."
    }
    if ($certificate.Subject -ne $publisher) {
        Fail "Signing certificate subject '$($certificate.Subject)' does not match Package.appxmanifest publisher '$publisher'."
    }

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Fail "git is required to record the product and upstream commits."
    }

    $commit = Get-GitOutput @("rev-parse", "HEAD")
    $resolvedUpstream = $UpstreamCommit
    if ([string]::IsNullOrWhiteSpace($resolvedUpstream)) {
        foreach ($candidateRef in @("origin/main", "main")) {
            $mergeBase = & git -C $repoRoot merge-base HEAD $candidateRef 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($mergeBase)) {
                $resolvedUpstream = ($mergeBase | Select-Object -First 1).Trim()
                break
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($resolvedUpstream)) {
        Fail "Could not determine the upstream commit. Pass -UpstreamCommit."
    }
    $resolvedUpstream = Get-GitOutput @("rev-parse", "--verify", "$resolvedUpstream^{commit}")

    $workingTreeState = "Clean"
    $dirtyOutput = & git -C $repoRoot status --porcelain 2>$null
    if (-not [string]::IsNullOrWhiteSpace(($dirtyOutput | Out-String).Trim())) {
        $workingTreeState = "Dirty"
        Write-Warning "The working tree is dirty. Record the release as unreproducible from the recorded commit."
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $repoRoot "artifacts\internal\$version"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory = Join-Path $repoRoot $OutputDirectory
    }
    $OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (-not (Test-Path -LiteralPath $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }

    $outputFullPath = (Resolve-Path -LiteralPath $OutputDirectory).Path
    if ($certificatePathResolved.StartsWith($outputFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail "The signing certificate must not be inside the release output directory."
    }

    $msixName = "${productId}_${version}_${platform}.msix"
    $zipName = "${productId}_${version}_${platform}-portable.zip"
    $checksumsName = "SHA256SUMS.txt"
    $releaseInfoName = "RELEASE-INFO.txt"
    foreach ($name in @($msixName, $zipName, $checksumsName, $releaseInfoName)) {
        $stalePath = Join-Path $OutputDirectory $name
        if (Test-Path -LiteralPath $stalePath) {
            Remove-Item -LiteralPath $stalePath -Force
        }
    }

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("files-internal-" + [Guid]::NewGuid().ToString("N"))
    $msixStaging = Join-Path $tempRoot "msix"
    $portableStaging = Join-Path $tempRoot "portable"
    New-Item -ItemType Directory -Path $msixStaging -Force | Out-Null
    New-Item -ItemType Directory -Path $portableStaging -Force | Out-Null

    $msbuildPath = Find-MSBuild
    $env:PackageCertificatePassword = $CertificatePassword

    $builtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")

    Invoke-MSBuild "Build signed internal MSIX" @(
        $appProject,
        "-restore",
        "-t:Build",
        "-p:Configuration=$configuration",
        "-p:Platform=$platform",
        "-p:Distribution=Msix",
        "-p:AppxBundle=Never",
        "-p:AppxBundlePlatforms=$platform",
        "-p:GenerateAppxPackageOnBuild=true",
        "-p:UapAppxPackageBuildMode=SideloadOnly",
        "-p:AppxPackageDir=$msixStaging\",
        "-p:AppxPackageSigningEnabled=true",
        "-p:PackageCertificateKeyFile=$certificatePathResolved",
        "-p:PackageCertificateThumbprint=",
        "-m:1",
        "-nr:false",
        "-v:quiet",
        "-clp:ErrorsOnly"
    )

    $producedMsix = @(Get-ChildItem -LiteralPath $msixStaging -Recurse -Filter "*.msix" -File |
        Where-Object { $_.DirectoryName -notmatch "\\Dependencies\\" })
    if ($producedMsix.Count -ne 1) {
        Fail "Expected exactly one x64 MSIX package, found $($producedMsix.Count) under '$msixStaging'. AppxBundle must stay disabled for this internal release."
    }

    $msixPath = Join-Path $OutputDirectory $msixName
    Copy-Item -LiteralPath $producedMsix[0].FullName -Destination $msixPath -Force

    $msixArchive = [System.IO.Compression.ZipFile]::OpenRead($msixPath)
    try {
        $signatureEntry = $msixArchive.Entries | Where-Object { $_.FullName -eq "AppxSignature.p7x" } | Select-Object -First 1
        $manifestEntry = $msixArchive.Entries | Where-Object { $_.FullName -eq "AppxManifest.xml" } | Select-Object -First 1
        if ($null -eq $manifestEntry) {
            Fail "The produced MSIX does not contain AppxManifest.xml."
        }
        $manifestReader = New-Object System.IO.StreamReader($manifestEntry.Open())
        try {
            [xml]$packageManifest = $manifestReader.ReadToEnd()
        }
        finally {
            $manifestReader.Dispose()
        }
    }
    finally {
        $msixArchive.Dispose()
    }
    if ($null -eq $signatureEntry) {
        Fail "The produced MSIX does not contain AppxSignature.p7x. It is not a signed package."
    }
    if ([string]$packageManifest.Package.Identity.Name -ne $productId -or
        [string]$packageManifest.Package.Identity.Version -ne $version -or
        [string]$packageManifest.Package.Identity.ProcessorArchitecture -ne $platform -or
        [string]$packageManifest.Package.Identity.Publisher -ne $publisher) {
        Fail "The produced MSIX identity does not match the internal x64 package manifest."
    }

    $authenticode = Get-AuthenticodeSignature -LiteralPath $msixPath
    if ($null -eq $authenticode.SignerCertificate) {
        Fail "The produced MSIX has no Authenticode signer certificate."
    }
    if ($authenticode.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        Fail "The produced MSIX was not signed by the supplied certificate (expected $($certificate.Thumbprint), found $($authenticode.SignerCertificate.Thumbprint))."
    }
    Assert-ZipHasNoPrivateKeyFiles $msixPath "The MSIX package"

    Invoke-MSBuild "Publish portable application" @(
        $appProject,
        "-restore",
        "-t:Publish",
        "-p:Configuration=$configuration",
        "-p:Platform=$platform",
        "-p:Distribution=Portable",
        "-p:RuntimeIdentifier=$runtimeIdentifier",
        "-p:PublishProfile=$portablePublishProfile",
        "-p:PublishDir=$portableStaging\",
        "-m:1",
        "-nr:false",
        "-v:quiet",
        "-clp:ErrorsOnly"
    )

    $portableExe = Join-Path $portableStaging "Files.exe"
    if (-not (Test-Path -LiteralPath $portableExe)) {
        Fail "Portable publish did not produce Files.exe in '$portableStaging'."
    }

    foreach ($requiredPortableFile in @("Microsoft.WindowsAppRuntime.Bootstrap.dll", "Microsoft.WindowsAppRuntime.dll", "Microsoft.ui.xaml.dll", "Files.pri")) {
        if (-not (Test-Path -LiteralPath (Join-Path $portableStaging $requiredPortableFile))) {
            Fail "Portable publish is missing '$requiredPortableFile'. The Windows App SDK must be self-contained."
        }
    }
    foreach ($frameworkFile in @("hostfxr.dll", "coreclr.dll")) {
        if (Test-Path -LiteralPath (Join-Path $portableStaging $frameworkFile)) {
            Fail "Portable publish contains '$frameworkFile'. The release must be native AOT and must not require a system .NET runtime."
        }
    }
    if (Test-Path -LiteralPath (Join-Path $portableStaging "Files.App.Server\Files.App.Server.exe")) {
        Fail "Portable publish contains the out-of-process COM server host."
    }
    if (Test-Path -LiteralPath (Join-Path $portableStaging "Data")) {
        Fail "Portable publish contains a Data directory. User data must not be bundled into the release ZIP."
    }

    Assert-NoPrivateKeyFiles $portableStaging "The portable publish output"

    $releaseInfo = @(
        "Product: $productId"
        "Display-Name: $displayName"
        "Version: $version"
        "Distribution: Internal"
        "Architecture: $platform"
        "Commit: $commit"
        "Upstream-Commit: $resolvedUpstream"
        "Working-Tree: $workingTreeState"
        "Built-Utc: $builtUtc"
        "Msix: $msixName"
        "Portable: $zipName"
        "Signature-Subject: $($certificate.Subject)"
        "Signature-Thumbprint: $($certificate.Thumbprint)"
    ) -join "`r`n"
    Write-Utf8File (Join-Path $portableStaging $releaseInfoName) ($releaseInfo + "`r`n")

    $zipPath = Join-Path $OutputDirectory $zipName
    $zipStream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::CreateNew)
    $zipWriter = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $portableFiles = Get-ChildItem -LiteralPath $portableStaging -Recurse -File
        foreach ($portableFile in $portableFiles) {
            if ($portableFile.Extension.ToLowerInvariant() -eq ".pdb") {
                continue
            }

            $entryName = $portableFile.FullName.Substring($portableStaging.Length + 1).Replace("\", "/")
            $entry = $zipWriter.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $entryStream = $entry.Open()
            $fileStream = [System.IO.File]::OpenRead($portableFile.FullName)
            try {
                $fileStream.CopyTo($entryStream)
            }
            finally {
                $fileStream.Dispose()
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $zipWriter.Dispose()
        $zipStream.Dispose()
    }

    $zipArchive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if (-not ($zipArchive.Entries | Where-Object { $_.FullName -eq "Files.exe" })) {
            Fail "The portable ZIP does not contain Files.exe at its root."
        }
        if (-not ($zipArchive.Entries | Where-Object { $_.FullName -eq $releaseInfoName })) {
            Fail "The portable ZIP does not contain $releaseInfoName."
        }
    }
    finally {
        $zipArchive.Dispose()
    }
    Assert-ZipHasNoPrivateKeyFiles $zipPath "The portable ZIP"

    Write-Utf8File (Join-Path $OutputDirectory $releaseInfoName) ($releaseInfo + "`r`n")
    Assert-NoPrivateKeyFiles $OutputDirectory "The release output directory"

    $msixHash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksums = @(
        "# $displayName $version"
        "# Commit: $commit"
        "# Upstream-Commit: $resolvedUpstream"
        "# Built-Utc: $builtUtc"
        "# Format: SHA-256, two spaces, filename"
        "$msixHash  $msixName"
        "$zipHash  $zipName"
    ) -join "`r`n"
    Write-Utf8File (Join-Path $OutputDirectory $checksumsName) ($checksums + "`r`n")

    Write-Host ""
    Write-Host "Internal release artifacts:"
    Write-Host "  Output:      $OutputDirectory"
    Write-Host "  Version:     $version"
    Write-Host "  Commit:      $commit"
    Write-Host "  Upstream:    $resolvedUpstream"
    Write-Host "  MSIX:        $msixPath"
    Write-Host "  MSIX SHA256: $msixHash"
    Write-Host "  Portable:    $zipPath"
    Write-Host "  ZIP SHA256:  $zipHash"
    Write-Host "  Checksums:   $(Join-Path $OutputDirectory $checksumsName)"
}
finally {
    Remove-Item Env:\PackageCertificatePassword -ErrorAction SilentlyContinue
    if ($null -ne $certificate) {
        $certificate.Dispose()
    }
    if (-not [string]::IsNullOrWhiteSpace($tempRoot) -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
