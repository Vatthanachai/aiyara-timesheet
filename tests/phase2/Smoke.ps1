param(
    [string]$GatewayUrl = 'http://127.0.0.1:8081',
    [string]$MailDevUrl = 'http://127.0.0.1:1081'
)

$ErrorActionPreference = 'Stop'

function Send-Json($path, $body, $method = 'Post', $headers = @{}) {
    Invoke-WebRequest -Uri ($GatewayUrl + $path) -Method $method -ContentType 'application/json' `
        -Headers $headers -Body ($body | ConvertTo-Json -Depth 5) -SkipHttpErrorCheck
}

function Expect-Status($response, [int]$status, [string]$step) {
    if ($response.StatusCode -ne $status) {
        throw "$step returned $($response.StatusCode), expected $status."
    }
}

function Get-Code([string]$email, [string]$subject) {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $mailResponse = Invoke-RestMethod -Uri "$MailDevUrl/email"
        $messages = @($mailResponse)
        $message = $messages | Where-Object {
            ($_.subject -eq $subject) -and ($_.to[0].address -eq $email)
        } | Select-Object -Last 1
        if ($message) {
            $codeMatch = [regex]::Match($message.text, ':\s*([^\s]+)\s*$')
            if ($codeMatch.Success) { return $codeMatch.Groups[1].Value }
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Expected $subject email was not delivered."
}

$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$adminEmail = "phase2-admin-$suffix@example.test"
$employeeEmail = "phase2-employee-$suffix@example.test"
$oldPassword = 'Correct-Password-123!'
$newPassword = 'New-Correct-Password-456!'

$created = Send-Json '/api/v1/tenants' @{
    name = 'Phase 2 E2E'; slug = "phase2-e2e-$suffix"; adminEmail = $adminEmail
}
Expect-Status $created 201 'tenant creation'
$tenant = $created.Content | ConvertFrom-Json
$code = Get-Code $adminEmail 'Activate your Aiyara Timesheet account'
$activated = Send-Json '/api/v1/auth/activate' @{ code = $code; password = $oldPassword }
Expect-Status $activated 204 'activation'
Expect-Status (Send-Json '/api/v1/auth/activate' @{
    code = $code; password = $oldPassword
}) 401 'single-use activation'

$login = Send-Json '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $adminEmail; password = $oldPassword
}
Expect-Status $login 200 'login'
$session = $login.Content | ConvertFrom-Json
if (-not $session.accessToken.StartsWith('v4.public.')) {
    throw 'Login did not issue a PASETO v4.public token.'
}
$authorization = @{ Authorization = "Bearer $($session.accessToken)" }
$protected = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" `
    -Headers $authorization -SkipHttpErrorCheck
if ($protected.StatusCode -eq 401 -or $protected.StatusCode -eq 403) {
    throw 'Valid access token was rejected by Gateway.'
}

Expect-Status (Send-Json "/api/v1/tenants/$($tenant.tenantId)/invitations" @{
    email = $employeeEmail; role = 'Employee'
} 'Post' @{ 'X-Onboarding-Key' = $tenant.onboardingKey }) 401 'retired onboarding key'
$invitation = Send-Json "/api/v1/auth/tenants/$($tenant.tenantId)/invitations" @{
    email = $employeeEmail; role = 'Employee'
} 'Post' $authorization
Expect-Status $invitation 201 'authenticated invitation'
$invitationData = $invitation.Content | ConvertFrom-Json
$inviteMailCode = Get-Code $employeeEmail 'Your Aiyara Timesheet invitation'
if ($inviteMailCode -ne $invitationData.code) {
    throw 'Invitation email did not match the issued invitation.'
}
Expect-Status (Send-Json '/api/v1/invitations/accept' @{
    code = $inviteMailCode
}) 200 'invitation acceptance'
$employeeCode = Get-Code $employeeEmail 'Activate your Aiyara Timesheet account'
Expect-Status (Send-Json '/api/v1/auth/activate' @{
    code = $employeeCode; password = $oldPassword
}) 204 'invited employee activation'
Expect-Status (Send-Json '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $employeeEmail; password = $oldPassword
}) 200 'invited employee login'

$refreshed = Send-Json '/api/v1/auth/refresh' @{
    refreshToken = $session.refreshToken
}
Expect-Status $refreshed 200 'refresh rotation'
$newSession = $refreshed.Content | ConvertFrom-Json
Expect-Status (Send-Json '/api/v1/auth/refresh' @{
    refreshToken = $session.refreshToken
}) 401 'refresh replay'
Expect-Status (Send-Json '/api/v1/auth/refresh' @{
    refreshToken = $newSession.refreshToken
}) 401 'replay revocation'

$login = Send-Json '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $adminEmail; password = $oldPassword
}
Expect-Status $login 200 'second login'
$session = $login.Content | ConvertFrom-Json
$authorization = @{ Authorization = "Bearer $($session.accessToken)" }
$cached = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" `
    -Headers $authorization -SkipHttpErrorCheck
if ($cached.StatusCode -eq 401) { throw 'Second valid access token was rejected.' }
Expect-Status (Send-Json '/api/v1/auth/logout' @{
    refreshToken = $session.refreshToken
}) 204 'logout'
$revoked = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" `
    -Headers $authorization -SkipHttpErrorCheck
if ($revoked.StatusCode -ne 401) { throw 'Logout did not invalidate cached access token.' }

Expect-Status (Send-Json '/api/v1/auth/password/forgot' @{
    tenantId = $tenant.tenantId; email = $adminEmail
}) 202 'forgot password'
$resetCode = Get-Code $adminEmail 'Reset your Aiyara Timesheet password'
Expect-Status (Send-Json '/api/v1/auth/password/reset' @{
    code = $resetCode; password = $newPassword
}) 204 'password reset'
Expect-Status (Send-Json '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $adminEmail; password = $oldPassword
}) 401 'old password after reset'
$login = Send-Json '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $adminEmail; password = $newPassword
}
Expect-Status $login 200 'login after reset'
$session = $login.Content | ConvertFrom-Json
$authorization = @{ Authorization = "Bearer $($session.accessToken)" }
Expect-Status (Send-Json "/api/v1/auth/tenants/$($tenant.tenantId)/password-policy" @{
    minimumLength = 24; expiryDays = 180; requireUppercase = $true
    requireLowercase = $true; requireDigit = $true; requireSymbol = $true
} 'Put' $authorization) 204 'password policy update'
$forced = Send-Json '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $adminEmail; password = $newPassword
}
Expect-Status $forced 200 'forced-change login'
if (-not ($forced.Content | ConvertFrom-Json).mustChangePassword) {
    throw 'Policy migration did not force password change.'
}
$forcedToken = ($forced.Content | ConvertFrom-Json).accessToken
$forcedRoute = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" `
    -Headers @{ Authorization = "Bearer $forcedToken" } -SkipHttpErrorCheck
if ($forcedRoute.StatusCode -ne 401) {
    throw 'Gateway accepted a token that requires a password change.'
}
Expect-Status (Send-Json '/api/v1/auth/password/change' @{
    currentPassword = $newPassword; newPassword = 'Very-Strong-New-Password-789!'
} 'Post' @{ Authorization = "Bearer $forcedToken" }) 204 'forced policy password change'
Expect-Status (Send-Json '/api/v1/auth/refresh' @{
    refreshToken = ($forced.Content | ConvertFrom-Json).refreshToken
}) 401 'forced policy old session revoked'
$compliant = Send-Json '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $adminEmail
    password = 'Very-Strong-New-Password-789!'
}
Expect-Status $compliant 200 'login after forced policy reset'
if (($compliant.Content | ConvertFrom-Json).mustChangePassword) {
    throw 'Compliant password still requires a change.'
}

$unknownEmail = "phase2-unknown-$suffix@example.test"
for ($attempt = 0; $attempt -lt 10; $attempt++) {
    Expect-Status (Send-Json '/api/v1/auth/login' @{
        tenantId = $tenant.tenantId; email = $unknownEmail; password = 'Not-A-Password!'
    }) 401 'unknown-account login'
}
Expect-Status (Send-Json '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $unknownEmail; password = 'Not-A-Password!'
}) 429 'account login rate limit'

Write-Output 'Phase 2 Gateway, SMTP, activation, invitation, session, policy and rate-limit smoke test passed.'
