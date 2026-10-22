param(
    [string]$GatewayUrl = $(if ($env:TIMESHEET_GATEWAY_URL) { $env:TIMESHEET_GATEWAY_URL } else { 'http://127.0.0.1:8081' }),
    [string]$MailDevUrl = $(if ($env:TIMESHEET_MAILDEV_URL) { $env:TIMESHEET_MAILDEV_URL } else { 'http://127.0.0.1:8080' }),
    [string]$MailDevToken = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' 'MailInbox.ps1')
$MailDevToken = Resolve-MailDevToken $MailDevToken '.env'

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
    if ($response.Content) {
        $payload = $response.Content | ConvertFrom-Json
        if ($status -ge 200 -and $status -lt 300) { return $payload.result }
    }
}

function ActivationCode([string]$email) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $message = Find-MailMessage $MailDevUrl $MailDevToken $email 'Activate your Aiyara Timesheet account'
        if ($message) {
            $match = [regex]::Match($message.Text, '(?mi)code:\s*(\S+)')
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
    organizationCode = $tenant.organizationCode; email = $adminEmail; password = $password
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
$workWeek = Expect (Request Put '/api/v1/timesheets/work-week' @{
    workingDays = @(1, 2, 3, 4, 5, 6); startTime = '08:30:00'; endTime = '17:30:00'
    breakStart = '11:30:00'; breakEnd = '12:30:00'
} $admin) 200 'work week'
if (($workWeek.workingDays -join ',') -ne '1,2,3,4,5,6') { throw 'Work week did not persist.' }
if ($workWeek.workHours.startTime -ne '08:30' -or $workWeek.workHours.endTime -ne '17:30') { throw 'Working hours did not persist.' }
if ($workWeek.workHours.breakStart -ne '11:30' -or $workWeek.workHours.breakEnd -ne '12:30') { throw 'Break did not persist.' }
Expect (Request Put '/api/v1/timesheets/work-week' @{
    workingDays = @(1, 2, 3, 4, 5, 6); startTime = '18:00:00'; endTime = '09:00:00'
} $admin) 400 'working day ending before it starts' | Out-Null
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
    organizationCode = $tenant.organizationCode; email = $employeeEmail; password = $password
}) 200 'employee login'
$employee = @{ Authorization = "Bearer $($employeeSession.accessToken)" }
Expect (Request Post '/api/v1/timesheets/projects' @{ name = 'Denied' } $employee) 403 'employee catalog permission' | Out-Null
Expect (Request Put '/api/v1/timesheets/work-week' @{ workingDays = @(1) } $employee) 403 'employee work week permission' | Out-Null
$catalog = Expect (Request Get '/api/v1/timesheets/catalog' $null $employee) 200 'employee catalog'
if (($catalog.workingDays -join ',') -ne '1,2,3,4,5,6') { throw 'Catalog did not return the work week.' }
if ($catalog.workHours.startTime -ne '08:30') { throw 'Catalog did not return the working hours.' }
Expect (Request Get "/api/v1/identity/tenants/$($tenant.tenantId)/password-policy" $null $employee) 403 'employee password policy permission' | Out-Null

# A lunch-hour entry that was not worked through counts for nothing, so it is refused (and not stored).
$lunchOnly = Request Post '/api/v1/timesheets/entries' @{ date = $date; startTime = '11:30:00'; endTime = '12:30:00'
    taskName = 'Lunch'; detail = $null; notes = $null; projectId = $null; categoryId = $null; personalTaskId = $null
    reason = $null; includesBreak = $false } $employee
if ($lunchOnly.StatusCode -ne 400 -or $lunchOnly.Content -notmatch 'ENTRY_IN_BREAK') { throw 'A lunch-only entry was not refused.' }

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

# Billable work and budgets: the project gets a client, rate, and budget; the 4-hour entry is billed at the
# rate and goes over a 3-hour budget; rates stay hidden from members and the reports are for admins.
$billing = @{ client = 'Acme'; isBillable = $true; hourlyRate = 100; currencyCode = 'THB'; budgetHours = 3 }
Expect (Request Put "/api/v1/timesheets/projects/$($project.id)/billing" $billing $employee) 403 'employee billing permission' | Out-Null
Expect (Request Put "/api/v1/timesheets/projects/$($project.id)/billing" @{ client = 'Acme'; isBillable = $true; hourlyRate = -1; currencyCode = 'THB'; budgetHours = 3 } $admin) 400 'negative rate' | Out-Null
Expect (Request Put "/api/v1/timesheets/projects/$($project.id)/billing" $billing $admin) 200 'project billing' | Out-Null
$usage = Expect (Request Get "/api/v1/timesheets/projects/$($project.id)/usage?from=$year-$month-01&to=$year-$month-28" $null $admin) 200 'project usage'
if ($usage.project.usedMinutes -ne 240 -or $usage.project.billableAmount -ne 400 -or -not $usage.project.overBudget -or $usage.project.remainingHours -ne -1) { throw 'Project usage is incorrect.' }
$projectsUsage = Expect (Request Get '/api/v1/timesheets/projects/usage' $null $admin) 200 'projects usage'
if (@($projectsUsage.projects).Count -ne 1 -or $projectsUsage.totals.amounts[0].currencyCode -ne 'THB') { throw 'Usage by project is incorrect.' }
$clientsUsage = Expect (Request Get '/api/v1/timesheets/clients/usage' $null $admin) 200 'clients usage'
if (@($clientsUsage.clients).Count -ne 1 -or $clientsUsage.clients[0].client -ne 'Acme' -or $clientsUsage.clients[0].amounts[0].amount -ne 400) { throw 'Usage by client is incorrect.' }
Expect (Request Get '/api/v1/timesheets/projects/usage' $null $employee) 403 'employee usage permission' | Out-Null
$memberCatalog = Expect (Request Get '/api/v1/timesheets/catalog' $null $employee) 200 'employee catalog with billing'
if ($null -ne $memberCatalog.projects[0].hourlyRate -or $memberCatalog.projects[0].client -ne 'Acme') { throw 'The catalog shows a member the rate or hides the client.' }
$leave = Expect (Request Post '/api/v1/timesheets/leave' @{
    date = $date; kind = 'annual'; notes = 'Half day'
} $employee) 201 'leave entry'
Expect (Request Get "/api/v1/timesheets/leave?year=$year&month=$month" $null $employee) 200 'leave read' | Out-Null
Expect (Request Delete "/api/v1/timesheets/leave/$($leave.id)" $null $admin) 404 'leave owner isolation' | Out-Null
$myMonth = Expect (Request Get "/api/v1/timesheets/me/timesheet?year=$year&month=$month" $null $employee) 200 'own open month'
if ($myMonth.locked -or @($myMonth.entries).Count -ne 1 -or @($myMonth.leaves).Count -ne 1) { throw 'Own month is not read live.' }
$adminMonth = Expect (Request Get "/api/v1/timesheets/me/timesheet?year=$year&month=$month" $null $admin) 200 'admin own month'
if (@($adminMonth.entries).Count -ne 0) { throw 'Own month shows another member''s entries.' }

# Leave types: once the organization has them, leave names one, may cover part of the day, and
# counts against the type's days for the year (half of the 8-hour working day is 0.5).
$annual = Expect (Request Post '/api/v1/timesheets/leave-types' @{ name = 'Annual'; annualDays = 10 } $admin) 201 'leave type'
Expect (Request Post '/api/v1/timesheets/leave-types' @{ name = 'Denied'; annualDays = 1 } $employee) 403 'employee leave type permission' | Out-Null
Expect (Request Post '/api/v1/timesheets/leave' @{ date = $date; kind = 'annual' } $employee) 400 'leave without a type' | Out-Null
Expect (Request Post '/api/v1/timesheets/leave' @{ date = $date; leaveTypeId = $annual.id; minutes = 240 } $employee) 201 'half-day leave' | Out-Null
$balance = Expect (Request Get "/api/v1/timesheets/me/leave-balance?year=$year" $null $employee) 200 'leave balance'
$annualBalance = @($balance.balances) | Where-Object leaveTypeId -eq $annual.id
if ($annualBalance.usedDays -ne 0.5 -or $annualBalance.remainingDays -ne 9.5) { throw 'The leave balance does not count the half day.' }

# Copy and bulk entry, in the admin's own month: a saved template fills one entry written on two working
# days at once, that week is copied a week later (copying again skips the days that now have entries),
# and anything reaching into last month is refused whole.
$template = Expect (Request Post '/api/v1/timesheets/entry-templates' @{ name = 'Standup'; taskName = 'Daily standup'
    startTime = '09:00:00'; endTime = '09:30:00'; projectId = $project.id; categoryId = $category.id
    detail = 'Team'; notes = $null; includesBreak = $false } $admin) 201 'entry template'
Expect (Request Post '/api/v1/timesheets/entry-templates' @{ name = 'standup'; taskName = 'Other'
    startTime = '10:00:00'; endTime = '11:00:00' } $admin) 409 'duplicate entry template' | Out-Null
if (@(Expect (Request Get '/api/v1/timesheets/entry-templates' $null $admin) 200 'entry templates').Count -ne 1) { throw 'The template was not listed.' }
if (@(Expect (Request Get '/api/v1/timesheets/entry-templates' $null $employee) 200 'employee entry templates').Count -ne 0) { throw 'Another member can read the template.' }
# The first two of the tenant's working days in the 8th–14th (the holiday is the 7th); a week later
# falls on the same weekdays, so the copy writes both whatever the month.
$workingDays = @((Expect (Request Get '/api/v1/timesheets/catalog' $null $admin) 200 'admin catalog').workingDays)
$bulkDays = @(8..14 | ForEach-Object { [datetime]::new([int]$year, [int]$month, $_) } |
    Where-Object { $workingDays -contains [int]$_.DayOfWeek } | Select-Object -First 2 | ForEach-Object { $_.ToString('yyyy-MM-dd') })
if ($bulkDays.Count -ne 2) { throw 'The work week has fewer than two working days for the bulk step.' }
$bulk = Expect (Request Post '/api/v1/timesheets/entries/bulk' @{ dates = $bulkDays; startTime = $template.startTime
    endTime = $template.endTime; taskName = $template.taskName; detail = $template.detail; notes = $null
    projectId = $template.projectId; categoryId = $template.categoryId; personalTaskId = $null; reason = $null
    includesBreak = $template.includesBreak } $admin) 200 'bulk entry'
if (@($bulk.created).Count -ne 2 -or @($bulk.created)[0].durationMinutes -ne 30) { throw 'Bulk entry did not write both days.' }
$weekCopy = @{ from = "$year-$month-08"; to = "$year-$month-15"; days = 7 }
$copied = Expect (Request Post '/api/v1/timesheets/entries/copy' $weekCopy $admin) 200 'week copy'
if (@($copied.created).Count -ne 2 -or @($copied.skipped).Count -ne 0) { throw 'The week was not copied.' }
$again = Expect (Request Post '/api/v1/timesheets/entries/copy' $weekCopy $admin) 200 'repeated week copy'
if (@($again.created).Count -ne 0 -or @(@($again.skipped) | Where-Object reason -eq 'has-entries').Count -ne 2) { throw 'Copying again did not skip the copied days.' }
$lastMonth = [datetime]::new([int]$year, [int]$month, 1).AddDays(-1).ToString('yyyy-MM-dd')
$closed = Request Post '/api/v1/timesheets/entries/bulk' @{ dates = @($bulkDays[0], $lastMonth); startTime = '09:00:00'
    endTime = '10:00:00'; taskName = 'Late'; includesBreak = $false } $admin
if ($closed.StatusCode -ne 409 -or ($closed.Content | ConvertFrom-Json).errors.dates -notcontains $lastMonth) { throw 'Bulk entry into last month was not refused by date.' }
Expect (Request Post '/api/v1/timesheets/entries/copy' @{ from = "$year-$month-08"; to = $lastMonth; days = 1 } $admin) 409 'copy into last month' | Out-Null
Expect (Request Delete "/api/v1/timesheets/entry-templates/$($template.id)" $null $employee) 404 'template owner isolation' | Out-Null
Expect (Request Delete "/api/v1/timesheets/entry-templates/$($template.id)" $null $admin) 204 'template deletion' | Out-Null

# Running timer: one per member, kept on the server; a stop that is refused keeps it, and stopping
# writes the entry.
Expect (Request Post '/api/v1/timesheets/timer/start' @{ taskName = 'Timer task' } $admin) 201 'timer start' | Out-Null
Expect (Request Post '/api/v1/timesheets/timer/start' @{} $admin) 409 'second timer start' | Out-Null
if (-not (Expect (Request Get '/api/v1/timesheets/timer' $null $admin) 200 'running timer').timer) { throw 'The running timer was not kept.' }
if ((Expect (Request Get '/api/v1/timesheets/timer' $null $employee) 200 'employee timer').timer) { throw 'Another member can see the timer.' }
Expect (Request Post '/api/v1/timesheets/timer/stop' @{ startTime = '06:00:00'; endTime = '06:00:00' } $admin) 400 'timer stop with no time' | Out-Null
if (-not (Expect (Request Get '/api/v1/timesheets/timer' $null $admin) 200 'timer after refused stop').timer) { throw 'A refused stop lost the timer.' }
Expect (Request Post '/api/v1/timesheets/timer/stop' @{ date = "$year-$month-15"; startTime = '06:00:00'; endTime = '06:30:00' } $employee) 404 'timer owner isolation' | Out-Null
$timed = Expect (Request Post '/api/v1/timesheets/timer/stop' @{ date = "$year-$month-15"; startTime = '06:00:00'; endTime = '06:30:00' } $admin) 201 'timer stop'
if ($timed.taskName -ne 'Timer task' -or $timed.durationMinutes -ne 30) { throw 'Stopping the timer did not write the entry.' }
if ((Expect (Request Get '/api/v1/timesheets/timer' $null $admin) 200 'timer after stop').timer) { throw 'The timer still runs after it was stopped.' }
Expect (Request Post '/api/v1/timesheets/timer/start' @{} $admin) 201 'timer restart' | Out-Null
Expect (Request Delete '/api/v1/timesheets/timer' $null $admin) 204 'timer discard' | Out-Null
Expect (Request Delete '/api/v1/timesheets/timer' $null $admin) 404 'discard without a timer' | Out-Null

# Reminders: off until a Tenant Admin turns them on; only a Tenant Admin may change them.
$reminders = Expect (Request Get '/api/v1/timesheets/reminders' $null $admin) 200 'reminder settings'
if ($reminders.dailyEnabled -or $reminders.monthEndEnabled -or $reminders.sendAt -ne '09:00') { throw 'Reminders are not off by default.' }
$reminders = Expect (Request Put '/api/v1/timesheets/reminders' @{ dailyEnabled = $true; monthEndEnabled = $true; sendAt = '07:30:00' } $admin) 200 'save reminders'
if (-not $reminders.dailyEnabled -or $reminders.sendAt -ne '07:30') { throw 'Reminder settings were not saved.' }
Expect (Request Put '/api/v1/timesheets/reminders' @{ dailyEnabled = $false; monthEndEnabled = $false; sendAt = '07:30:00' } $employee) 403 'employee reminder permission' | Out-Null

# Approval: the employee submits, the admin returns it with a reason (emailed), the employee fixes
# it and submits again, and the admin approves it.
$employeeId = $myMonth.ownerId
$approvalPath = "/api/v1/timesheets/members/$employeeId/months/$year/$month"
Expect (Request Post "/api/v1/timesheets/me/months/$year/$month/submit" $null $employee) 200 'month submission' | Out-Null
Expect (Request Put "/api/v1/timesheets/entries/$($entry.id)" $entryBody $employee) 409 'submitted entry' | Out-Null
Expect (Request Post "$approvalPath/approve" $null $employee) 403 'approval permission' | Out-Null
Expect (Request Post "$approvalPath/return" @{ reason = 'Add the client call on the 7th.' } $admin) 200 'month return' | Out-Null
$returned = Expect (Request Get "/api/v1/timesheets/me/months/$year/$month" $null $employee) 200 'returned month'
if ($returned.status -ne 'returned' -or -not $returned.editable -or $returned.returnReason -ne 'Add the client call on the 7th.') {
    throw 'The returned month is not open to its member with the reason.'
}
$returnEmail = $null
for ($attempt = 0; $attempt -lt 30 -and -not $returnEmail; $attempt++) {
    $returnEmail = Find-MailMessage $MailDevUrl $MailDevToken $employeeEmail 'Your timesheet for * was returned'
    if (-not $returnEmail) { Start-Sleep -Milliseconds 500 }
}
if (-not $returnEmail -or $returnEmail.Text -notmatch 'Add the client call on the 7th\.') { throw 'The month-returned email was not delivered.' }
Expect (Request Put "/api/v1/timesheets/entries/$($entry.id)" $entryBody $employee) 200 'returned entry update' | Out-Null
Expect (Request Post "/api/v1/timesheets/me/months/$year/$month/submit" $null $employee) 200 'month resubmission' | Out-Null
Expect (Request Post "$approvalPath/approve" $null $admin) 200 'month approval' | Out-Null
$summary = Expect (Request Get "/api/v1/timesheets/members/summary?year=$year&month=$month" $null $admin) 200 'approved summary'
if ((@($summary.members) | Where-Object ownerId -eq $employeeId).status -ne 'approved') { throw 'The summary does not show the approval.' }
Expect (Request Get "/api/v1/timesheets/months/$year/$month/snapshot" $null $employee) 200 'approval snapshot' | Out-Null

Expect (Request Post "/api/v1/timesheets/months/$year/$month/lock" $null $employee) 403 'month lock permission' | Out-Null
Expect (Request Post "/api/v1/timesheets/months/$year/$month/lock" $null $admin) 200 'month lock' | Out-Null
Expect (Request Get "/api/v1/timesheets/months/$year/$month/snapshot" $null $employee) 200 'immutable snapshot' | Out-Null
Expect (Request Put "/api/v1/timesheets/entries/$($entry.id)" $entryBody $employee) 409 'locked entry' | Out-Null
Expect (Request Delete "/api/v1/timesheets/leave/$($leave.id)" $null $employee) 409 'locked leave' | Out-Null

Write-Output 'Phase 3 profile, catalog, task, overnight time entry, templates, bulk and copied entries, running timer, leave, isolation, approval, month lock and snapshot smoke test passed.'
