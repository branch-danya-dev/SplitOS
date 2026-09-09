[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Managed-service SCM integration test requires an elevated Windows runner.'
    }
}

function Wait-ServiceState([string]$Name, [string]$Expected, [int]$TimeoutSeconds = 20) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 200
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($service -and $service.Status.ToString() -eq $Expected) { return }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $actual = if ($service) { $service.Status.ToString() } else { '<missing>' }
    throw "Service '$Name' did not reach $Expected. Actual: $actual"
}

function Select-StableFixtureService {
    # The hosted runner is disposable, but the integration gate still restores the exact
    # pre-test RUNNING state. Prefer low-impact services and refuse candidates with active
    # dependent services so ControlService(STOP) exercises only the selected target.
    $candidateNames = @('W32Time', 'WSearch', 'Spooler', 'Themes')

    foreach ($candidateName in $candidateNames) {
        $service = Get-Service -Name $candidateName -ErrorAction SilentlyContinue
        if (-not $service -or $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running -or -not $service.CanStop) {
            continue
        }

        $activeDependents = @($service.DependentServices | Where-Object {
            $_.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running
        })
        if ($activeDependents.Count -ne 0) {
            continue
        }

        $escapedName = $candidateName.Replace("'", "''")
        $cim = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedName'" -ErrorAction SilentlyContinue
        if (-not $cim -or $cim.StartMode -eq 'Disabled' -or $cim.State -ne 'Running') {
            continue
        }

        # Avoid choosing a service that happens to be racing to STOPPED on the hosted image.
        Start-Sleep -Milliseconds 750
        $stable = Get-Service -Name $candidateName -ErrorAction SilentlyContinue
        if ($stable -and $stable.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running -and $stable.CanStop) {
            return $candidateName
        }
    }

    throw 'No stable RUNNING/stoppable CI fixture service without active dependents was available.'
}

Assert-Administrator
$serviceName = Select-StableFixtureService
Write-Host "Using stable disposable-runner SCM fixture: $serviceName"

try {
    $env:SPLITOS_CI_MANAGED_SERVICE_NAME = $serviceName
    dotnet test tests/SplitOS.Broker.Tests/SplitOS.Broker.Tests.csproj `
        -c Release `
        --no-build `
        --no-restore `
        -p:Platform=x64 `
        --filter FullyQualifiedName~WindowsManagedServiceAdapterIntegrationTests
    if ($LASTEXITCODE -ne 0) {
        throw "Real SCM managed-service integration test failed with exit code $LASTEXITCODE."
    }

    Wait-ServiceState -Name $serviceName -Expected 'Running'
    Write-Host 'Managed-service SCM mutation gate passed: STOPPED and RUNNING were both native read-back verified and the original RUNNING state was restored.'
}
finally {
    Remove-Item Env:SPLITOS_CI_MANAGED_SERVICE_NAME -ErrorAction SilentlyContinue
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        Start-Service -Name $serviceName -ErrorAction SilentlyContinue
        try {
            Wait-ServiceState -Name $serviceName -Expected 'Running'
        }
        catch {
            Write-Warning "Failed to restore CI fixture service '$serviceName' to RUNNING: $($_.Exception.Message)"
        }
    }
}
