#requires -Version 5.1
<#
.SYNOPSIS
Runs the packaged Files app through the UI test runner inside the test VMs.

.DESCRIPTION
Deploys the portable artifact and the compiled FilterUiTest runner over SMB, then
uses WinSimpleSSH (service mode) to execute the runner in the VM's interactive
session. Results are copied back over SMB and summarized.

.EXAMPLE
$env:FILES_VM_PASSWORD = '<password>'
powershell -File tools/ui-pipeline/run-pipeline.ps1 -ArtifactZip artifacts/internal/4.3.3.3/portable/Files.Internal_4.3.3.3_x64-portable.zip
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactZip,

    [string]$RunnerExe = "",

    [string]$SshcPath = "D:\tools\WinSimpleSSH\src\SSHClient\bin\Release\net452\SSHC.exe",

    [string]$SshServerExe = "D:\tools\WinSimpleSSH\src\SSHServer\bin\Release\net452\SSHServer.exe",

    [string]$ResultsRoot = "artifacts/ui-pipeline/results",

    [string]$GuestPassword = $env:FILES_VM_PASSWORD,

    [switch]$RevertSnapshots,

    [string]$OnlyIp = ""
)

$ErrorActionPreference = "Continue"
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

if (-not $GuestPassword) { throw "Set FILES_VM_PASSWORD or pass -GuestPassword (VM local admin password)." }
if (-not (Test-Path $ArtifactZip)) { throw "artifact not found: $ArtifactZip" }
if (-not (Test-Path $SshcPath)) { throw "SSHC not found: $SshcPath (build WinSimpleSSH first)" }

if (-not $RunnerExe) {
    $RunnerExe = Join-Path $PSScriptRoot "..\..\artifacts\ui-pipeline\publish\FilterUiTest.exe"
}
if (-not (Test-Path $RunnerExe)) { throw "runner not found: $RunnerExe (publish FilesFilterUiTests first)" }

$env:SSHC_PASSWORD = $GuestPassword

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultsDir = Join-Path $ResultsRoot $stamp
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$stageApp = Join-Path $env:TEMP "files-ui-stage-$stamp"
Write-Host "==> staging artifact"
Expand-Archive -Path $ArtifactZip -DestinationPath $stageApp -Force

function New-Credential([pscustomobject]$Vm) {
    $secure = ConvertTo-SecureString $GuestPassword -AsPlainText -Force
    New-Object System.Management.Automation.PSCredential($Vm.User, $secure)
}

function Ensure-SshService([pscustomobject]$Vm) {
    if (Test-NetConnection -ComputerName $Vm.Ip -Port 22222 -InformationLevel Quiet -WarningAction SilentlyContinue) {
        return
    }

    Write-Host "    bootstrap WinSimpleSSH service"
    $share = "\\$($Vm.Ip)\c$"
    New-Item -ItemType Directory -Force -Path "$share\Test\ssh" | Out-Null
    Copy-Item $SshServerExe "$share\Test\ssh\SSHServer.exe" -Force
    $json = '{"port":22222,"ipWhitelist":["192.168.221.*"],"users":[{"username":"' + $Vm.User + '","password":"' + $GuestPassword + '"}]}'
    [System.IO.File]::WriteAllText("$share\Test\ssh\server.json", $json, (New-Object System.Text.UTF8Encoding($false)))
    Invoke-WmiMethod -ComputerName $Vm.Ip -Credential (New-Credential $Vm) -Class Win32_Process -Name Create -ArgumentList "cmd.exe /c C:\Test\ssh\SSHServer.exe --install" -ErrorAction SilentlyContinue | Out-Null

    $deadline = (Get-Date).AddMinutes(2)
    while ((Get-Date) -lt $deadline) {
        if (Test-NetConnection -ComputerName $Vm.Ip -Port 22222 -InformationLevel Quiet -WarningAction SilentlyContinue) { return }
        Start-Sleep -Seconds 5
    }

    throw "WinSimpleSSH service did not start on $($Vm.Ip)"
}

function Invoke-SshExec([pscustomobject]$Vm, [string]$Command) {
    & $SshcPath exec $Vm.Ip -u $Vm.User --json $Command 2>$null | Out-Null
    return $LASTEXITCODE
}

function Invoke-SshStart([pscustomobject]$Vm, [string]$Program) {
    & $SshcPath start $Vm.Ip -u $Vm.User $Program 2>$null | Out-Null
    return $LASTEXITCODE
}

$summary = @()

foreach ($vm in $vms) {
    if ($OnlyIp -and $vm.Ip -ne $OnlyIp) { continue }

    Write-Host "==> $($vm.Ip)"
    Ensure-SshService $vm

    if ($RevertSnapshots) {
        Write-Host "    revert snapshot initial"
        & $vmrun -T ws revertToSnapshot $vm.Vmx initial | Out-Null
        & $vmrun -T ws start $vm.Vmx nogui | Out-Null
        $deadline = (Get-Date).AddMinutes(5)
        while ((Get-Date) -lt $deadline) {
            if (Test-NetConnection -ComputerName $vm.Ip -Port 22222 -InformationLevel Quiet -WarningAction SilentlyContinue) { break }
            Start-Sleep -Seconds 5
        }
        Ensure-SshService $vm
    }

    $share = "\\$($vm.Ip)\c$"
    $fixture = "$share\FilterTest"
    New-Item -ItemType Directory -Force -Path $fixture, "$fixture\sub\deep", "$share\Test\pipeline", "$share\Test\results" | Out-Null
    foreach ($file in @("a.zip", "b.zip", "report-2026.txt", "notes.md")) {
        Set-Content -Path "$fixture\$file" -Value "x" -NoNewline
    }
    Set-Content -Path "$fixture\sub\c.zip" -Value "x" -NoNewline
    Set-Content -Path "$fixture\sub\deep\d.zip" -Value "x" -NoNewline
    Set-Content -Path "$fixture\sub\deep\deep.txt" -Value "x" -NoNewline

    Copy-Item $RunnerExe "$share\Test\pipeline\FilterUiTest.exe" -Force
    Invoke-SshExec $vm "taskkill /IM Files.exe /F /T & taskkill /IM FilterUiTest.exe /F /T" | Out-Null
    if (Test-Path "$share\Test\app") { Remove-Item "$share\Test\app" -Recurse -Force -ErrorAction SilentlyContinue }
    robocopy $stageApp "$share\Test\app" /MIR /MT:16 /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null

    $bat = "@echo off`r`nC:\Test\pipeline\FilterUiTest.exe --exe C:\Test\app\Files.exe --folder C:\FilterTest --out C:\Test\results`r`n"
    [System.IO.File]::WriteAllText("$share\Test\pipeline\run.bat", $bat, (New-Object System.Text.UTF8Encoding($false)))

    Get-ChildItem "$share\Test\results" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

    Write-Host "    run UI suite"
    Invoke-SshStart $vm "C:\Test\pipeline\run.bat" | Out-Null

    $deadline = (Get-Date).AddMinutes(5)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path "$share\Test\results\summary.json") { break }
        Start-Sleep -Seconds 5
    }

    $vmResultDir = Join-Path $resultsDir $vm.Ip
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
            Vm = $vm.Ip; Passed = $json.passed; Failed = $json.failed; Total = ($json.passed + $json.failed)
            Detail = ($json.results | ForEach-Object { "$($_.Name)=$($_.Pass)" }) -join ";"
        }
    }
    else {
        Write-Host "    NO RESULTS (interactive session missing or runner crashed)"
        Copy-Item "$share\Test\results\run.log" $vmResultDir -Force -ErrorAction SilentlyContinue
        $summary += [pscustomobject]@{ Vm = $vm.Ip; Passed = 0; Failed = 1; Total = 1; Detail = "no results" }
    }
}

$summary | Format-Table -AutoSize
$summary | ConvertTo-Json | Set-Content (Join-Path $resultsDir "pipeline-summary.json") -Encoding UTF8
Write-Host "results: $resultsDir"

if (($summary | Measure-Object -Property Failed -Sum).Sum -gt 0) {
    exit 1
}
