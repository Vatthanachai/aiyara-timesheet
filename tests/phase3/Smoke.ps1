param(
    [string]$GatewayUrl = 'http://127.0.0.1:8081',
    [string]$MailDevUrl = 'http://127.0.0.1:8080'
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
        throw "$step returned $($response.StatusCode), expected $status."
    }
    if ($response.Content) { return $response.Content | ConvertFrom-Json }
}

function ActivationCode([string]$email) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $messages = Invoke-RestMethod -Uri "$MailDevUrl/email"
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
$adminEmail = "phase3-admin-$suffix@example.test"
$employeeEmail = "phase3-employee-$suffix@example.test"
$password = 'Correct-Password-123!'
$tenant = Expect (Request Post '/api/v1/tenants' @{
    name = 'Phase 3 E2E'; slug = "phase3-e2e-$suffix"; adminEmail = $adminEmail
}) 201 'tenant creation'
Expect (Request Post '/api/v1/auth/activate' @{
    code = (ActivationCode $adminEmail); password = $password
}) 204 'admin activation' | Out-Null
$adminSession = Expect (Request Post '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $adminEmail; password = $password
}) 200 'admin login'
$admin = @{ Authorization = "Bearer $($adminSession.accessToken)" }

Expect (Request Get '/api/v1/timesheets/context') 401 'unauthorized timesheet' | Out-Null
$context = Expect (Request Get '/api/v1/timesheets/context' $null $admin) 200 'timesheet context'
$year, $month = $context.currentMonth.Split('-')
$date = "$year-$month-07"
$profile = Expect (Request Put '/api/v1/identity/profile/me' @{
    firstName = 'Phase'; lastName = 'Admin'; photoUrl = $null; jobTitle = 'Manager'
} $admin) 200 'profile update'
if ($profile.jobTitle -ne 'Manager') { throw 'Profile update did not persist.' }
$profile = Expect (Request Get '/api/v1/identity/profile/me' $null $admin) 200 'profile read'
if ($profile.firstName -ne 'Phase') { throw 'Profile read is stale.' }
Expect (Request Get "/api/v1/identity/tenants/$($tenant.tenantId)/password-policy" $null $admin) 200 'password policy read' | Out-Null

$project = Expect (Request Post '/api/v1/timesheets/projects' @{ name = 'Customer rollout' } $admin) 201 'project'
$category = Expect (Request Post '/api/v1/timesheets/categories' @{ name = 'Delivery' } $admin) 201 'category'
Expect (Request Post '/api/v1/timesheets/holidays' @{ date = $date; name = 'Team holiday' } $admin) 201 'holiday' | Out-Null
$task = Expect (Request Post '/api/v1/timesheets/tasks' @{
    name = 'Prepare rollout'; status = 'todo'; projectId = $project.id; categoryId = $category.id
} $admin) 201 'personal task'
$task = Expect (Request Put "/api/v1/timesheets/tasks/$($task.id)" @{
    name = 'Prepare rollout'; status = 'doing'; projectId = $project.id; categoryId = $category.id
} $admin) 200 'task status update'
if ($task.status -ne 'doing') { throw 'Task status did not persist.' }

$invitation = Expect (Request Post "/api/v1/auth/tenants/$($tenant.tenantId)/invitations" @{
    email = $employeeEmail; role = 'Employee'
} $admin) 201 'employee invitation'
Expect (Request Post '/api/v1/invitations/accept' @{ code = $invitation.code }) 200 'invitation acceptance' | Out-Null
Expect (Request Post '/api/v1/auth/activate' @{
    code = (ActivationCode $employeeEmail); password = $password
}) 204 'employee activation' | Out-Null
$employeeSession = Expect (Request Post '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $employeeEmail; password = $password
}) 200 'employee login'
$employee = @{ Authorization = "Bearer $($employeeSession.accessToken)" }
Expect (Request Post '/api/v1/timesheets/projects' @{ name = 'Denied' } $employee) 403 'employee catalog permission' | Out-Null
Expect (Request Get "/api/v1/identity/tenants/$($tenant.tenantId)/password-policy" $null $employee) 403 'employee password policy permission' | Out-Null

$entryBody = @{ date = $date; startTime = '22:00:00'; endTime = '02:00:00';
    taskName = 'Overnight rollout'; detail = 'Deployment'; notes = 'Phase 3';
    projectId = $project.id; categoryId = $category.id; personalTaskId = $null; reason = $null }
$entry = Expect (Request Post '/api/v1/timesheets/entries' $entryBody $employee) 201 'overnight entry'
if ($entry.durationMinutes -ne 240) { throw 'Overnight duration is incorrect.' }
$entries = Expect (Request Get "/api/v1/timesheets/entries?year=$year&month=$month" $null $employee) 200 'employee entries'
if (@($entries).Count -ne 1) { throw 'Employee entry was not returned.' }
$adminEntries = Expect (Request Get "/api/v1/timesheets/entries?year=$year&month=$month" $null $admin) 200 'owner isolation'
if (@($adminEntries).Count -ne 0) { throw 'Another user can read employee entries.' }
Expect (Request Put "/api/v1/timesheets/entries/$($entry.id)" $entryBody $admin) 404 'owner update isolation' | Out-Null
$entryBody.notes = 'Revised'
Expect (Request Put "/api/v1/timesheets/entries/$($entry.id)" $entryBody $employee) 200 'inline entry update' | Out-Null
$leave = Expect (Request Post '/api/v1/timesheets/leave' @{
    date = $date; kind = 'annual'; notes = 'Half day'
} $employee) 201 'leave entry'
Expect (Request Get "/api/v1/timesheets/leave?year=$year&month=$month" $null $employee) 200 'leave read' | Out-Null
Expect (Request Delete "/api/v1/timesheets/leave/$($leave.id)" $null $admin) 404 'leave owner isolation' | Out-Null

Expect (Request Post "/api/v1/timesheets/months/$year/$month/lock" $null $employee) 403 'month lock permission' | Out-Null
Expect (Request Post "/api/v1/timesheets/months/$year/$month/lock" $null $admin) 200 'month lock' | Out-Null
Expect (Request Get "/api/v1/timesheets/months/$year/$month/snapshot" $null $employee) 200 'immutable snapshot' | Out-Null
Expect (Request Put "/api/v1/timesheets/entries/$($entry.id)" $entryBody $employee) 409 'locked entry' | Out-Null
Expect (Request Delete "/api/v1/timesheets/leave/$($leave.id)" $null $employee) 409 'locked leave' | Out-Null

Write-Output 'Phase 3 profile, catalog, task, overnight time entry, leave, isolation, month lock and snapshot smoke test passed.'
