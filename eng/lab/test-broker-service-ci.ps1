[CmdletBinding()]
param(
    [string]$PublishRoot = (Join-Path $PSScriptRoot '..\..\artifacts\publish')
)

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Broker service smoke test requires an elevated Windows runner.'
    }
}

Assert-Administrator

$serviceName = 'SplitOSBrokerCiSmoke'
$installRoot = Join-Path $env:ProgramData ("SplitOS-CI-{0}" -f [Guid]::NewGuid().ToString('N'))
$brokerRoot = Join-Path $installRoot 'Broker'
$sourceBroker = Join-Path ([System.IO.Path]::GetFullPath($PublishRoot)) 'Broker'
$brokerExe = Join-Path $brokerRoot 'SplitOS.Broker.Service.exe'
$splitOsDataRoot = Join-Path $env:ProgramData 'SplitOS'
$createdSplitOsDataRoot = -not (Test-Path $splitOsDataRoot)

if (-not $createdSplitOsDataRoot) {
    throw "Broker CI smoke refuses to run because an existing SplitOS machine-data root is present: $splitOsDataRoot"
}

try {
    if (-not (Test-Path $sourceBroker)) { throw "Published Broker directory was not found: $sourceBroker" }
    New-Item -ItemType Directory -Path $brokerRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceBroker '*') -Destination $brokerRoot -Recurse -Force

    $existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($existing) {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $serviceName | Out-Null
        Start-Sleep -Milliseconds 500
    }

    New-Service -Name $serviceName -BinaryPathName ('"{0}"' -f $brokerExe) -DisplayName 'SplitOS Broker CI Smoke' -StartupType Automatic | Out-Null
    Start-Service -Name $serviceName

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    } while ($service.State -ne 'Running' -and [DateTimeOffset]::UtcNow -lt $deadline)

    if ($service.State -ne 'Running') { throw "Broker service did not reach Running state. Current state: $($service.State)" }
    if ($service.StartMode -ne 'Auto') { throw "Broker service StartMode must be Auto, got $($service.StartMode)." }
    if ($service.StartName -notin @('LocalSystem', 'LocalSystem ')) { throw "Broker service must run as LocalSystem, got '$($service.StartName)'." }
    if (-not $service.ProcessId) { throw 'Broker service has no process id.' }

    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($service.ProcessId)"
    if (-not $process) { throw 'Broker process could not be resolved through Win32_Process.' }
    if ($process.SessionId -ne 0) { throw "Broker must execute in service session 0, got session $($process.SessionId)." }
    if (-not [string]::Equals([System.IO.Path]::GetFullPath($process.ExecutablePath), [System.IO.Path]::GetFullPath($brokerExe), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Broker process image path mismatch. Actual: $($process.ExecutablePath)"
    }

    $machineDb = Join-Path $splitOsDataRoot 'Data\machine.db'
    $marker = Join-Path $splitOsDataRoot 'Data\machine-store.initialized'
    if (-not (Test-Path $machineDb)) { throw 'Broker did not bootstrap machine.db.' }
    if (-not (Test-Path $marker)) { throw 'Broker did not create the canonical machine-store bootstrap marker.' }

    Write-Host 'Broker Windows Service smoke passed: LocalSystem, Automatic, Session 0, exact image, machine canonical store bootstrapped.'
}
finally {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName 2>$null | Out-Null
    Start-Sleep -Milliseconds 500
    Remove-Item -Path $installRoot -Recurse -Force -ErrorAction SilentlyContinue
    if ($createdSplitOsDataRoot) { Remove-Item -Path $splitOsDataRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
