# Runs Identity, Gateway, Report, and Timesheet with `dotnet run --launch-profile http` and calls
# every GET in each project's .http file. The APIs fail fast without their connection strings and
# keys, so each one receives the settings Compose gives its container, with Compose service names
# mapped to the ports Compose publishes on the host. Run from the repository root with the Compose
# stack up and the solution built. Identity's gRPC listens on -IdentityGrpcPort for the others.
param(
    [string]$EnvFile = '.env',
    [int]$IdentityGrpcPort = 18082,
    [int]$StartTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$logDir = Join-Path ([IO.Path]::GetTempPath()) 'aiyara-http-files'
New-Item -ItemType Directory -Force $logDir | Out-Null
$compose = (docker compose --env-file $EnvFile config --format json | ConvertFrom-Json).services
if ($LASTEXITCODE -ne 0) { throw 'docker compose config failed.' }

$apis = @(
    @{ Name = 'Identity';  Service = 'identities-api'; Project = 'src/Backend/identities/Aiyara.Identities.Api' },
    @{ Name = 'Gateway';   Service = 'gateway-api';    Project = 'src/Backend/gateways/Aiyara.Gateways.Api' },
    @{ Name = 'Report';    Service = 'reports-api';    Project = 'src/Backend/reports/Aiyara.Report.Api' },
    @{ Name = 'Timesheet'; Service = 'timesheet-api';  Project = 'src/Backend/timesheet/Aiyara.Timesheet.Api' }
)

function Read-HttpFile([string]$project) {
    $file = Get-ChildItem "$project/*.http" | Select-Object -First 1
    $hostLine = Get-Content $file | Where-Object { $_ -match '^@\S+_HostAddress\s*=' } | Select-Object -First 1
    @{
        Base  = ($hostLine -split '=', 2)[1].Trim()
        Paths = @(Get-Content $file | Where-Object { $_ -match '^GET ' } | ForEach-Object { ($_ -split '}}', 2)[1].Trim() })
    }
}

# Compose service names resolve only inside the Compose network.
function Map-Value([string]$value) {
    $identityHttp = (Read-HttpFile $apis[0].Project).Base
    $value = $value -replace 'Host=postgres\b', 'Host=localhost'
    $value = $value -replace '\bredis:6379', 'localhost:6379'
    $value = $value -replace 'http://identities-api:8082', "http://localhost:$IdentityGrpcPort"
    $value = $value -replace 'http://identities-api:8080', $identityHttp
    $value = $value -replace 'http://(rustfs|prometheus|jaeger)(:\d+)', 'http://localhost$2'
    if ($value -in 'postgres', 'redis', 'rabbitmq', 'rustfs') { $value = 'localhost' }
    $value
}

function Get-ServiceEnvironment($api) {
    $result = @{}
    foreach ($setting in $compose.($api.Service).environment.PSObject.Properties) {
        # The launch profile supplies the environment and URL.
        if ($setting.Name -eq 'ASPNETCORE_ENVIRONMENT' -or $setting.Name -like 'ASPNETCORE_Kestrel__*') { continue }
        $result[$setting.Name] = Map-Value ([string]$setting.Value)
    }
    if ($api.Name -eq 'Identity') {
        # Configuring any Kestrel endpoint replaces the launch profile URL, so restate it next to gRPC.
        $result['ASPNETCORE_Kestrel__Endpoints__Http__Url'] = (Read-HttpFile $api.Project).Base
        $result['ASPNETCORE_Kestrel__Endpoints__Grpc__Url'] = "http://localhost:$IdentityGrpcPort"
        $result['ASPNETCORE_Kestrel__Endpoints__Grpc__Protocols'] = 'Http2'
        # The Compose stack's Identity owns the Platform Admin bootstrap.
        $result['Bootstrap__PlatformAdminEmail'] = ''
    }
    $result
}

function Start-Api($api) {
    $log = Join-Path $logDir "$($api.Name).log"
    Start-Process dotnet -ArgumentList @('run', '--project', $api.Project, '--launch-profile', 'http', '--no-build') `
        -Environment (Get-ServiceEnvironment $api) -RedirectStandardOutput $log -RedirectStandardError "$log.err" `
        -PassThru -WindowStyle Hidden
}

function Stop-Api($process) {
    if ($process -and -not $process.HasExited) {
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" -ErrorAction SilentlyContinue |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}

function Test-Api($api, $process) {
    $http = Read-HttpFile $api.Project
    $started = $false
    for ($attempt = 0; $attempt -lt $StartTimeoutSeconds -and -not $process.HasExited; $attempt++) {
        try { Invoke-WebRequest "$($http.Base)/alive" -TimeoutSec 2 -SkipHttpErrorCheck | Out-Null; $started = $true; break }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $started) { throw "$($api.Name) did not start; see $(Join-Path $logDir "$($api.Name).log")." }
    $results = foreach ($path in $http.Paths) {
        $response = Invoke-WebRequest "$($http.Base)$path" -TimeoutSec 20 -SkipHttpErrorCheck
        if ($response.StatusCode -ne 200) { throw "$($api.Name) GET $path returned $($response.StatusCode)." }
        "$path 200"
    }
    Write-Host ("{0,-10} {1}  ({2})" -f $api.Name, ($results -join ' | '), $http.Base)
}

$identity = $null
try {
    # Identity stays up so the other APIs' Identity gRPC dependency is ready.
    $identity = Start-Api $apis[0]
    Test-Api $apis[0] $identity
    foreach ($api in $apis[1..3]) {
        $process = Start-Api $api
        try { Test-Api $api $process } finally { Stop-Api $process }
    }
}
finally { Stop-Api $identity }
Write-Host '.http files passed.'
