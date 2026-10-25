# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
Builds the x64 Files - Internal release artifacts.

.DESCRIPTION
Produces exactly three release artifacts in the output directory:
  - a signed x64 internal MSIX package
  - a complete portable ZIP archive
  - a SHA-256 checksum manifest

A release information file is also written next to the artifacts and included
in the portable ZIP so the package version, product commit, upstream commit,
target dependency, and signature status travel with the artifacts.

All builds and validation happen in staging directories. The previous release is
replaced only after the new MSIX, ZIP, release information, and checksums have
all passed validation.

The signing certificate and password must be supplied by the caller. The script
never creates, installs, or copies a private key. It fails before building when
signing parameters are missing or when the certificate does not match the
package publisher.

The MSIX uses a framework-dependent Windows App SDK package dependency. The
script records the dependency and prints the target requirement; it does not
bundle or install the Windows App Runtime framework package.

.PARAMETER OutputDirectory
Destination for the release artifacts. Defaults to
artifacts\internal\<package-version>.

.PARAMETER UpstreamCommit
Upstream commit recorded in the release metadata. When omitted, the most recent
merge base with origin/main (or main) is used. That is the latest commit shared
with upstream, not necessarily the original fork point. The commit must be an
ancestor of HEAD. Pass an explicit commit when the exact upstream baseline
matters.

.PARAMETER CertificatePath
Path to the PFX/P12 signing certificate. The certificate must contain a private
key and its subject must exactly match Package.appxmanifest Identity Publisher.

.PARAMETER CertificatePassword
Password for the signing certificate. Prefer the FILES_INTERNAL_CERT_PASSWORD
environment variable to keep the password out of shell history and parameters.

.PARAMETER AllowDirty
Allow building from a working tree with uncommitted changes. By default a dirty
working tree is rejected before any build starts. Artifacts built with this
switch are recorded as Dirty and are not reproducible from the recorded commit.

.EXAMPLE
$securePassword = Read-Host -AsSecureString "Signing certificate password"
$env:FILES_INTERNAL_CERT_PASSWORD = (New-Object System.Net.NetworkCredential("", $securePassword)).Password
.\Build-Internal.ps1 -CertificatePath C:\keys\files-internal.pfx

.EXAMPLE
.\Build-Internal.ps1 `
    -CertificatePath C:\keys\files-internal.pfx `
    -CertificatePassword $env:FILES_INTERNAL_CERT_PASSWORD `
    -OutputDirectory C:\release\files-internal

.EXAMPLE
.\Build-Internal.ps1 `
    -CertificatePath C:\keys\files-internal.pfx `
    -UpstreamCommit ee399b0d3e09084aa24d7261c9342d92bc89a5da `
    -AllowDirty
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [string]$UpstreamCommit = "",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = "",
    [switch]$AllowDirty
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$configuration = "Release"
$platform = "x64"
$runtimeIdentifier = "win-x64"
$productId = "Files.Internal"
$excludedArchiveExtensions = @(".pdb")

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$appProject = Join-Path $repoRoot "src\Files.App\Files.App.csproj"
$appManifest = Join-Path $repoRoot "src\Files.App\Package.appxmanifest"
$portablePublishProfile = "win-x64-portable"

$tempRoot = $null
$releaseStaging = $null
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

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-PathUnderDirectory([string]$Path, [string]$Directory) {
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    $directoryFull = [System.IO.Path]::GetFullPath($Directory)
    if (-not $directoryFull.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $directoryFull += [System.IO.Path]::DirectorySeparatorChar
    }

    return $pathFull.StartsWith($directoryFull, [System.StringComparison]::OrdinalIgnoreCase)
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
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        Fail "$Description could not be scanned because '$Root' is not a directory."
    }

    $forbidden = @(".pfx", ".p12", ".pvk", ".snk", ".key")
    try {
        $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction Stop)
    }
    catch {
        Fail "$Description could not be scanned for private key material: $($_.Exception.Message)"
    }

    $matches = @($files | Where-Object { $forbidden -contains $_.Extension.ToLowerInvariant() })
    if ($matches.Count -ne 0) {
        Fail "$Description contains private key material: $(($matches | ForEach-Object FullName) -join ', ')"
    }
}

function Assert-ZipHasNoPrivateKeyFiles([string]$ZipPath, [string]$Description) {
    $forbidden = @(".pfx", ".p12", ".pvk", ".snk", ".key")
    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    }
    catch {
        Fail "$Description could not be opened for private key scanning: $($_.Exception.Message)"
    }

    try {
        $matches = @($archive.Entries |
            Where-Object { $forbidden -contains [System.IO.Path]::GetExtension($_.FullName).ToLowerInvariant() })
        if ($matches.Count -ne 0) {
            Fail "$Description contains private key material: $(($matches | ForEach-Object FullName) -join ', ')"
        }
    }
    finally {
        $archive.Dispose()
    }
}

function New-PortableZip([string]$PortableRoot, [string]$ZipPath) {
    $zipStream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew)
    try {
        $zipWriter = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $portableFiles = @(Get-ChildItem -LiteralPath $PortableRoot -Recurse -File -Force -ErrorAction Stop)
            foreach ($portableFile in $portableFiles) {
                if ($excludedArchiveExtensions -contains $portableFile.Extension.ToLowerInvariant()) {
                    continue
                }

                $entryName = $portableFile.FullName.Substring($PortableRoot.Length).TrimStart("\", "/").Replace("\", "/")
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
        }
    }
    finally {
        $zipStream.Dispose()
    }
}

function Get-ExpectedPortableEntries([string]$PortableRoot) {
    $expected = @{}
    $files = @(Get-ChildItem -LiteralPath $PortableRoot -Recurse -File -Force -ErrorAction Stop)
    foreach ($file in $files) {
        if ($excludedArchiveExtensions -contains $file.Extension.ToLowerInvariant()) {
            continue
        }

        $entryName = $file.FullName.Substring($PortableRoot.Length).TrimStart("\", "/").Replace("\", "/")
        $expected[$entryName] = $file.Length
    }

    return $expected
}

function Assert-PortableZip([string]$ZipPath, [string]$PortableRoot, [string]$ReleaseInfoName) {
    $expected = Get-ExpectedPortableEntries $PortableRoot
    $remaining = @{}
    foreach ($name in $expected.Keys) {
        $remaining[$name] = $true
    }

    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    }
    catch {
        Fail "The portable ZIP could not be opened: $($_.Exception.Message)"
    }

    try {
        if ($expected.Count -eq 0) {
            Fail "The portable ZIP has no expected entries."
        }
        if ($archive.Entries.Count -ne $expected.Count) {
            Fail "The portable ZIP has $($archive.Entries.Count) entries, but the staged portable directory has $($expected.Count) files after exclusions."
        }
        if (-not $expected.ContainsKey("Files.exe")) {
            Fail "The staged portable directory is missing Files.exe."
        }
        if (-not $expected.ContainsKey($ReleaseInfoName)) {
            Fail "The staged portable directory is missing $ReleaseInfoName."
        }

        foreach ($entry in $archive.Entries) {
            if (-not $remaining.ContainsKey($entry.FullName)) {
                Fail "The portable ZIP contains an unexpected or duplicate entry '$($entry.FullName)'."
            }
            $remaining.Remove($entry.FullName)
            if ($entry.Length -ne $expected[$entry.FullName]) {
                Fail "The portable ZIP entry '$($entry.FullName)' has length $($entry.Length), but the staged file length is $($expected[$entry.FullName])."
            }

            try {
                $entryStream = $entry.Open()
                try {
                    $entryStream.CopyTo([System.IO.Stream]::Null)
                }
                finally {
                    $entryStream.Dispose()
                }
            }
            catch {
                Fail "The portable ZIP entry '$($entry.FullName)' could not be read: $($_.Exception.Message)"
            }
        }

        if ($remaining.Count -ne 0) {
            Fail "The portable ZIP is missing entries: $(($remaining.Keys | Sort-Object) -join ', ')."
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-ReleaseChecksums([string]$ManifestPath, [string]$Root, [string[]]$ExpectedFileNames) {
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        Fail "Checksum manifest '$ManifestPath' is missing."
    }

    $manifestHashes = @{}
    foreach ($line in Get-Content -LiteralPath $ManifestPath) {
        if ($line -match "^\s*#") {
            continue
        }
        if ($line -notmatch "^([0-9a-fA-F]{64})  (.+)$") {
            Fail "Checksum manifest '$ManifestPath' contains an invalid line: '$line'."
        }

        $name = $matches[2]
        if ($manifestHashes.ContainsKey($name)) {
            Fail "Checksum manifest '$ManifestPath' contains duplicate entry '$name'."
        }
        $manifestHashes[$name] = $matches[1].ToLowerInvariant()
    }

    foreach ($name in $ExpectedFileNames) {
        if (-not $manifestHashes.ContainsKey($name)) {
            Fail "Checksum manifest '$ManifestPath' does not cover '$name'."
        }
        $filePath = Join-Path $Root $name
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            Fail "Checksum manifest references missing file '$name'."
        }
        if ((Get-Sha256 $filePath) -ne $manifestHashes[$name]) {
            Fail "Checksum mismatch for '$name'."
        }
    }

    if ($manifestHashes.Count -ne $ExpectedFileNames.Count) {
        Fail "Checksum manifest '$ManifestPath' contains entries outside the release file set."
    }
}

function Publish-ReleaseSet([string]$StagingDirectory, [string]$DestinationDirectory, [string[]]$FileNames, [string]$ChecksumsName) {
    if (-not (Test-Path -LiteralPath $StagingDirectory -PathType Container)) {
        Fail "Release staging directory '$StagingDirectory' is missing."
    }

    $stagingExtras = @(Get-ChildItem -LiteralPath $StagingDirectory -Force |
        Where-Object { $FileNames -notcontains $_.Name })
    if ($stagingExtras.Count -ne 0) {
        Fail "Release staging contains unexpected entries: $(($stagingExtras | ForEach-Object Name) -join ', ')."
    }
    foreach ($name in $FileNames) {
        if (-not (Test-Path -LiteralPath (Join-Path $StagingDirectory $name) -PathType Leaf)) {
            Fail "Release staging is missing '$name'."
        }
    }

    if (-not (Test-Path -LiteralPath $DestinationDirectory)) {
        Move-Item -LiteralPath $StagingDirectory -Destination $DestinationDirectory
        try {
            Assert-ReleaseChecksums (Join-Path $DestinationDirectory $ChecksumsName) $DestinationDirectory ($FileNames | Where-Object { $_ -ne $ChecksumsName })
        }
        catch {
            if (Test-Path -LiteralPath $DestinationDirectory) {
                Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
            throw
        }
        return
    }

    if (Test-Path -LiteralPath $DestinationDirectory) {
        if (-not (Test-Path -LiteralPath $DestinationDirectory -PathType Container)) {
            Fail "Release output path '$DestinationDirectory' exists but is not a directory."
        }

        $existingNames = @(Get-ChildItem -LiteralPath $DestinationDirectory -Force | ForEach-Object Name)
        $hasOnlyReleaseFiles = @($existingNames | Where-Object { $FileNames -notcontains $_ }).Count -eq 0

        if ($hasOnlyReleaseFiles) {
            $backupDirectory = $DestinationDirectory + ".old-" + [Guid]::NewGuid().ToString("N")
            Move-Item -LiteralPath $DestinationDirectory -Destination $backupDirectory
            try {
                Move-Item -LiteralPath $StagingDirectory -Destination $DestinationDirectory
                Assert-ReleaseChecksums (Join-Path $DestinationDirectory $ChecksumsName) $DestinationDirectory ($FileNames | Where-Object { $_ -ne $ChecksumsName })
            }
            catch {
                $publishError = $_
                if (Test-Path -LiteralPath $DestinationDirectory) {
                    Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force -ErrorAction SilentlyContinue
                }
                if (Test-Path -LiteralPath $backupDirectory) {
                    try {
                        Move-Item -LiteralPath $backupDirectory -Destination $DestinationDirectory -ErrorAction Stop
                    }
                    catch {
                        Fail "Publishing failed and the previous release could not be restored from '$backupDirectory': $($_.Exception.Message)"
                    }
                }
                throw $publishError
            }

            if (Test-Path -LiteralPath $backupDirectory) {
                try {
                    Remove-Item -LiteralPath $backupDirectory -Recurse -Force -ErrorAction Stop
                }
                catch {
                    Write-Warning "The new release was published, but the previous release backup '$backupDirectory' could not be removed: $($_.Exception.Message)"
                }
            }
            return
        }
    }

    $destinationParent = Split-Path -Parent $DestinationDirectory
    $destinationLeaf = Split-Path -Leaf $DestinationDirectory
    $backupDirectory = Join-Path $destinationParent (".$destinationLeaf.backup-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

    $movedNewFiles = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($name in $FileNames) {
            $existingFile = Join-Path $DestinationDirectory $name
            if (Test-Path -LiteralPath $existingFile -PathType Leaf) {
                Copy-Item -LiteralPath $existingFile -Destination (Join-Path $backupDirectory $name) -Force
            }
        }

        foreach ($name in $FileNames) {
            Move-Item -LiteralPath (Join-Path $StagingDirectory $name) -Destination (Join-Path $DestinationDirectory $name) -Force
            $movedNewFiles.Add($name)
        }

        Assert-ReleaseChecksums (Join-Path $DestinationDirectory $ChecksumsName) $DestinationDirectory ($FileNames | Where-Object { $_ -ne $ChecksumsName })
    }
    catch {
        $publishError = $_
        foreach ($name in $FileNames) {
            $backupFile = Join-Path $backupDirectory $name
            $destinationFile = Join-Path $DestinationDirectory $name
            if (Test-Path -LiteralPath $backupFile -PathType Leaf) {
                try {
                    Copy-Item -LiteralPath $backupFile -Destination $destinationFile -Force -ErrorAction Stop
                }
                catch {
                    Fail "Publishing failed and the previous '$name' could not be restored: $($_.Exception.Message)"
                }
            }
            elseif ($movedNewFiles.Contains($name) -and (Test-Path -LiteralPath $destinationFile)) {
                Remove-Item -LiteralPath $destinationFile -Force
            }
        }
        throw $publishError
    }

    if (Test-Path -LiteralPath $backupDirectory) {
        try {
            Remove-Item -LiteralPath $backupDirectory -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "The new release was published, but the previous release backup '$backupDirectory' could not be removed: $($_.Exception.Message)"
        }
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
    $upstreamSource = "explicit"
    if ([string]::IsNullOrWhiteSpace($resolvedUpstream)) {
        $upstreamSource = "merge-base origin/main"
        foreach ($candidateRef in @("origin/main", "main")) {
            $mergeBase = & git -C $repoRoot merge-base HEAD $candidateRef 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($mergeBase)) {
                $resolvedUpstream = ($mergeBase | Select-Object -First 1).Trim()
                if ($candidateRef -ne "origin/main") {
                    $upstreamSource = "merge-base $candidateRef"
                }
                break
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($resolvedUpstream)) {
        Fail "Could not determine the upstream commit. Pass -UpstreamCommit."
    }
    $resolvedUpstream = Get-GitOutput @("rev-parse", "--verify", "$resolvedUpstream^{commit}")
    & git -C $repoRoot merge-base --is-ancestor $resolvedUpstream $commit 2>$null
    if ($LASTEXITCODE -ne 0) {
        Fail "Upstream commit '$resolvedUpstream' is not an ancestor of HEAD '$commit'."
    }

    $dirtyOutput = @(& git -C $repoRoot status --porcelain 2>$null)
    if ($LASTEXITCODE -ne 0) {
        Fail "git status failed while checking the working tree."
    }
    $workingTreeState = "Clean"
    if ($dirtyOutput.Count -ne 0) {
        $workingTreeState = "Dirty"
        $dirtyPreview = ($dirtyOutput | Select-Object -First 20) -join "`r`n"
        if (-not $AllowDirty) {
            Fail "The working tree has uncommitted changes. Commit or stash them before building a release, or pass -AllowDirty.`r`n$dirtyPreview"
        }
        Write-Warning "The working tree is dirty. The artifacts will be recorded as Dirty and are not reproducible from the recorded commit."
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $repoRoot "artifacts\internal\$version"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory = Join-Path $repoRoot $OutputDirectory
    }
    $outputFullPath = [System.IO.Path]::GetFullPath($OutputDirectory)
    $outputParent = Split-Path -Parent $outputFullPath
    $outputLeaf = Split-Path -Leaf $outputFullPath
    if ([string]::IsNullOrWhiteSpace($outputParent) -or [string]::IsNullOrWhiteSpace($outputLeaf)) {
        Fail "Output directory '$outputFullPath' must be a directory below a drive root."
    }
    if (Test-Path -LiteralPath $outputFullPath) {
        if (-not (Test-Path -LiteralPath $outputFullPath -PathType Container)) {
            Fail "Release output path '$outputFullPath' exists but is not a directory."
        }
    }
    if (Test-PathUnderDirectory $certificatePathResolved $outputFullPath) {
        Fail "The signing certificate must not be inside the release output directory."
    }

    $msixName = "${productId}_${version}_${platform}.msix"
    $zipName = "${productId}_${version}_${platform}-portable.zip"
    $checksumsName = "SHA256SUMS.txt"
    $releaseInfoName = "RELEASE-INFO.txt"
    $releaseFileNames = @($msixName, $zipName, $releaseInfoName, $checksumsName)

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

    $msixSourcePath = $producedMsix[0].FullName
    $msixArchive = [System.IO.Compression.ZipFile]::OpenRead($msixSourcePath)
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
    if ($null -eq $signatureEntry -or $signatureEntry.Length -eq 0) {
        Fail "The produced MSIX does not contain a valid AppxSignature.p7x. It is not a signed package."
    }
    if ([string]$packageManifest.Package.Identity.Name -ne $productId -or
        [string]$packageManifest.Package.Identity.Version -ne $version -or
        [string]$packageManifest.Package.Identity.ProcessorArchitecture -ne $platform -or
        [string]$packageManifest.Package.Identity.Publisher -ne $publisher) {
        Fail "The produced MSIX identity does not match the internal x64 package manifest."
    }

    $dependencySummary = @()
    $windowsAppRuntimeDependency = $null
    if ($null -ne $packageManifest.Package.Dependencies) {
        foreach ($dependency in @($packageManifest.Package.Dependencies.PackageDependency | Where-Object { $null -ne $_ })) {
            $dependencyName = [string]$dependency.Name
            $dependencyMinVersion = [string]$dependency.MinVersion
            if ([string]::IsNullOrWhiteSpace($dependencyName) -or [string]::IsNullOrWhiteSpace($dependencyMinVersion)) {
                Fail "The produced MSIX contains an invalid package dependency."
            }
            $dependencySummary += "$dependencyName >= $dependencyMinVersion"
            if ($dependencyName -eq "Microsoft.WindowsAppRuntime.2") {
                $windowsAppRuntimeDependency = $dependency
            }
        }
    }
    if ($dependencySummary.Count -eq 0) {
        $dependencySummary = @("none")
        Write-Warning "The produced MSIX has no package dependencies. Confirm that it is intentionally self-contained before release."
    }
    elseif ($null -eq $windowsAppRuntimeDependency) {
        Write-Warning "The produced MSIX does not depend on Microsoft.WindowsAppRuntime.2. Confirm that it is intentionally self-contained."
    }
    else {
        Write-Host "    MSIX target requirement: Microsoft.WindowsAppRuntime.2 >= $([string]$windowsAppRuntimeDependency.MinVersion) (x64) must be installed or provisioned separately."
    }

    $authenticode = Get-AuthenticodeSignature -LiteralPath $msixSourcePath
    $authenticodeStatus = [string]$authenticode.Status
    if ($authenticodeStatus -eq "NotSigned" -or $authenticodeStatus -eq "HashMismatch") {
        Fail "The produced MSIX Authenticode status is '$authenticodeStatus'. It is not a valid signed package."
    }
    if (@("Valid", "UnknownError", "NotTrusted") -notcontains $authenticodeStatus) {
        Fail "The produced MSIX Authenticode status is '$authenticodeStatus'. Only Valid, UnknownError, or NotTrusted signatures are accepted."
    }
    if ($null -eq $authenticode.SignerCertificate) {
        Fail "The produced MSIX has no Authenticode signer certificate."
    }
    if ($authenticode.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        Fail "The produced MSIX was not signed by the supplied certificate (expected $($certificate.Thumbprint), found $($authenticode.SignerCertificate.Thumbprint))."
    }
    if ($authenticodeStatus -ne "Valid") {
        Write-Warning "MSIX Authenticode status is '$authenticodeStatus'. This is expected for an untrusted test certificate; verify certificate trust on target machines."
    }
    Assert-ZipHasNoPrivateKeyFiles $msixSourcePath "The MSIX package"

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
        "Upstream-Commit-Source: $upstreamSource"
        "Working-Tree: $workingTreeState"
        "Built-Utc: $builtUtc"
        "Msix: $msixName"
        "Portable: $zipName"
        "Msix-Dependencies: $($dependencySummary -join '; ')"
        "Signature-Subject: $($certificate.Subject)"
        "Signature-Thumbprint: $($certificate.Thumbprint)"
        "Signature-Status: $authenticodeStatus"
    ) -join "`r`n"
    Write-Utf8File (Join-Path $portableStaging $releaseInfoName) ($releaseInfo + "`r`n")

    $zipSourcePath = Join-Path $tempRoot $zipName
    New-PortableZip $portableStaging $zipSourcePath
    Assert-PortableZip $zipSourcePath $portableStaging $releaseInfoName
    Assert-ZipHasNoPrivateKeyFiles $zipSourcePath "The portable ZIP"

    if (-not (Test-Path -LiteralPath $outputParent -PathType Container)) {
        New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
    }
    $releaseStaging = Join-Path $outputParent (".$outputLeaf.staging-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $releaseStaging -Force | Out-Null
    Copy-Item -LiteralPath $msixSourcePath -Destination (Join-Path $releaseStaging $msixName) -Force
    Copy-Item -LiteralPath $zipSourcePath -Destination (Join-Path $releaseStaging $zipName) -Force
    Write-Utf8File (Join-Path $releaseStaging $releaseInfoName) ($releaseInfo + "`r`n")

    $msixHash = Get-Sha256 (Join-Path $releaseStaging $msixName)
    $zipHash = Get-Sha256 (Join-Path $releaseStaging $zipName)
    $releaseInfoHash = Get-Sha256 (Join-Path $releaseStaging $releaseInfoName)
    $checksums = @(
        "# $displayName $version"
        "# Commit: $commit"
        "# Upstream-Commit: $resolvedUpstream"
        "# Built-Utc: $builtUtc"
        "# Format: SHA-256, two spaces, filename"
        "$msixHash  $msixName"
        "$zipHash  $zipName"
        "$releaseInfoHash  $releaseInfoName"
    ) -join "`r`n"
    Write-Utf8File (Join-Path $releaseStaging $checksumsName) ($checksums + "`r`n")

    Assert-NoPrivateKeyFiles $releaseStaging "The release staging directory"
    Assert-ReleaseChecksums (Join-Path $releaseStaging $checksumsName) $releaseStaging @($msixName, $zipName, $releaseInfoName)

    Publish-ReleaseSet $releaseStaging $outputFullPath $releaseFileNames $checksumsName
    $releaseStaging = $null
    Assert-ReleaseChecksums (Join-Path $outputFullPath $checksumsName) $outputFullPath @($msixName, $zipName, $releaseInfoName)

    Write-Host ""
    Write-Host "Internal release artifacts:"
    Write-Host "  Output:      $outputFullPath"
    Write-Host "  Version:     $version"
    Write-Host "  Commit:      $commit"
    Write-Host "  Upstream:    $resolvedUpstream ($upstreamSource)"
    Write-Host "  MSIX:        $(Join-Path $outputFullPath $msixName)"
    Write-Host "  MSIX SHA256: $msixHash"
    Write-Host "  Portable:    $(Join-Path $outputFullPath $zipName)"
    Write-Host "  ZIP SHA256:  $zipHash"
    Write-Host "  Info SHA256: $releaseInfoHash"
    Write-Host "  Checksums:   $(Join-Path $outputFullPath $checksumsName)"
    Write-Host "  MSIX target dependency: $($dependencySummary -join '; ') (install or provision separately)"
}
finally {
    Remove-Item Env:\PackageCertificatePassword -ErrorAction SilentlyContinue
    if ($null -ne $certificate) {
        $certificate.Dispose()
    }
    if (-not [string]::IsNullOrWhiteSpace($tempRoot) -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not [string]::IsNullOrWhiteSpace($releaseStaging) -and (Test-Path -LiteralPath $releaseStaging)) {
        Remove-Item -LiteralPath $releaseStaging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
