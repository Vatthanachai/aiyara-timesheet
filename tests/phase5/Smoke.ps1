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
$gatewayPort = if ($settings.ContainsKey('GATEWAY_API_PORT')) { $settings['GATEWAY_API_PORT'] } else { '8081' }
$jaegerPort = if ($settings.ContainsKey('JAEGER_UI_PORT')) { $settings['JAEGER_UI_PORT'] } else { '16686' }
$prometheusUrl = "http://127.0.0.1:$prometheusPort"
$grafanaUrl = "http://127.0.0.1:$grafanaPort"
$jaegerUrl = "http://127.0.0.1:$jaegerPort"

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

$reportRunSeries = @()
$expectedReportStatuses = @('queued', 'running', 'succeeded', 'failed')
for ($attempt = 0; $attempt -lt 24; $attempt++) {
    $reportRunSeries = @(Get-PrometheusQuery 'aiyara_report_runs')
    $reportStatuses = @($reportRunSeries | ForEach-Object { $_.metric.status })
    $missingStatuses = @($expectedReportStatuses | Where-Object { $_ -notin $reportStatuses })
    if ($missingStatuses.Count -eq 0) { break }
    Start-Sleep -Seconds 5
}

if ($missingStatuses.Count -gt 0) {
    throw "Report run metrics missing status series: $($missingStatuses -join ', ')."
}

$authProbePassword = "phase5-probe-$([guid]::NewGuid().ToString('N'))"
$probeTraceId = [guid]::NewGuid().ToString('N')
$probeSpanId = [guid]::NewGuid().ToString('N').Substring(0, 16)
$authProbeBody = @{ tenantId = '00000000-0000-0000-0000-000000000001';
    email = 'phase5-probe@invalid.example'; password = $authProbePassword } | ConvertTo-Json
$authProbe = Invoke-WebRequest -Uri "http://127.0.0.1:$gatewayPort/api/v1/auth/login" `
    -Method Post -ContentType 'application/json' -Body $authProbeBody `
    -Headers @{ traceparent = "00-$probeTraceId-$probeSpanId-01" } -SkipHttpErrorCheck
if ($authProbe.StatusCode -lt 400 -or $authProbe.StatusCode -ge 500) {
    throw "Authentication metrics probe returned unexpected HTTP $($authProbe.StatusCode)."
}
$authMetricSeries = @()
for ($attempt = 0; $attempt -lt 24; $attempt++) {
    $authMetricSeries = @(Get-PrometheusQuery 'aiyara_authentication_attempts_total{action="login",outcome="failure"}')
    if ($authMetricSeries.Count -gt 0) { break }
    Start-Sleep -Seconds 5
}
if ($authMetricSeries.Count -eq 0) { throw 'Prometheus has no authentication failure metric after the probe.' }

$traceResult = $null
for ($attempt = 0; $attempt -lt 24; $attempt++) {
    try { $traceResult = Invoke-RestMethod -Uri "$jaegerUrl/api/v3/traces/$probeTraceId" }
    catch { $traceResult = $null }
    if ($traceResult.result.resourceSpans.Count -gt 0) { break }
    Start-Sleep -Seconds 5
}
if ($null -eq $traceResult -or $traceResult.result.resourceSpans.Count -eq 0) {
    throw 'Jaeger has not received the authentication probe trace.'
}
$traceServices = @($traceResult.result.resourceSpans | ForEach-Object {
    $_.resource.attributes | Where-Object { $_.key -eq 'service.name' } |
        ForEach-Object { $_.value.stringValue }
})
$loginSpans = @($traceResult.result.resourceSpans | ForEach-Object {
    $_.scopeSpans | ForEach-Object { $_.spans } |
        Where-Object { $_.name -eq 'POST /api/v1/auth/login' }
})
if ('Aiyara.Gateways.Api' -notin $traceServices -or
    'Aiyara.Identities.Api' -notin $traceServices -or $loginSpans.Count -eq 0) {
    throw 'The authentication probe trace is missing Gateway or Identity login spans.'
}
$traceJson = $traceResult | ConvertTo-Json -Depth 50 -Compress
if ($traceJson.Contains($authProbePassword)) {
    throw 'An authentication probe password appeared in an exported trace.'
}

foreach ($service in @('gateway-api', 'identities-api')) {
    $containers = @(& docker ps --filter "label=com.docker.compose.service=$service" --format '{{.ID}}')
    if ($LASTEXITCODE -ne 0 -or $containers.Count -eq 0) {
        throw "Cannot inspect $service logs for the secret-redaction check."
    }
    foreach ($container in $containers) {
        $containerLogs = (& docker logs $container --since 10m 2>&1 | Out-String)
        if ($containerLogs.Contains($authProbePassword)) {
            throw "An authentication probe password appeared in $service logs."
        }
    }
}

$rabbitMetrics = @(Get-PrometheusQuery 'aiyara_rabbitmq_queue_messages')
if (-not ($rabbitMetrics | Where-Object { $_.metric.queue -eq 'reporting.generate.v1' })) {
    throw 'Prometheus has no report generation RabbitMQ queue metrics.'
}

$quartzMetrics = @()
for ($attempt = 0; $attempt -lt 24; $attempt++) {
    $quartzMetrics = @(Get-PrometheusQuery 'aiyara_quartz_job_executions_total')
    if ($quartzMetrics.Count -gt 0) { break }
    Start-Sleep -Seconds 5
}
if ($quartzMetrics.Count -eq 0) { throw 'Prometheus has no Quartz job execution metrics.' }

$rustFsMetrics = @(Get-PrometheusQuery 'aiyara_rustfs_operations_total')
if ($rustFsMetrics.Count -eq 0) {
    throw 'Prometheus has no RustFS operation metrics. Run tests/phase4/Smoke.ps1 first to exercise report storage.'
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
if ($dashboard.dashboard.panels.Count -ne 10) { throw 'Grafana platform dashboard should contain ten panels.' }

Write-Output 'Phase 5 observability smoke test passed: backend, authentication, RabbitMQ, Quartz, report run, and RustFS metrics are present; Jaeger received the Gateway/Identity probe trace without its password; both service logs are redacted; Grafana has its provisioned data source and dashboard.'
