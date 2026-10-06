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

$code = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($code)))
$invitationId = [Guid]::NewGuid()
$sql = @"
insert into invitations ("Id", "TenantId", "Email", "TokenHash", "Role", "InvitedByAccountId", "CreatedAtUtc", "ExpiresAtUtc")
values ('$invitationId', '$($first.tenantId)', 'invite-$suffix@example.test', '$hash', 'Employee', '$($first.accountId)', now(), now() + interval '1 day')
"@
& docker compose @compose exec -T postgres psql -U $postgresUser -d identity_db -c $sql | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not seed Phase 1 invitation fixture.' }
$grants = & docker compose @compose exec -T postgres psql -U $postgresUser -d postgres -tAc "select has_database_privilege('identity_app','identity_db','CONNECT'), has_database_privilege('identity_app','timesheet_db','CONNECT'), has_database_privilege('timesheet_app','identity_db','CONNECT')"
if ($LASTEXITCODE -ne 0 -or $grants.Trim() -ne 't|f|f') { throw "Service database isolation failed: $grants" }

$acceptBody = @{code=$code} | ConvertTo-Json
$accepted = Invoke-RestMethod -Uri "$GatewayUrl/api/v1/invitations/accept" -Method Post -ContentType 'application/json' -Body $acceptBody
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
$protected = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" -Headers @{'X-Tenant-Id'=$first.tenantId} -SkipHttpErrorCheck
if ($protected.StatusCode -ne 401) { throw "Protected route returned $($protected.StatusCode), expected 401." }

Write-Host 'Phase 1 Compose smoke test passed.'
