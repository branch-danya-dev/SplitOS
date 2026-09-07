[CmdletBinding()]
param(
    [string]$PublishRoot = (Join-Path $PSScriptRoot '..\..\artifacts\publish'),
    [string]$ReleaseId = 'slice00-dev',
    [string]$InstallBase = "$env:ProgramFiles\SplitOS\Releases"
)

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Slice-00 lab installation requires an elevated PowerShell session.'
    }
}

Assert-Administrator

$publish = [System.IO.Path]::GetFullPath($PublishRoot)
$releaseRoot = Join-Path $InstallBase $ReleaseId
$serviceName = 'SplitOSBroker'
$taskPath = '\SplitOS\'
$taskName = 'RuntimeHost-Slice00-Lab'
$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$currentSession = [Diagnostics.Process]::GetCurrentProcess().SessionId

$requiredDirectories = @('Broker', 'RuntimeHost', 'Manager', 'GameLauncher')
foreach ($directory in $requiredDirectories) {
    $source = Join-Path $publish $directory
    if (-not (Test-Path $source)) {
        throw "Published component directory was not found: $source"
    }
}

if (Test-Path $releaseRoot) {
    Remove-Item -Path $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

foreach ($directory in $requiredDirectories) {
    Copy-Item -Path (Join-Path $publish $directory) -Destination $releaseRoot -Recurse -Force
}

# Protect the release root from ordinary-user writes while preserving read/execute.
& icacls.exe $releaseRoot '/inheritance:r' | Out-Null
& icacls.exe $releaseRoot '/grant:r' '*S-1-5-18:(OI)(CI)F' | Out-Null
& icacls.exe $releaseRoot '/grant:r' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
& icacls.exe $releaseRoot '/grant:r' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null

$brokerExe = Join-Path $releaseRoot 'Broker\SplitOS.Broker.Service.exe'
$runtimeExe = Join-Path $releaseRoot 'RuntimeHost\SplitOS.RuntimeHost.exe'

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existingService) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Milliseconds 500
}

New-Service `
    -Name $serviceName `
    -BinaryPathName ('"{0}"' -f $brokerExe) `
    -DisplayName 'SplitOS Privileged Broker' `
    -Description 'SplitOS bounded privileged broker.' `
    -StartupType Automatic | Out-Null

Start-Service -Name $serviceName

$action = New-ScheduledTaskAction -Execute $runtimeExe
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
$principal = New-ScheduledTaskPrincipal `
    -UserId $currentUser `
    -LogonType Interactive `
    -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero)

$task = New-ScheduledTask `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings

Register-ScheduledTask `
    -TaskPath $taskPath `
    -TaskName $taskName `
    -InputObject $task `
    -Force | Out-Null

Start-ScheduledTask -TaskPath $taskPath -TaskName $taskName

$state = [pscustomobject]@{
    releaseId = $ReleaseId
    releaseRoot = $releaseRoot
    serviceName = $serviceName
    taskPath = $taskPath
    taskName = $taskName
    windowsUser = $currentUser
    interactiveSessionId = $currentSession
    installedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}

$statePath = Join-Path $releaseRoot 'slice00-lab-state.json'
$state | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $statePath

Write-Host "Slice-00 lab topology installed to $releaseRoot"
Write-Host "State: $statePath"
