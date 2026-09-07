[CmdletBinding()]
param(
    [string]$ReleaseId = 'dev-slice00',
    [string]$InstallRoot = "$env:ProgramFiles\SplitOS\Releases"
)

$ErrorActionPreference = 'Stop'

$releaseRoot = Join-Path $InstallRoot $ReleaseId
$manager = Join-Path $releaseRoot 'Manager\SplitOS.Manager.exe'
$machineDb = Join-Path $env:ProgramData 'SplitOS\Data\machine.db'
$userDb = Join-Path $env:LocalAppData 'SplitOS\Data\user.db'
$projectionDb = Join-Path $env:LocalAppData 'SplitOS\Cache\projection.db'

foreach ($path in @($manager, $machineDb, $userDb, $projectionDb)) {
    if (-not (Test-Path $path)) { throw "SLICE-01 expected artifact/store is missing: $path" }
}

$deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
do {
    $process = Start-Process -FilePath $manager -ArgumentList '--state-probe' -Wait -PassThru
    if ($process.ExitCode -eq 0) {
        Write-Host 'SLICE-01 verification passed: Runtime READY, ManagedRuntime DISABLED, OperationalMode NONE, all three stores present.'
        exit 0
    }
    Start-Sleep -Seconds 1
} while ([DateTimeOffset]::UtcNow -lt $deadline)

throw "Manager state probe did not reach expected FREE state. Last exit code: $($process.ExitCode)"
