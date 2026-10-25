# Files - Internal release notes

This branch ships two x64 artifacts. Both are built from the same commit and must
be published together with their SHA-256 manifest.

## Requirements

- Windows x64 build host.
- Visual Studio with MSBuild and the Desktop development with C++ workload.
- .NET SDK and Windows App SDK restored from `global.json` and NuGet.
- An internal signing PFX/P12 certificate with a private key whose subject
  exactly matches `Package.appxmanifest` Identity Publisher (`CN=Files`).

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

Default output: `artifacts\internal\<version>\`.

| File | Purpose |
| --- | --- |
| `Files.Internal_<version>_x64.msix` | Signed internal install package |
| `Files.Internal_<version>_x64-portable.zip` | Complete portable directory |
| `SHA256SUMS.txt` | SHA-256 hashes for both artifacts |
| `RELEASE-INFO.txt` | Version, product commit, upstream commit, signature identity |

`RELEASE-INFO.txt` is also stored at the root of the portable ZIP. MSIX bundles,
app installers, symbol packages, and official update metadata are intentionally
not included in the release output.

Verify the artifacts:

```powershell
Get-FileHash .\Files.Internal_<version>_x64.msix -Algorithm SHA256
Get-FileHash .\Files.Internal_<version>_x64-portable.zip -Algorithm SHA256
Get-AuthenticodeSignature .\Files.Internal_<version>_x64.msix
```

Compare the hashes with `SHA256SUMS.txt`. Trust the internal certificate on the
target machine through the organization's normal certificate deployment; the
build script does not install it.

## Install and run

MSIX, from a normal (non-elevated) PowerShell:

```powershell
Add-AppxPackage .\Files.Internal_<version>_x64.msix
```

The package identity is `Files.Internal`, so it can coexist with the original
Files package. Exclusive shell integration is off by default and is only enabled
from Advanced settings.

Portable: extract the whole ZIP to a writable directory, including directories
with spaces or non-ASCII characters, and run `Files.exe`. No administrator
rights, .NET installation, or Windows App SDK installation is required. Both the
application and its `Data` directory move with the extracted folder. Copy the
whole folder to another location or removable drive; do not copy only the
executable. A second process using the same `Data` directory is rejected, and a
read-only data location fails explicitly instead of falling back to the user
profile.

Updates are manual. Replace the package or portable folder with a newer internal
release; the application does not contact official Files update endpoints.

## Acceptance checklist

Automated build evidence covers tests, Debug/Release builds, signed MSIX
creation, portable publish, ZIP integrity, and checksums. Before handing a
release to users, still verify on a clean Windows VM:

- Portable starts without extra .NET or Windows App SDK installs, as a standard
  user, from a path containing spaces and Chinese characters, and from a USB
  drive.
- Settings, tags, layout, core server data, and basic file operations survive a
  folder move and a drive-letter change.
- Read-only `Data`, a second instance, two portable copies, and coexistence with
  the original Files and the internal MSIX all behave as documented.
- No hidden COM registration or official update requests occur, and disabling
  shell takeover only restores values still owned by this product.
