param(
    [string]$GatewayUrl = 'http://127.0.0.1:8081',
    [string]$MailDevUrl = 'http://127.0.0.1:8080',
    [string]$EnvFile = '.env',
    [string]$ProjectName = ''
)

$ErrorActionPreference = 'Stop'

function Request([string]$method, [string]$path, $body = $null, $headers = @{}) {
    $arguments = @{ Uri = "$GatewayUrl$path"; Method = $method;
        Headers = $headers; SkipHttpErrorCheck = $true }
    if ($null -ne $body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $body | ConvertTo-Json -Depth 10
    }
    Invoke-WebRequest @arguments
}

function Expect($response, [int]$status, [string]$step) {
    if ($response.StatusCode -ne $status) {
        throw "$step returned $($response.StatusCode), expected $status. Response: $($response.Content)"
    }
    if ($response.Content) { return $response.Content | ConvertFrom-Json }
}

function ActivationCode([string]$email) {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $mailResponse = Invoke-WebRequest -Uri "$MailDevUrl/email" -UseBasicParsing
        $messages = @($mailResponse.Content | ConvertFrom-Json)
        $message = $messages | Where-Object {
            $_.subject -eq 'Activate your Aiyara Timesheet account' -and
            $_.to[0].address -eq $email
        } | Select-Object -Last 1
        if ($message) {
            $match = [regex]::Match($message.text, ':\s*([^\s]+)\s*$')
            if ($match.Success) { return $match.Groups[1].Value }
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Activation email for $email was not delivered."
}

$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$adminEmail = "phase4-admin-$suffix@example.test"
$password = 'Phase4-Smoke-Password-2026!'
$tenant = Expect (Request Post '/api/v1/tenants' @{
    name = 'Phase 4 E2E'; slug = "phase4-e2e-$suffix"; adminEmail = $adminEmail
}) 201 'tenant creation'
Expect (Request Post '/api/v1/auth/activate' @{
    code = (ActivationCode $adminEmail); password = $password
}) 204 'admin activation' | Out-Null
$session = Expect (Request Post '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $adminEmail; password = $password
}) 200 'admin login'
$auth = @{ Authorization = "Bearer $($session.accessToken)" }
$profile = Expect (Request Put '/api/v1/identity/profile/me' @{
    firstName = 'Phase'; lastName = 'Four'; photoUrl = $null; jobTitle = 'E2E Admin'
} $auth) 200 'profile setup'
$context = Expect (Request Get '/api/v1/timesheets/context' $null $auth) 200 'timesheet context'
$year, $month = $context.currentMonth.Split('-')
$date = "$year-$month-07"
$project = Expect (Request Post '/api/v1/timesheets/projects' @{ name = 'Phase 4 E2E project' } $auth) 201 'project'
$category = Expect (Request Post '/api/v1/timesheets/categories' @{ name = 'Reporting E2E' } $auth) 201 'category'
Expect (Request Post '/api/v1/timesheets/entries' @{
    date = $date; startTime = '09:00:00'; endTime = '17:30:00'; taskName = 'E2E report source'
    detail = 'Source data for a live report run'; notes = 'Phase 4 live smoke'
    projectId = $project.id; categoryId = $category.id; personalTaskId = $null; reason = $null
} $auth) 201 'timesheet entry' | Out-Null
Expect (Request Post "/api/v1/timesheets/months/$year/$month/lock" $null $auth) 200 'month lock' | Out-Null

$definition = Expect (Request Post '/api/v1/reports/definitions' @{
    kind = 'Monthly'; format = 'Pdf'; name = 'Phase 4 live E2E'
} $auth) 201 'report definition'
$startLocal = [DateTimeOffset]::new([int]$year, [int]$month, 1, 0, 0, 0, [TimeSpan]::FromHours(7))
$nextLocal = $startLocal.AddMonths(1)
$run = Expect (Request Post '/api/v1/reports/runs' @{
    definitionId = $definition.id
    periodStartUtc = $startLocal.ToUniversalTime().ToString('o')
    periodEndUtc = $nextLocal.ToUniversalTime().ToString('o')
    subjectUserId = $profile.id
} $auth) 202 'report request'

$completed = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    $runs = @(Expect (Request Get '/api/v1/reports/runs' $null $auth) 200 'report status')
    $current = $runs | Where-Object { $_.id -eq $run.id } | Select-Object -First 1
    if ($current.status -eq 'Succeeded') { $completed = $true; break }
    if ($current.status -eq 'Failed') { throw "Report worker failed: $($current.attemptCount) attempt(s)." }
    Start-Sleep -Seconds 1
}
if (-not $completed) { throw 'Report worker did not finish within 60 seconds.' }

Add-Type -AssemblyName System.Net.Http
$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $session.accessToken)
$generatedPdf = $client.GetByteArrayAsync("$GatewayUrl/api/v1/reports/runs/$($run.id)/download").GetAwaiter().GetResult()
if ([System.Text.Encoding]::ASCII.GetString($generatedPdf, 0, 5) -ne '%PDF-') {
    throw 'Generated report download is not a PDF.'
}

$signedPdf = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.7`nPhase 4 signed E2E document`n%%EOF")
$upload = [System.Net.Http.MultipartFormDataContent]::new()
$fileContent = [System.Net.Http.ByteArrayContent]::new($signedPdf)
$fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/pdf')
$upload.Add($fileContent, 'file', 'phase4-signed.pdf')
$uploadResponse = $client.PostAsync("$GatewayUrl/api/v1/reports/runs/$($run.id)/signed-document", $upload).GetAwaiter().GetResult()
if ([int]$uploadResponse.StatusCode -ne 201) {
    throw "Signed PDF upload returned $([int]$uploadResponse.StatusCode)."
}
$signedDocument = $uploadResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
if ($signedDocument.version -ne 1) { throw 'The signed PDF version was not recorded as version 1.' }
$downloadSigned = $client.GetByteArrayAsync("$GatewayUrl/api/v1/reports/runs/$($run.id)/download").GetAwaiter().GetResult()
if ([Convert]::ToBase64String($downloadSigned) -ne [Convert]::ToBase64String($signedPdf)) {
    throw 'Latest report download did not return the signed PDF.'
}

$readyEmail = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    $mailResponse = Invoke-WebRequest -Uri "$MailDevUrl/email" -UseBasicParsing
    $messages = @($mailResponse.Content | ConvertFrom-Json)
    if ($messages | Where-Object {
        $_.subject -like '*report ready*' -and $_.to[0].address -eq $adminEmail
    }) { $readyEmail = $true; break }
    Start-Sleep -Milliseconds 500
}
if (-not $readyEmail) { throw 'Report-ready email was not received by the test account.' }

$compose = @('--env-file', $EnvFile)
if ($ProjectName) { $compose += @('-p', $ProjectName) }
$postgresUser = (& docker compose @compose config --format json | ConvertFrom-Json).services.postgres.environment.POSTGRES_USER
if ($LASTEXITCODE -ne 0 -or -not $postgresUser) { throw 'PostgreSQL user is not configured.' }
$deliveryPersisted = $false
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    $deliveryCount = & docker compose @compose exec -T postgres psql -U $postgresUser -d notification_db -tAc `
        "select count(*) from notification_deliveries where `"RecipientEmail`" = '$adminEmail' and `"Template`" = 'report-ready' and `"Outcome`" = 'sent'"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Notification delivery outcomes.' }
    if ([int]$deliveryCount -gt 0) { $deliveryPersisted = $true; break }
    Start-Sleep -Milliseconds 500
}
if (-not $deliveryPersisted) { throw 'Notification did not persist the report-ready delivery outcome.' }

$upload.Dispose()
$client.Dispose()
Write-Output "Phase 4 live E2E passed: tenant=$($tenant.tenantId) run=$($run.id) generatedPdfBytes=$($generatedPdf.Length) signedVersion=$($signedDocument.version) reportReadyEmail=received deliveryPersisted=yes"
