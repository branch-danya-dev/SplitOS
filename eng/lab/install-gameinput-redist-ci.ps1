[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$packagesPropsPath = Join-Path $repoRoot 'Directory.Packages.props'

[xml]$packagesProps = Get-Content -Path $packagesPropsPath -Raw
$gameInputVersionNode = $packagesProps.Project.ItemGroup.PackageVersion |
    Where-Object { $_.Include -eq 'Microsoft.GameInput' } |
    Select-Object -First 1

if ($null -eq $gameInputVersionNode -or [string]::IsNullOrWhiteSpace($gameInputVersionNode.Version)) {
    throw 'Microsoft.GameInput version was not found in Directory.Packages.props.'
}

$version = [string]$gameInputVersionNode.Version
$packagesRoot = if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    $env:NUGET_PACKAGES
} else {
    Join-Path $env:USERPROFILE '.nuget\packages'
}

$msiPath = Join-Path $packagesRoot "microsoft.gameinput\$version\redist\GameInputRedist.msi"
if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) {
    throw "GameInput redistributable MSI was not restored at '$msiPath'."
}

Write-Host "Provisioning Microsoft.GameInput $version redistributable from '$msiPath'."
$installer = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') `
    -ArgumentList @('/i', "`"$msiPath`"", '/qn', '/norestart') `
    -Wait `
    -PassThru

if ($installer.ExitCode -notin @(0, 3010)) {
    throw "GameInputRedist.msi failed with exit code $($installer.ExitCode)."
}

$systemRedist = Join-Path $env:SystemRoot 'System32\GameInputRedist.dll'
$registeredRedist = $null
$baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::LocalMachine,
    [Microsoft.Win32.RegistryView]::Registry32)
try {
    $key = $baseKey.OpenSubKey('SOFTWARE\Microsoft\GameInput', $false)
    try {
        $redistDirectory = if ($null -ne $key) { [string]$key.GetValue('RedistDir') } else { $null }
        if (-not [string]::IsNullOrWhiteSpace($redistDirectory)) {
            $registeredRedist = Join-Path $redistDirectory 'GameInputRedist.dll'
        }
    } finally {
        if ($null -ne $key) { $key.Dispose() }
    }
} finally {
    $baseKey.Dispose()
}

$resolvedRedist = @($systemRedist, $registeredRedist) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($resolvedRedist)) {
    throw 'GameInput redistributable installation completed but GameInputRedist.dll could not be resolved.'
}

$versionInfo = (Get-Item -LiteralPath $resolvedRedist).VersionInfo.FileVersion
Write-Host "GameInput redistributable ready: '$resolvedRedist' (file version $versionInfo)."
