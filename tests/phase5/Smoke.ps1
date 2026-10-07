param(
    [string]$EnvFile = '.env'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $EnvFile)) {
    throw "Environment file '$EnvFile' does not exist."
}

$settings = @{}
foreach ($line in Get-Content -LiteralPath $EnvFile) {
    if ($line -match '^\s*([A-Z0-9_]+)=(.*)$') {
        $settings[$Matches[1]] = $Matches[2]
    }
}

$prometheusPort = if ($settings.ContainsKey('PROMETHEUS_PORT')) { $settings['PROMETHEUS_PORT'] } else { '9090' }
$grafanaPort = if ($settings.ContainsKey('GRAFANA_PORT')) { $settings['GRAFANA_PORT'] } else { '3001' }
$prometheusUrl = "http://127.0.0.1:$prometheusPort"
$grafanaUrl = "http://127.0.0.1:$grafanaPort"

function Get-PrometheusQuery {
    param([string]$Expression)

    $encoded = [uri]::EscapeDataString($Expression)
    $response = Invoke-RestMethod -Uri "$prometheusUrl/api/v1/query?query=$encoded"
    if ($response.status -ne 'success') {
        throw "Prometheus query failed: $Expression"
    }
    return $response.data.result
}

$expectedServices = @(
    'Aiyara.Gateways.Api',
    'Aiyara.Identities.Api',
    'Aiyara.Timesheet.Api',
    'Aiyara.Report.Api',
    'Aiyara.Notifications.Api',
    'Aiyara.Report.Worker'
)

$requestSeries = @()
for ($attempt = 0; $attempt -lt 24; $attempt++) {
    $requestSeries = @(Get-PrometheusQuery 'count by (service_name) (http_server_request_duration_seconds_count)')
    $services = @($requestSeries | ForEach-Object { $_.metric.service_name })
    $missingServices = @($expectedServices | Where-Object { $_ -notin $services })
    if ($missingServices.Count -eq 0) { break }
    Start-Sleep -Seconds 5
}

if ($missingServices.Count -gt 0) {
    throw "Backend request metrics missing for: $($missingServices -join ', '). Ensure the Compose stack is healthy and wait for an OTLP export interval."
}

$latencySeries = @(Get-PrometheusQuery 'histogram_quantile(0.95, sum by (service_name, le) (rate(http_server_request_duration_seconds_bucket[5m])))')
$memorySeries = @(Get-PrometheusQuery 'dotnet_process_memory_working_set_bytes')
if ($latencySeries.Count -eq 0) { throw 'Prometheus has no HTTP latency histogram data.' }
if ($memorySeries.Count -lt $expectedServices.Count) { throw 'Prometheus has incomplete .NET process memory data.' }

$healthSeries = @()
for ($attempt = 0; $attempt -lt 24; $attempt++) {
    $healthSeries = @(Get-PrometheusQuery 'aiyara_service_health_ratio')
    $healthServices = @($healthSeries | ForEach-Object { $_.metric.service_name })
    $missingHealth = @($expectedServices | Where-Object { $_ -notin $healthServices })
    if ($missingHealth.Count -eq 0) { break }
    Start-Sleep -Seconds 5
}

if ($missingHealth.Count -gt 0) {
    throw "Readiness metrics missing for: $($missingHealth -join ', ')."
}
$unhealthyServices = @($healthSeries | Where-Object { $_.value[1] -ne '1' })
if ($unhealthyServices.Count -gt 0) {
    $names = @($unhealthyServices | ForEach-Object { $_.metric.service_name })
    throw "Services report unhealthy readiness: $($names -join ', ')."
}

$username = $settings['GRAFANA_ADMIN_USER']
$password = $settings['GRAFANA_ADMIN_PASSWORD']
if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)) {
    throw 'GRAFANA_ADMIN_USER and GRAFANA_ADMIN_PASSWORD must be set in the environment file.'
}

$securePassword = ConvertTo-SecureString $password -AsPlainText -Force
$credential = [System.Management.Automation.PSCredential]::new($username, $securePassword)
# Credentials are sent only to the local loopback Grafana endpoint.
$datasource = Invoke-RestMethod -Uri "$grafanaUrl/api/datasources/uid/prometheus" `
    -Authentication Basic -Credential $credential -AllowUnencryptedAuthentication
$dashboard = Invoke-RestMethod -Uri "$grafanaUrl/api/dashboards/uid/aiyara-platform-overview" `
    -Authentication Basic -Credential $credential -AllowUnencryptedAuthentication

if ($datasource.name -ne 'Prometheus') { throw 'Grafana Prometheus data source is not provisioned.' }
if ($dashboard.dashboard.title -ne 'Aiyara Platform Overview') { throw 'Grafana platform dashboard is not provisioned.' }
if ($dashboard.dashboard.panels.Count -ne 5) { throw 'Grafana platform dashboard should contain five panels.' }

Write-Output 'Phase 5 observability smoke test passed: six services export request, runtime, and readiness metrics; Grafana has its provisioned data source and dashboard.'
