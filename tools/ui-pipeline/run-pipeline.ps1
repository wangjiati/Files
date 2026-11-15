#requires -Version 5.1
<#
.SYNOPSIS
Runs the packaged Files app through the UI test runner inside the test VMs.

.DESCRIPTION
Deploys the portable artifact and the compiled FilterUiTest runner over SMB,
triggers the runner as an interactive scheduled task in the logged-on session,
collects results, and prints a PASS/FAIL table.

.EXAMPLE
pwsh -File tools/ui-pipeline/run-pipeline.ps1 -ArtifactZip artifacts/internal/4.3.3.3/portable/Files.Internal_4.3.3.3_x64-portable.zip
#>
param(`r`n    [Parameter(Mandatory = $true)]
    [string]$ArtifactZip,

    [string]$RunnerExe = "",

    [switch]$RevertSnapshots,

    [string]$ResultsRoot = "artifacts/ui-pipeline/results",

    [string]$GuestPassword = $env:FILES_VM_PASSWORD
)

$ErrorActionPreference = "Stop"
$vmrun = "C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe"

$vms = @(
    [pscustomobject]@{
        Ip = "192.168.221.128"
        User = "win10"
        Vmx = "D:\Users\win11\Documents\Virtual Machines\Windows 10 Enterprise LTSC 2021 (x64)\Windows 10 Enterprise LTSC 2021 (x64).vmx"
    },
    [pscustomobject]@{
        Ip = "192.168.221.130"
        User = "win11"
        Vmx = "D:\Users\win11\Documents\Virtual Machines\Windows 11 Enterprise LTSC x64\Win11-Rebuilt.vmx"
    }
)

if (-not $RunnerExe) {
    $RunnerExe = Join-Path $PSScriptRoot "..\..\artifacts\ui-pipeline\publish\FilterUiTest.exe"
}

if (-not $GuestPassword) { throw 'Set FILES_VM_PASSWORD or pass -GuestPassword (VM local admin password).' }

if (-not (Test-Path $ArtifactZip)) { throw "artifact not found: $ArtifactZip" }
if (-not (Test-Path $RunnerExe)) { throw "runner not found: $RunnerExe (publish FilesFilterUiTests first)" }

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultsDir = Join-Path $ResultsRoot $stamp
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$stageApp = Join-Path $env:TEMP "files-ui-stage-$stamp"
Write-Host "==> staging artifact"
Expand-Archive -Path $ArtifactZip -DestinationPath $stageApp -Force

$summary = @()

foreach ($vm in $vms) {
    $ip = $vm.Ip
    Write-Host "==> $ip"

    if ($RevertSnapshots) {
        Write-Host "    revert snapshot initial"
        & $vmrun -T ws revertToSnapshot $vm.Vmx initial | Out-Null
    }

    $running = (& $vmrun list 2>$null) -match [regex]::Escape($vm.Vmx)
    if (-not $running) {
        Write-Host "    start VM"
        & $vmrun -T ws start $vm.Vmx nogui | Out-Null
    }

    $deadline = (Get-Date).AddMinutes(5)
    while ((Get-Date) -lt $deadline) {
        if (Test-Connection -ComputerName $ip -Count 1 -Quiet -ErrorAction SilentlyContinue) { break }
        Start-Sleep -Seconds 5
    }
    if (-not (Test-Connection -ComputerName $ip -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
        Write-Host "    UNREACHABLE"
        $summary += [pscustomobject]@{ Vm = $ip; Passed = 0; Failed = 1; Total = 1; Detail = "unreachable" }
        continue
    }

    $share = "\\$ip\c$"
    $fixture = "$share\FilterTest"
    New-Item -ItemType Directory -Force -Path $fixture, "$fixture\sub\deep", "$share\Test\pipeline", "$share\Test\results" | Out-Null
    foreach ($file in @("a.zip", "b.zip", "report-2026.txt", "notes.md")) {
        Set-Content -Path "$fixture\$file" -Value "x" -NoNewline
    }
    Set-Content -Path "$fixture\sub\c.zip" -Value "x" -NoNewline
    Set-Content -Path "$fixture\sub\deep\d.zip" -Value "x" -NoNewline
    Set-Content -Path "$fixture\sub\deep\deep.txt" -Value "x" -NoNewline

    Copy-Item $RunnerExe "$share\Test\pipeline\FilterUiTest.exe" -Force
    $cred = New-Object System.Management.Automation.PSCredential($vm.User, (ConvertTo-SecureString $GuestPassword -AsPlainText -Force))
    Invoke-WmiMethod -ComputerName $ip -Credential $cred -Class Win32_Process -Name Create -ArgumentList "cmd.exe /c taskkill /IM Files.exe /F /T & taskkill /IM FilterUiTest.exe /F /T" -ErrorAction SilentlyContinue | Out-Null
    Start-Sleep -Seconds 2
    if (Test-Path "$share\Test\app") { Remove-Item "$share\Test\app" -Recurse -Force -ErrorAction SilentlyContinue }
    robocopy $stageApp "$share\Test\app" /MIR /MT:16 /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null

    $bat = "@echo off`r`nC:\Test\pipeline\FilterUiTest.exe --exe C:\Test\app\Files.exe --folder C:\FilterTest --out C:\Test\results`r`n"
    [System.IO.File]::WriteAllText("$share\Test\pipeline\run.bat", $bat, (New-Object System.Text.UTF8Encoding($false)))

    Get-ChildItem "$share\Test\results" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

    & schtasks /s $ip /u $vm.User /p $GuestPassword /create /tn FilterUiTest /tr "cmd.exe /c C:\Test\pipeline\run.bat" /sc once /st 23:59 /it /f | Out-Null
    & schtasks /s $ip /u $vm.User /p $GuestPassword /run /tn FilterUiTest | Out-Null

    $deadline = (Get-Date).AddMinutes(5)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path "$share\Test\results\summary.json") { break }
        Start-Sleep -Seconds 5
    }

    $vmResultDir = Join-Path $resultsDir $ip
    New-Item -ItemType Directory -Force -Path $vmResultDir | Out-Null

    if (Test-Path "$share\Test\results\summary.json") {
        Copy-Item "$share\Test\results\*" $vmResultDir -Force
        $json = Get-Content "$vmResultDir\summary.json" -Encoding UTF8 -Raw | ConvertFrom-Json
        Write-Host ("    passed={0} failed={1}" -f $json.passed, $json.failed)
        foreach ($result in $json.results) {
            $mark = if ($result.Pass) { "PASS" } else { "FAIL" }
            Write-Host ("    {0} {1} :: {2}" -f $mark, $result.Name, $result.Detail)
        }
        $summary += [pscustomobject]@{
            Vm = $ip; Passed = $json.passed; Failed = $json.failed; Total = ($json.passed + $json.failed)
            Detail = ($json.results | ForEach-Object { "$($_.Name)=$($_.Pass)" }) -join ";"
        }
    }
    else {
        Write-Host "    NO RESULTS (no logged-on session or runner crashed)"
        Copy-Item "$share\Test\results\run.log" $vmResultDir -Force -ErrorAction SilentlyContinue
        $summary += [pscustomobject]@{ Vm = $ip; Passed = 0; Failed = 1; Total = 1; Detail = "no results" }
    }
}

$summary | Format-Table -AutoSize
$summary | ConvertTo-Json | Set-Content (Join-Path $resultsDir "pipeline-summary.json") -Encoding UTF8
Write-Host "results: $resultsDir"

if (($summary | Measure-Object -Property Failed -Sum).Sum -gt 0) {
    exit 1
}
