# Files - Internal release notes

This branch ships two x64 artifacts. Both are built from the same commit and must
be published together with their SHA-256 manifest.

## Requirements

- Windows x64 build host.
- Visual Studio with MSBuild and the Desktop development with C++ workload.
- .NET SDK and Windows App SDK restored from `global.json` and NuGet.
- An internal signing PFX/P12 certificate with a private key whose subject
  exactly matches `Package.appxmanifest` Identity Publisher (`CN=Files`).

Target machines:

- Windows 10 version 2004 (build 19041) or later, x64.
- The internal MSIX is framework-dependent. It declares
  `Microsoft.WindowsAppRuntime.2` (current build: minimum `2.5.1.0`) and does not
  bundle that framework package. Install or provision the matching x64 Windows
  App Runtime framework package from Microsoft's redistributable or your
  organization's normal deployment before installing the MSIX. This is a
  Microsoft runtime dependency, not a Files update source. `RELEASE-INFO.txt`
  records the exact dependency for each build.
- The portable ZIP is packaged with its own Windows App SDK and Native AOT
  runtime and does not declare the MSIX framework dependency. Verify startup on
  the clean target VM using the acceptance checklist.

Do not commit the signing certificate or its password. Keep the PFX outside the
repository and provide the password through `FILES_INTERNAL_CERT_PASSWORD`.
The release script never creates, imports, installs, or copies a certificate.

## Build

Run from a Visual Studio Developer PowerShell (the script locates Visual Studio
automatically when `msbuild` is not already on `PATH`):

```powershell
$securePassword = Read-Host -AsSecureString "Signing certificate password"
$env:FILES_INTERNAL_CERT_PASSWORD = (New-Object System.Net.NetworkCredential("", $securePassword)).Password
.\.github\scripts\Build-Internal.ps1 -CertificatePath C:\keys\files-internal.pfx
```

The script fails before building when the certificate or password is missing,
when the certificate has no private key, or when its subject does not match the
package publisher. It never emits an unsigned package labeled as signed.

The script rejects a dirty working tree by default. Pass `-AllowDirty` only when
an uncommitted build is intentional; such artifacts are recorded as `Dirty` and
are not reproducible from the recorded commit.

The script always builds and runs the unit tests, builds both Msix and Portable
in Debug, builds the native launcher and 32/64-bit open/save dialog projects in
Release, then creates the signed Release MSIX and the Portable Native AOT
publish. It rejects the MSIX payload when any launcher or dialog binary is
missing, and rejects the Portable output when required UI/assets files such as
`Assets/AppTiles/Dev/Logo.ico`, `Assets/Sidebar/EmptySidebar_48_ThemeLight.svg`,
`Assets/WSL/alpinepng.png`, and `Assets/error.png` are absent. It also checks
that the Portable executable file version matches the manifest version.

A non-empty output directory is rejected unless `-ReplaceExistingRelease` is
passed explicitly. All builds, ZIP validation, checksum generation, and hash
verification happen in staging directories. The previous release is replaced
only after the complete new release passes validation. A failed build leaves the
previous release intact and removes the failed staging output.

Default output: `artifacts\internal\<version>\`.

| File | Purpose |
| --- | --- |
| `Files.Internal_<version>_x64.msix` | Signed internal install package |
| `Files.Internal_<version>_x64-portable.zip` | Complete portable directory |
| `SHA256SUMS.txt` | SHA-256 hashes for the MSIX, Portable ZIP, and `RELEASE-INFO.txt` |
| `RELEASE-INFO.txt` | Version, commits, target dependency, signature identity and status |

`RELEASE-INFO.txt` is also stored at the root of the portable ZIP. MSIX bundles,
app installers, symbol packages, and official update metadata are intentionally
not included in the release output.

Verify the artifacts:

```powershell
Get-FileHash .\Files.Internal_<version>_x64.msix -Algorithm SHA256
Get-FileHash .\Files.Internal_<version>_x64-portable.zip -Algorithm SHA256
Get-FileHash .\RELEASE-INFO.txt -Algorithm SHA256
Get-AuthenticodeSignature .\Files.Internal_<version>_x64.msix
```

Compare all three hashes with `SHA256SUMS.txt`. `SHA256SUMS.txt` does not contain
a hash of itself.

Trust the internal certificate on the target machine through the organization's
normal certificate deployment; the build script does not install it. A build
using an untrusted test certificate reports a non-`Valid` Authenticode status and
must not be treated as a trusted release.

## Install and run

MSIX, from a normal (non-elevated) PowerShell, after provisioning the Windows App
Runtime dependency recorded in `RELEASE-INFO.txt`:

```powershell
Add-AppxPackage .\Files.Internal_<version>_x64.msix
```

The package identity is `Files.Internal`, isolated from the original package.
Coexistence with the original Files package must be verified in the acceptance
checklist. Exclusive shell integration is off by default and is only enabled
from Advanced settings.

Turn off shell takeover before uninstalling or resetting the package. Takeover
writes real HKCU values that point to integration files copied into
`%LocalAppData%\Files.Internal`. Uninstalling removes package-local assets and
virtualized app data, but it does not reliably remove those HKCU values or the
copied integration files, so leftover values can point to missing files.

Portable: extract the whole ZIP to a writable directory, including directories
with spaces or non-ASCII characters, and run `Files.exe`. The package is designed
to run without administrator rights, a separately installed .NET runtime, or a
separately installed Windows App SDK. Both the application and its `Data`
directory move with the extracted folder. Copy the whole folder to another
location or removable drive; do not copy only the executable. A second process
using the same `Data` directory is expected to be rejected, and a read-only data
location is expected to fail explicitly instead of falling back to the user
profile. These runtime behaviors must be verified on a clean target machine using
the acceptance checklist below; they are not established by the build script.

Updates are manual. Replace the package or portable folder with a newer internal
release. The internal build registers a local no-op update service (source and
unit-test verified); no live network capture is part of this build. Do not treat
the absence of official update requests as verified until the network check in
the acceptance checklist is performed.

## Acceptance checklist

Automated build evidence covers tests, Debug/Release builds, signed MSIX
creation, portable publish, ZIP integrity, and checksums. The build script does
not complete the runtime checks below. Do not claim them as verified until they
have been executed on the target environment.

Before handing a release to users, verify on a clean Windows VM:

- Portable starts without extra .NET or Windows App SDK installs, as a standard
  user, from a path containing spaces and Chinese characters, and from a USB
  drive.
- Settings, tags, layout, core server data, and basic file operations survive a
  folder move and a drive-letter change.
- Read-only `Data`, a second instance, two portable copies, and coexistence with
  the original Files and the internal MSIX all behave as documented.
- The MSIX installs after the declared Windows App Runtime dependency is
  provisioned, starts, and can be removed without leaving hidden registrations.
- No hidden COM registration or official update requests occur, and disabling
  shell takeover only restores values still owned by this product.
