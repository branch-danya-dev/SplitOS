[CmdletBinding()]
param(
    [string]$ReleaseRoot = "$env:ProgramFiles\SplitOS\Releases\slice00-dev",
    [switch]$RemoveReleaseFiles
)

$ErrorActionPreference = 'Stop'

$serviceName = 'SplitOSBroker'
$taskPath = '\SplitOS\'
$taskName = 'RuntimeHost-Slice00-Lab'

Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
& sc.exe delete $serviceName 2>$null | Out-Null

Unregister-ScheduledTask -TaskPath $taskPath -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

Get-Process -Name 'SplitOS.RuntimeHost' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if ($RemoveReleaseFiles -and (Test-Path $ReleaseRoot)) {
    Start-Sleep -Milliseconds 500
    Remove-Item -Path $ReleaseRoot -Recurse -Force
}

Write-Host 'Slice-00 lab service/task topology removed.'
