param(
    [string]$EnvFile = '.env',
    [switch]$Start,
    [switch]$CheckFailure
)

$ErrorActionPreference = 'Stop'
$compose = @('--env-file', $EnvFile)

if (-not (Test-Path -LiteralPath $EnvFile)) {
    throw "Environment file '$EnvFile' does not exist. Copy .env.example to .env and set local secrets."
}

function Invoke-Compose {
    param([string[]]$Arguments)

    & docker compose @compose @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

Invoke-Compose -Arguments @('config', '--quiet')
if ($Start) {
    Invoke-Compose -Arguments @('up', '-d', '--build', '--wait')
}

$services = @(
    'gateway-api', 'identities-api', 'timesheet-api', 'reports-api',
    'notifications-api', 'report-worker', 'frontend', 'timesheet-remote',
    'reporting-remote', 'administration-remote', 'identity-profile-remote',
    'postgres', 'redis', 'rabbitmq', 'rustfs', 'prometheus', 'grafana'
)

foreach ($service in $services) {
    $containerId = & docker compose @compose ps -q $service
    if ($LASTEXITCODE -ne 0 -or -not $containerId) {
        throw "$service is not running."
    }

    $health = & docker inspect --format '{{.State.Health.Status}}' $containerId
    if ($LASTEXITCODE -ne 0 -or $health -ne 'healthy') {
        throw "$service is $health."
    }
    Write-Host "$service healthy"
}

foreach ($service in @('gateway-api', 'identities-api', 'timesheet-api', 'reports-api', 'notifications-api')) {
    & docker compose @compose exec -T $service curl --fail --silent --output /dev/null http://localhost:8080/health
    if ($LASTEXITCODE -ne 0) { throw "$service /health failed." }

    & docker compose @compose exec -T $service curl --fail --silent --output /dev/null http://localhost:8080/openapi/v1.json
    if ($LASTEXITCODE -ne 0) { throw "$service OpenAPI failed." }
}

& docker compose @compose exec -T report-worker curl --fail --silent --output /dev/null http://localhost:8080/health
if ($LASTEXITCODE -ne 0) { throw 'report-worker /health failed.' }

if ($CheckFailure) {
    $redisContainer = & docker compose @compose ps -q redis
    if ($LASTEXITCODE -ne 0 -or -not $redisContainer) { throw 'Redis container is missing.' }

    try {
        & docker stop $redisContainer | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not stop Redis for readiness test.' }

        $alive = & docker compose @compose exec -T identities-api curl --silent --output /dev/null --write-out '%{http_code}' http://localhost:8080/alive
        $ready = & docker compose @compose exec -T identities-api curl --silent --output /dev/null --write-out '%{http_code}' http://localhost:8080/health
        if ($alive -ne '200' -or $ready -ne '503') {
            throw "Expected Identity /alive=200 and /health=503 with Redis stopped; got $alive and $ready."
        }
    }
    finally {
        & docker start $redisContainer | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not restart Redis after readiness test.' }
    }

    $restored = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $health = & docker inspect --format '{{.State.Health.Status}}' $redisContainer
        if ($health -eq 'healthy') {
            $restored = $true
            break
        }
        Start-Sleep -Seconds 1
    }
    if (-not $restored) { throw 'Redis did not recover after the readiness test.' }

    Write-Host 'Dependency failure readiness test passed.'
}

Write-Host 'Platform smoke test passed.'
