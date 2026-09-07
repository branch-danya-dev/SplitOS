[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\publish')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = [System.IO.Path]::GetFullPath($OutputRoot)

$projects = @(
    @{ Name = 'RuntimeHost'; Path = 'src\SplitOS.RuntimeHost\SplitOS.RuntimeHost.csproj' },
    @{ Name = 'Broker'; Path = 'src\SplitOS.Broker.Service\SplitOS.Broker.Service.csproj' },
    @{ Name = 'Manager'; Path = 'src\SplitOS.Manager\SplitOS.Manager.csproj' },
    @{ Name = 'GameLauncher'; Path = 'src\SplitOS.GameLauncher\SplitOS.GameLauncher.csproj' }
)

if (Test-Path $output) {
    Remove-Item -Path $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

foreach ($project in $projects) {
    $target = Join-Path $output $project.Name
    dotnet publish (Join-Path $repoRoot $project.Path) `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:Platform=x64 `
        -o $target

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $($project.Name)."
    }
}

$files = Get-ChildItem -Path $output -File -Recurse | ForEach-Object {
    [pscustomobject]@{
        path = [System.IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/')
        length = $_.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash.ToLowerInvariant()
    }
}

$manifest = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    configuration = $Configuration
    architecture = 'x64'
    files = $files
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $output 'artifact-manifest.json')
Write-Host "Published SplitOS Slice-00 artifacts to $output"
