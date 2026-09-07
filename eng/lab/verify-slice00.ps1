[CmdletBinding()]
param(
    [string]$ReleaseRoot = "$env:ProgramFiles\SplitOS\Releases\slice00-dev",
    [int]$ProbeTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Slice-00 lab verification requires an elevated PowerShell session.'
    }
}

Assert-Administrator

$releaseRoot = [System.IO.Path]::GetFullPath($ReleaseRoot)
$statePath = Join-Path $releaseRoot 'slice00-lab-state.json'
if (-not (Test-Path $statePath)) {
    throw "Lab install state not found: $statePath"
}

$state = Get-Content $statePath -Raw | ConvertFrom-Json
$brokerExe = Join-Path $releaseRoot 'Broker\SplitOS.Broker.Service.exe'
$runtimeExe = Join-Path $releaseRoot 'RuntimeHost\SplitOS.RuntimeHost.exe'
$managerExe = Join-Path $releaseRoot 'Manager\SplitOS.Manager.exe'
$currentSession = [Diagnostics.Process]::GetCurrentProcess().SessionId

$service = Get-CimInstance Win32_Service -Filter "Name='$($state.serviceName)'"
if (-not $service) { throw 'SplitOS Broker service is missing.' }
if ($service.State -ne 'Running') { throw "Broker service is not running: $($service.State)" }
if ($service.StartMode -ne 'Auto') { throw "Broker service is not Automatic: $($service.StartMode)" }
if ($service.StartName -notin @('LocalSystem', 'LocalSystem ')) { throw "Broker service identity is '$($service.StartName)', expected LocalSystem." }

$brokerProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$($service.ProcessId)"
if (-not $brokerProcess) { throw 'Broker process is not observable.' }
if ($brokerProcess.SessionId -ne 0) { throw "Broker process session is $($brokerProcess.SessionId), expected service session 0." }
if (-not [string]::Equals(
    [System.IO.Path]::GetFullPath($brokerProcess.ExecutablePath),
    [System.IO.Path]::GetFullPath($brokerExe),
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Broker image path mismatch: $($brokerProcess.ExecutablePath)"
}

$task = Get-ScheduledTask -TaskPath $state.taskPath -TaskName $state.taskName
if (-not $task) { throw 'RuntimeHost scheduled task is missing.' }
if ($task.Principal.RunLevel -ne 'Limited') { throw "RuntimeHost task RunLevel is $($task.Principal.RunLevel), expected Limited." }

$runtimeProcess = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'SplitOS.RuntimeHost.exe' -and
    $_.SessionId -eq $currentSession -and
    $_.ExecutablePath -and
    [string]::Equals(
        [System.IO.Path]::GetFullPath($_.ExecutablePath),
        [System.IO.Path]::GetFullPath($runtimeExe),
        [StringComparison]::OrdinalIgnoreCase)
} | Select-Object -First 1

if (-not $runtimeProcess) {
    throw "RuntimeHost is not running from the expected release path in interactive session $currentSession."
}

$usersSid = 'S-1-5-32-545'
$writeMask = [Security.AccessControl.FileSystemRights]::Write -bor `
             [Security.AccessControl.FileSystemRights]::Modify -bor `
             [Security.AccessControl.FileSystemRights]::FullControl
$acl = Get-Acl $releaseRoot
$writableUsersRule = $acl.Access | Where-Object {
    try {
        $sid = $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        $sid -eq $usersSid -and
        $_.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
        (($_.FileSystemRights -band $writeMask) -ne 0)
    }
    catch {
        $false
    }
} | Select-Object -First 1

if ($writableUsersRule) {
    throw 'Builtin Users has a write-capable rule on the protected SplitOS release root.'
}

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($ProbeTimeoutSeconds)
$probeExit = $null
do {
    $probe = Start-Process -FilePath $managerExe -ArgumentList '--health-probe' -PassThru -Wait
    $probeExit = $probe.ExitCode
    if ($probeExit -eq 0) { break }
    Start-Sleep -Milliseconds 750
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if ($probeExit -ne 0) {
    throw "Manager → RuntimeHost → Broker health probe did not become healthy. Last exit code: $probeExit"
}

Write-Host 'Slice-00 Windows topology verified:'
Write-Host '  Manager (interactive user) -> RuntimeHost (limited user session) -> Broker (LocalSystem/session 0)'
Write-Host '  Exact release paths and protected release-root ACL verified.'
