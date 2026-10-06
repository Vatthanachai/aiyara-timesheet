param(
    [string]$EnvFile = '.env',
    [string]$ProjectName = '',
    [string]$GatewayUrl = 'http://localhost:8081'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $EnvFile)) { throw "Missing environment file: $EnvFile" }
$compose = @('--env-file', $EnvFile)
if ($ProjectName) { $compose += @('-p', $ProjectName) }

& docker compose @compose config --quiet
if ($LASTEXITCODE -ne 0) { throw 'Compose configuration is invalid.' }
$postgresUser = (& docker compose @compose config --format json | ConvertFrom-Json).services.postgres.environment.POSTGRES_USER
if ($LASTEXITCODE -ne 0 -or -not $postgresUser) { throw 'PostgreSQL user is not configured.' }

foreach ($service in @('identities-api', 'timesheet-api', 'reports-api', 'notifications-api', 'gateway-api')) {
    $id = & docker compose @compose ps -q $service
    if ($LASTEXITCODE -ne 0 -or -not $id) { throw "$service is not running." }
    $state = & docker inspect --format '{{.State.Health.Status}}' $id
    if ($state -ne 'healthy') { throw "$service is $state." }
}

$suffix = [Guid]::NewGuid().ToString('N')[0..7] -join ''
$firstBody = @{name='Phase 1 First'; slug="phase1-first-$suffix"; adminEmail="first-$suffix@example.test"} | ConvertTo-Json
$secondBody = @{name='Phase 1 Second'; slug="phase1-second-$suffix"; adminEmail="second-$suffix@example.test"} | ConvertTo-Json
$first = Invoke-RestMethod -Uri "$GatewayUrl/api/v1/tenants" -Method Post -ContentType 'application/json' -Body $firstBody
$second = Invoke-RestMethod -Uri "$GatewayUrl/api/v1/tenants" -Method Post -ContentType 'application/json' -Body $secondBody
if ($first.tenantId -eq $second.tenantId -or $first.status -ne 'PendingActivation') {
    throw 'Tenant creation did not produce isolated pending memberships.'
}

$duplicate = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/tenants" -Method Post -ContentType 'application/json' -Body $firstBody -SkipHttpErrorCheck
if ($duplicate.StatusCode -ne 409) { throw "Duplicate slug returned $($duplicate.StatusCode), expected 409." }

$inviteBody = @{email="invite-$suffix@example.test";role='Employee'} | ConvertTo-Json
$inviteUrl = "$GatewayUrl/api/v1/tenants/$($first.tenantId)/invitations"
$missingKey = Invoke-WebRequest -Uri $inviteUrl -Method Post -ContentType 'application/json' -Body $inviteBody -SkipHttpErrorCheck
if ($missingKey.StatusCode -ne 401) { throw "Missing key returned $($missingKey.StatusCode), expected 401." }
$wrongTenant = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/tenants/$($second.tenantId)/invitations" -Method Post -ContentType 'application/json' -Body $inviteBody -Headers @{'X-Onboarding-Key'=$first.onboardingKey} -SkipHttpErrorCheck
if ($wrongTenant.StatusCode -ne 401) { throw "Cross-tenant key returned $($wrongTenant.StatusCode), expected 401." }
$issued = Invoke-RestMethod -Uri $inviteUrl -Method Post -ContentType 'application/json' -Body $inviteBody -Headers @{'X-Onboarding-Key'=$first.onboardingKey;'X-Tenant-Id'=$second.tenantId}
if ($issued.tenantId -ne $first.tenantId -or -not $issued.code) { throw 'Invitation issuance returned the wrong tenant or no code.' }
$grants = & docker compose @compose exec -T postgres psql -U $postgresUser -d postgres -tAc "select has_database_privilege('identity_app','identity_db','CONNECT'), has_database_privilege('identity_app','timesheet_db','CONNECT'), has_database_privilege('timesheet_app','identity_db','CONNECT')"
if ($LASTEXITCODE -ne 0 -or $grants.Trim() -ne 't|f|f') { throw "Service database isolation failed: $grants" }

$acceptBody = @{code=$issued.code} | ConvertTo-Json
$accepted = Invoke-RestMethod -Uri "$GatewayUrl/api/v1/invitations/accept" -Method Post -ContentType 'application/json' -Body $acceptBody -Headers @{'X-Tenant-Id'=$second.tenantId}
if ($accepted.tenantId -ne $first.tenantId -or $accepted.status -ne 'PendingActivation') {
    throw 'Invitation acceptance returned the wrong tenant or status.'
}
$replayed = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/invitations/accept" -Method Post -ContentType 'application/json' -Body $acceptBody -SkipHttpErrorCheck
if ($replayed.StatusCode -ne 400) { throw "Replayed invitation returned $($replayed.StatusCode), expected 400." }

foreach ($path in @('/health', '/openapi/v1.json', '/api-docs/identity/openapi/v1.json',
    '/api-docs/timesheet/openapi/v1.json', '/api-docs/reporting/openapi/v1.json',
    '/api-docs/notification/openapi/v1.json')) {
    $result = Invoke-WebRequest -Uri "$GatewayUrl$path" -SkipHttpErrorCheck
    if ($result.StatusCode -ne 200) { throw "$path returned $($result.StatusCode)." }
}
$openApi = Invoke-RestMethod -Uri "$GatewayUrl/openapi/v1.json"
if (-not $openApi.paths.PSObject.Properties['/api/v1/tenants/{tenantId}/invitations']) {
    throw 'Gateway OpenAPI is missing the invitation issuance route.'
}
$protected = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" -Headers @{'X-Tenant-Id'=$first.tenantId} -SkipHttpErrorCheck
if ($protected.StatusCode -ne 401) { throw "Protected route returned $($protected.StatusCode), expected 401." }
$invalidToken = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" -Headers @{Authorization='Bearer invalid';'X-Tenant-Id'=$first.tenantId} -SkipHttpErrorCheck
if ($invalidToken.StatusCode -ne 401) { throw "Invalid token returned $($invalidToken.StatusCode), expected 401." }

Write-Host 'Phase 1 Compose smoke test passed.'
