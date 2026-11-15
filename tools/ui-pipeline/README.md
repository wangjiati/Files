# Automated UI test pipeline (test VMs)

Drives the packaged Files app through UI Automation inside the two test VMs and
produces a PASS/FAIL report. Everything runs in the VM's interactive session; the
host only copies files, creates one scheduled task, and reads results back.

## Why this shape

- The app is a WinUI desktop app: UI tests need a logged-on interactive session,
  so a plain remote process (session 0) is not enough. A scheduled task created
  with `/IT` runs in the logged-on user's session.
- The runner is a compiled C# console app. No PowerShell scripts, no screen
  capture, and no obfuscation inside the guest: this keeps antivirus heuristics
  quiet (the host also never runs the app itself).
- Results are plain JSON/text written by the runner; the host copies them back
  over SMB.

## Prerequisites

- Two VMware test VMs reachable over the network, each with a logged-on user,
  SMB admin share accessible (`\\<ip>\c$`), and the local admin password known.
  In this repo's setup: Win10 `192.168.221.128` (`win10`) and Win11
  `192.168.221.130` (`win11`, rebuilt unencrypted config).
- `vmrun` on the host when `-RevertSnapshots` is used.
- The portable artifact produced by `.github/scripts/Build-Internal.ps1 -PortableOnly`.

## Build the runner

```powershell
dotnet publish tools/ui-pipeline/FilesFilterUiTests/FilesFilterUiTests.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/ui-pipeline/publish
```

## Run

```powershell
$env:FILES_VM_PASSWORD = '<vm local admin password>'
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ui-pipeline/run-pipeline.ps1 `
  -ArtifactZip artifacts/internal/<version>/portable/Files.Internal_<version>_x64-portable.zip
```

Add `-RevertSnapshots` to revert each VM to its `initial` snapshot first (needs
`vmrun` and the VMX paths listed in the script).

Results land in `artifacts/ui-pipeline/results/<timestamp>/<ip>/` (`summary.json`,
`results.txt`, `run.log`), plus `pipeline-summary.json` for the whole run.

## Tests

The runner creates `C:\FilterTest` with deterministic content:

| Test | What it proves |
| --- | --- |
| `launch` | the packaged app starts and exposes a main window |
| `filter-bar` | mode selector and keyword box exist |
| `labels-zh` | new filter labels are localized (e.g. 常规/高级/搜索子文件夹) |
| `basic-wildcard` | `*.zip` in Basic mode filters to the 2 root zips |
| `advanced-editor` | switching to Advanced exposes the name-condition editor |
| `recursion-location` | recursion + `*.zip` reaches 4 zips and the 位置 column appears |
| `clear-all` | clearing restores the unfiltered list |
| `column-header-append` | a column-header funnel appends a condition and switches to Advanced |

## Notes

- The test fixture is recreated on every run; the app directory is replaced each
  run, so the artifact under test is exactly the one passed on the command line.
- Passwords are never stored in the repo: pass them through `FILES_VM_PASSWORD`.
- Win11's original VM is VMware-encrypted and cannot be powered on without its
  password; the pipeline uses the rebuilt unencrypted config
  (`Win11-Rebuilt.vmx`, IP `192.168.221.130`) and its own `initial` snapshot.
