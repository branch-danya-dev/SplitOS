[CmdletBinding()]
param(
    [string]$DotNet = 'dotnet',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    & $DotNet test tests/SplitOS.RuntimeHost.Tests/SplitOS.RuntimeHost.Tests.csproj `
        -c $Configuration -p:Platform=x64 `
        --filter 'FullyQualifiedName~RuntimeModeOrchestratorTests' `
        --logger 'trx;LogFileName=slice03-mode-persistence.trx' `
        --results-directory artifacts/verification/slice03-mode-persistence
    if ($LASTEXITCODE -ne 0) {
        throw "Mode persistence verification failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
