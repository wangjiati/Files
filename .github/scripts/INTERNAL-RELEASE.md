# Files - Internal release notes

This branch ships two x64 artifacts. Both are built from the same commit and must
be published together with their SHA-256 manifest.

## Versioning

Versions are four numeric parts: `<upstreamMajor>.<upstreamMinor>.<upstreamBuild>.<forkIteration>`.

- The first three parts match the upstream version the branch is synced to.
- The fourth part is the internal iteration. It starts at 1 and increases with
  every internal release; reset it to 1 when the upstream version changes.
- Do not bump the fourth part on your own. Ask the maintainer whether the build is a new
  release. Only increment after they say yes; a verification rebuild keeps the current
  version and overwrites the existing output directory.
- The first release using this scheme is `4.3.1.1`.
- Change both `Files.App.csproj` and `Package.appxmanifest`; the existing
  `Portable_VersionMatchesThePackageManifest` test fails if they drift.
- The GitHub tag is `v<version>` (for example `v4.3.1.1`) and no longer includes
  the distribution.
- The GitHub release title is `Files Internal <version>`.
- The release body must contain `## 本版变化`; this section is shown in the
  client update dialog.
- Upload the contents of `msix\` and `portable\`, each with its own `SHA256SUMS.txt`
  and `RELEASE-INFO.txt`.
- Mark preview builds as `prerelease` so `releases/latest` ignores them.

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

If the build host cannot reach `nuget.org` (offline, or behind a proxy that does not
forward NuGet traffic), restore aborts with `NU1900` while fetching package vulnerability
data. Disable the audit for that run:

```powershell
$env:NuGetAudit = 'false'
```

The same applies when only cached packages are available. It does not change which
packages are restored.

The script rebuilds `Files.App` and `Files.App.Server` outputs before it starts, and again
before the portable publish. The two distributions share `obj`/`bin`, and reusing them
poisons the CsWinRT projection (the Msix build needs `Files.App.Server`) or the merged
resource index (`Files.pri` loses the WinUI theme resources and the unpackaged app fails at
startup with `0x802B000A`). Do not remove that cleanup to save build time.

The script rejects a dirty working tree by default. Pass `-AllowDirty` only when
an uncommitted build is intentional; such artifacts are recorded as `Dirty` and
are not reproducible from the recorded commit.

### Portable-only rebuild

Use the same script for a portable-only build; no signing certificate is
required in this mode:

```powershell
.\.github\scripts\Build-Internal.ps1 -PortableOnly -OutputDirectory artifacts\portable-<version> -ReplaceExistingRelease
```

Always package through this script. Do not hand-roll an `msbuild -t:Publish`
command for the portable profile:

- Passing only `-p:PublishProfile=win-x64-portable` applies `Distribution` too
  late in MSBuild evaluation: `DISTRIBUTION_MSIX` stays defined while the
  Msix-only server build target is skipped, so `Files.App.Server` types fail to
  resolve and the build breaks. `Distribution` must be a global property from
  the start, which the script already does correctly.
- A raw publish also skips the staging and validation this script performs:
  unit tests, Debug builds of both distributions, launcher and dialog binaries,
  portable payload and asset checks, executable/manifest version match, ZIP
  layout, and the `SHA256SUMS.txt`/`RELEASE-INFO.txt` manifest.

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

The release directory owns exactly `msix\` and `portable\`. After a successful
publish the script removes every other top-level entry, so leftovers from an
older layout or manual copies do not survive a rebuild. It never sweeps when the
output directory is the repository root.

Default output: `artifacts\internal\<version>\`, split into two self-contained
folders.

`msix\`

| File | Purpose |
| --- | --- |
| `Files.Internal_<version>_x64.msix` | Signed internal install package |
| `Files.Internal_<version>_x64.cer` | Public certificate used to sign the package |
| `Start-FilesInternal.bat` | Imports the certificate into `LocalMachine\TrustedPeople`, then opens the MSIX |
| `Files.Internal_<version>_x64-msix.zip` | The files above plus `RELEASE-INFO.txt`, as one archive |
| `install-guide.md`, `install-guide.png` | Install instructions, text and single-page image |
| `SHA256SUMS.txt` | SHA-256 hashes for every file in the folder except itself |
| `RELEASE-INFO.txt` | Version, commits, target dependency, signature identity and status |

`portable\`

| File | Purpose |
| --- | --- |
| `Files.Internal_<version>_x64-portable.zip` | Complete portable directory, extract and run |
| `Files.Internal_<version>_x64.cer`, `Start-FilesInternal.bat` | Also stored at the root of the ZIP: the portable build needs the certificate trusted before its identity package can register. The script then starts the launcher. |
| `install-guide.md`, `install-guide.png` | Also stored inside the ZIP, so the extracted folder documents itself |
| `SHA256SUMS.txt` | SHA-256 hashes for every file in the folder except itself |
| `RELEASE-INFO.txt` | Version, commits, target dependency, signature identity and status |

Each folder can be handed out on its own: neither needs the other. The `.cer`
and `Start-FilesInternal.bat` are exported and copied by the build, so
they always match the signature of the package in the same folder.

`RELEASE-INFO.txt` is also stored at the root of the portable ZIP. MSIX bundles,
app installers, symbol packages, and official update metadata are intentionally
not included in the release output.

Verify the artifacts:

```powershell
Get-FileHash .\msix\Files.Internal_<version>_x64.msix -Algorithm SHA256
Get-FileHash .\portable\Files.Internal_<version>_x64-portable.zip -Algorithm SHA256
Get-AuthenticodeSignature .\msix\Files.Internal_<version>_x64.msix
```

Compare the hashes with the `SHA256SUMS.txt` of the folder each file belongs to.
A manifest covers every file in its own folder and never contains a hash of itself.

Trust the internal certificate on the target machine through the organization's
normal certificate deployment; the build script does not install it. A build
using an untrusted test certificate reports a non-`Valid` Authenticode status and
must not be treated as a trusted release.

## Automated tests

Three scripts cover the portable release. They exit with `0` on success, `1` when a check
fails and `2` when the environment cannot run the check (locked desktop, missing files).

| Script | Purpose |
| --- | --- |
| `Test-ReleaseArtifacts.ps1 -ZipPath <zip>` | Extracts the ZIP and verifies payload completeness, the merged resource index, launcher capabilities, `RELEASE-INFO`, plain launch (no identity) and identity launch (package registration + process identity). Works on a locked desktop. |
| `Test-PortableIme.ps1 -PortableRoot <dir> -Mode Identity\|Plain` | Drives the UI: selects a sample item, presses F2, types `nihao` and reports the composition text and whether an IME candidate window became visible. Needs an unlocked interactive desktop. |
| `Invoke-SandboxReleaseTest.ps1 -ZipPath <zip> -IncludeUi` | Runs the acceptance test inside Windows Sandbox: maps a payload folder, trusts the test certificate, executes the headless checks and the UI leg, and returns the JSON result. The sandbox has its own desktop, so the UI leg can take the foreground without a user present. |

Typical release check:

```powershell
# Headless checks on the build host
.\.github\scripts\Test-ReleaseArtifacts.ps1 -ZipPath .\artifacts\internal\<version>\Files.Internal_<version>_x64-portable.zip

# Full UI checks on an unlocked desktop
.\.github\scripts\Test-ReleaseArtifacts.ps1 -ZipPath .\artifacts\internal\<version>\Files.Internal_<version>_x64-portable.zip -IncludeUi

# Full UI checks in Windows Sandbox (no user session required)
.\.github\scripts\Invoke-SandboxReleaseTest.ps1 -ZipPath .\artifacts\internal\<version>\Files.Internal_<version>_x64-portable.zip -IncludeUi
```

`Test-PortableIme.ps1` measures only the rename text box of the sample item and diffs the
visible candidate windows against a baseline taken before typing, then commits with Space.
A run therefore reports the composing text, the committed text and the candidate windows
that newly appeared. It asserts that the identity mode shows candidates and the plain mode
does not; on Windows builds before 26300 both modes are expected to show candidates, and
the acceptance script picks that expectation from the OS build number automatically. Pass
`-ExpectCandidates Yes` when running the script directly on such a build. The JSON reports
contain the observed window list, so a failing run shows which IME windows existed.

The UI leg refuses to report a result on a locked desktop: `SendInput` would go to the
Winlogon desktop and the app would never receive the keystrokes. That case exits with `2`
and the acceptance script then reports exit code `2` as well, so a locked host cannot be
mistaken for a passing run.

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

### Portable identity package

The portable ZIP also contains `Files.Internal.Portable.Identity.msix` (a payload-free identity
package). On Windows builds where unpackaged WinUI 3 applications lose the IME candidate window
(observed on Windows 11 build 26300), the launcher registers this package on first start and then
activates the app through the package identity, which restores the candidate window. The binaries
keep living in the portable folder. No separate registration script ships: the launcher does it.

- Registration is per user and only succeeds when the signing certificate is trusted. Trust it
  once per machine with `Start-FilesInternal.bat`, which is shipped in the `msix\` folder and at
  the root of the portable ZIP; with a trusted code-signing certificate the launcher registers
  silently.
- To remove the registration, unregister before deleting the folder, otherwise the registration
  keeps pointing at a missing external location:
  `Get-AppxPackage -Name Files.Internal.Portable.Identity | Remove-AppxPackage`.
- Moving the folder changes the external location, so the launcher re-registers on the next start.
- QA switch: set `FILES_DISABLE_PORTABLE_IDENTITY=1` to compare behavior with and without package
  identity. The launcher then skips registration and activation.
- Activation arguments must not carry the whitespace that separated them from the launcher path in
  the command line: `IApplicationActivationManager` hands the string to the app verbatim, and a
  leading space makes the app treat the folder path as invalid. The launcher trims it, and the app
  trims the value it receives as a second guard.
- Portable-only builds have no signing certificate and intentionally omit the identity package.

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
