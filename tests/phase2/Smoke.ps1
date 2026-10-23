param(
    [string]$GatewayUrl = $(if ($env:TIMESHEET_GATEWAY_URL) { $env:TIMESHEET_GATEWAY_URL } else { 'http://127.0.0.1:8081' }),
    [string]$MailDevUrl = $(if ($env:TIMESHEET_MAILDEV_URL) { $env:TIMESHEET_MAILDEV_URL } else { 'http://127.0.0.1:8080' }),
    [string]$MailDevToken = '',
    [string]$BootstrapEmail = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' 'MailInbox.ps1')
$MailDevToken = Resolve-MailDevToken $MailDevToken '.env'

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
        $message = Find-MailMessage $MailDevUrl $MailDevToken $email $subject
        if ($message) {
            $codeMatch = [regex]::Match($message.Text, '(?mi)code:\s*(\S+)')
            if ($codeMatch.Success) { return $codeMatch.Groups[1].Value }
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Expected $subject email was not delivered."
}

if ($BootstrapEmail) {
    Get-Code $BootstrapEmail 'Activate your Aiyara Timesheet account' | Out-Null
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
$tenant = ($created.Content | ConvertFrom-Json).result
$code = Get-Code $adminEmail 'Activate your Aiyara Timesheet account'
$activated = Send-Json '/api/v1/auth/activate' @{ code = $code; password = $oldPassword }
Expect-Status $activated 204 'activation'
Expect-Status (Send-Json '/api/v1/auth/activate' @{
    code = $code; password = $oldPassword
}) 401 'single-use activation'

# The public login body selects a tenant only by organization code; a caller-supplied
# internal tenant ID is ignored, so these otherwise-valid credentials must be rejected.
Expect-Status (Send-Json '/api/v1/auth/login' @{
    tenantId = $tenant.tenantId; email = $adminEmail; password = $oldPassword
}) 401 'login by internal tenant ID'
$login = Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $adminEmail; password = $oldPassword
}
Expect-Status $login 200 'login'
$session = ($login.Content | ConvertFrom-Json).result
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
$invitationData = ($invitation.Content | ConvertFrom-Json).result
$inviteMailCode = Get-Code $employeeEmail 'Your Aiyara Timesheet invitation'
if ($inviteMailCode -ne $invitationData.code) {
    throw 'Invitation email did not match the issued invitation.'
}
# With FRONTEND_PUBLIC_URL set, the email links to the page that accepts the code, without the code.
$inviteMail = Find-MailMessage $MailDevUrl $MailDevToken $employeeEmail 'Your Aiyara Timesheet invitation'
$pageLine = [regex]::Match($inviteMail.Text, '(?m)^\S[^\r\n]*page:\s*(\S+)')
if ($pageLine.Success -and $pageLine.Groups[1].Value -notmatch '/accept-invitation$') {
    throw "Invitation email links to $($pageLine.Groups[1].Value) instead of the invitation page."
}
Expect-Status (Send-Json '/api/v1/invitations/accept' @{
    code = $inviteMailCode
}) 200 'invitation acceptance'
$employeeCode = Get-Code $employeeEmail 'Activate your Aiyara Timesheet account'
Expect-Status (Send-Json '/api/v1/auth/activate' @{
    code = $employeeCode; password = $oldPassword
}) 204 'invited employee activation'
Expect-Status (Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $employeeEmail; password = $oldPassword
}) 200 'invited employee login'

# Tenant Admin member management through Gateway.
function Get-Members([string]$path) {
    $response = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/identity/members$path" -Headers $authorization -SkipHttpErrorCheck
    Expect-Status $response 200 "GET members$path"
    @(($response.Content | ConvertFrom-Json).result)
}
$members = Get-Members ''
$employee = $members | Where-Object email -eq $employeeEmail
$self = $members | Where-Object email -eq $adminEmail
if (-not $employee -or $employee.status -ne 'Active' -or -not $self.isCurrentUser) {
    throw 'Member directory did not list the activated employee and mark the admin as the caller.'
}
$pendingEmail = "phase2-pending-$suffix@example.test"
Expect-Status (Send-Json "/api/v1/auth/tenants/$($tenant.tenantId)/invitations" @{
    email = $pendingEmail; role = 'Employee'
} 'Post' $authorization) 201 'second invitation'
$pending = Get-Members '/invitations' | Where-Object email -eq $pendingEmail
if (-not $pending) { throw 'Pending invitation was not listed.' }
Expect-Status (Send-Json "/api/v1/identity/members/invitations/$($pending.id)" $null 'Delete' $authorization) 204 'invitation revocation'
if (Get-Members '/invitations' | Where-Object email -eq $pendingEmail) { throw 'Revoked invitation is still listed.' }
Expect-Status (Send-Json "/api/v1/identity/members/$($self.id)/role" @{ role = 'Employee' } 'Put' $authorization) 409 'own role change'
Expect-Status (Send-Json "/api/v1/identity/members/$($employee.id)/role" @{ role = 'TenantAdmin' } 'Put' $authorization) 204 'promotion'
Expect-Status (Send-Json "/api/v1/identity/members/$($employee.id)/status" @{ active = $false } 'Put' $authorization) 204 'deactivation'
Expect-Status (Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $employeeEmail; password = $oldPassword
}) 401 'deactivated member login'
Expect-Status (Send-Json "/api/v1/identity/members/$($employee.id)/status" @{ active = $true } 'Put' $authorization) 204 'reactivation'
Expect-Status (Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $employeeEmail; password = $oldPassword
}) 200 'reactivated member login'
if ((Get-Members '' | Where-Object email -eq $employeeEmail).role -ne 'TenantAdmin') {
    throw 'Reactivation did not keep the promoted role.'
}

$refreshed = Send-Json '/api/v1/auth/refresh' @{
    refreshToken = $session.refreshToken
}
Expect-Status $refreshed 200 'refresh rotation'
$newSession = ($refreshed.Content | ConvertFrom-Json).result
Expect-Status (Send-Json '/api/v1/auth/refresh' @{
    refreshToken = $session.refreshToken
}) 401 'refresh replay'
Expect-Status (Send-Json '/api/v1/auth/refresh' @{
    refreshToken = $newSession.refreshToken
}) 401 'replay revocation'

$login = Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $adminEmail; password = $oldPassword
}
Expect-Status $login 200 'second login'
$session = ($login.Content | ConvertFrom-Json).result
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
    email = $adminEmail
}) 202 'forgot password'
$resetCode = Get-Code $adminEmail 'Reset your Aiyara Timesheet password'
Expect-Status (Send-Json '/api/v1/auth/password/reset' @{
    code = $resetCode; password = $newPassword
}) 204 'password reset'
Expect-Status (Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $adminEmail; password = $oldPassword
}) 401 'old password after reset'
$login = Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $adminEmail; password = $newPassword
}
Expect-Status $login 200 'login after reset'
$session = ($login.Content | ConvertFrom-Json).result
$authorization = @{ Authorization = "Bearer $($session.accessToken)" }
Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" `
    -Headers $authorization -SkipHttpErrorCheck | Out-Null
Expect-Status (Send-Json "/api/v1/auth/tenants/$($tenant.tenantId)/password-policy" @{
    minimumLength = 24; expiryDays = 180; requireUppercase = $true
    requireLowercase = $true; requireDigit = $true; requireSymbol = $true
} 'Put' $authorization) 204 'password policy update'
$oldAccess = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" `
    -Headers $authorization -SkipHttpErrorCheck
if ($oldAccess.StatusCode -ne 401) {
    throw 'Policy migration did not invalidate a cached access token.'
}
Expect-Status (Send-Json '/api/v1/auth/refresh' @{
    refreshToken = $session.refreshToken
}) 401 'policy-revoked refresh session'
$forced = Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $adminEmail; password = $newPassword
}
Expect-Status $forced 200 'forced-change login'
if (-not (($forced.Content | ConvertFrom-Json).result).mustChangePassword) {
    throw 'Policy migration did not force password change.'
}
$forcedToken = (($forced.Content | ConvertFrom-Json).result).accessToken
$forcedRoute = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/timesheets/health" `
    -Headers @{ Authorization = "Bearer $forcedToken" } -SkipHttpErrorCheck
if ($forcedRoute.StatusCode -ne 401) {
    throw 'Gateway accepted a token that requires a password change.'
}
Expect-Status (Send-Json '/api/v1/auth/password/change' @{
    currentPassword = $newPassword; newPassword = 'Very-Strong-New-Password-789!'
} 'Post' @{ Authorization = "Bearer $forcedToken" }) 204 'forced policy password change'
Expect-Status (Send-Json '/api/v1/auth/refresh' @{
    refreshToken = (($forced.Content | ConvertFrom-Json).result).refreshToken
}) 401 'forced policy old session revoked'
$compliant = Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $adminEmail
    password = 'Very-Strong-New-Password-789!'
}
Expect-Status $compliant 200 'login after forced policy reset'
if ((($compliant.Content | ConvertFrom-Json).result).mustChangePassword) {
    throw 'Compliant password still requires a change.'
}

# Member MFA: a Tenant Admin requires it, members enroll at their next sign-in, and a lost device is reset.
function ConvertFrom-Base32([string]$text) {
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $bits = -join ($text.ToUpperInvariant().TrimEnd('=').ToCharArray() | ForEach-Object {
        [Convert]::ToString($alphabet.IndexOf($_), 2).PadLeft(5, '0') })
    , [byte[]]@(for ($i = 0; $i + 8 -le $bits.Length; $i += 8) { [Convert]::ToByte($bits.Substring($i, 8), 2) })
}
function Get-Totp([string]$secret, [int]$offset = 0) {
    $step = [long][math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30) + $offset
    $counter = [BitConverter]::GetBytes($step); [Array]::Reverse($counter)
    $hash = [Security.Cryptography.HMACSHA1]::new((ConvertFrom-Base32 $secret)).ComputeHash($counter)
    $start = $hash[$hash.Length - 1] -band 0x0F
    # Bytes are widened first: PowerShell keeps a shifted byte a byte and would drop the high bits.
    $binary = (([int]$hash[$start] -band 0x7F) -shl 24) -bor ([int]$hash[$start + 1] -shl 16) -bor
        ([int]$hash[$start + 2] -shl 8) -bor [int]$hash[$start + 3]
    ($binary % 1000000).ToString('000000')
}
function Expect-ErrorCode($response, [int]$status, [string]$code, [string]$step) {
    Expect-Status $response $status $step
    $actual = ($response.Content | ConvertFrom-Json).errorCode
    if ($actual -ne $code) { throw "$step returned error code $actual, expected $code." }
}

# Gateway allows 60 auth calls a minute per client; start this section on a fresh window.
Start-Sleep -Seconds 61

$mfaAdmin = "phase2-mfa-admin-$suffix@example.test"
$mfaStaff = "phase2-mfa-staff-$suffix@example.test"
$mfaTenantResponse = Send-Json '/api/v1/tenants' @{ name = 'Phase 2 MFA'; slug = "phase2-mfa-$suffix"; adminEmail = $mfaAdmin }
Expect-Status $mfaTenantResponse 201 'MFA tenant creation'
$mfaTenant = ($mfaTenantResponse.Content | ConvertFrom-Json).result
Expect-Status (Send-Json '/api/v1/auth/activate' @{
    code = (Get-Code $mfaAdmin 'Activate your Aiyara Timesheet account'); password = $oldPassword
}) 204 'MFA admin activation'
$mfaLogin = @{ organizationCode = $mfaTenant.organizationCode; email = $mfaAdmin; password = $oldPassword }
$mfaAdminSession = ($(Send-Json '/api/v1/auth/login' $mfaLogin).Content | ConvertFrom-Json).result
$mfaAdminAuth = @{ Authorization = "Bearer $($mfaAdminSession.accessToken)" }
Expect-Status (Send-Json "/api/v1/auth/tenants/$($mfaTenant.tenantId)/invitations" @{
    email = $mfaStaff; role = 'Employee' } 'Post' $mfaAdminAuth) 201 'MFA staff invitation'
Expect-Status (Send-Json '/api/v1/invitations/accept' @{
    code = (Get-Code $mfaStaff 'Your Aiyara Timesheet invitation') }) 200 'MFA staff invitation acceptance'
Expect-Status (Send-Json '/api/v1/auth/activate' @{
    code = (Get-Code $mfaStaff 'Activate your Aiyara Timesheet account'); password = $oldPassword
}) 204 'MFA staff activation'
$staffLoginBody = @{ organizationCode = $mfaTenant.organizationCode; email = $mfaStaff; password = $oldPassword }
$staffBefore = ($(Send-Json '/api/v1/auth/login' $staffLoginBody).Content | ConvertFrom-Json).result

# The admin must have an authenticator before requiring MFA (opt-in through the signed-in endpoints).
Expect-ErrorCode (Send-Json '/api/v1/identity/organization/mfa' @{ required = $true } 'Put' $mfaAdminAuth) `
    409 'MFA_NOT_ENROLLED' 'requirement without an enrolled admin'
$adminOffer = Send-Json '/api/v1/identity/mfa/enrollment' $null 'Post' $mfaAdminAuth
Expect-Status $adminOffer 200 'admin opt-in enrollment'
$adminSecret = ($adminOffer.Content | ConvertFrom-Json).result.secret
$adminConfirm = Send-Json '/api/v1/identity/mfa/enrollment/confirm' @{ totpCode = (Get-Totp $adminSecret) } 'Post' $mfaAdminAuth
Expect-Status $adminConfirm 200 'admin enrollment confirmation'
if (($adminConfirm.Content | ConvertFrom-Json).result.recoveryCodes.Count -ne 10) { throw 'Expected ten recovery codes.' }
Expect-Status (Send-Json '/api/v1/identity/organization/mfa' @{ required = $true; reason = 'Smoke' } 'Put' $mfaAdminAuth) 200 'MFA requirement on'
if (-not (($(Invoke-WebRequest -Uri "$GatewayUrl/api/v1/identity/organization" -Headers $mfaAdminAuth -SkipHttpErrorCheck).Content |
    ConvertFrom-Json).result.requireMfa)) { throw 'Organization did not report the MFA requirement.' }
Expect-Status (Send-Json '/api/v1/auth/refresh' @{ refreshToken = $staffBefore.refreshToken }) 401 'unenrolled staff session ended by the requirement'

# The member gets no session until the authenticator is confirmed, then answers a code or a recovery code.
Expect-ErrorCode (Send-Json '/api/v1/auth/login' $staffLoginBody) 401 'MFA_ENROLLMENT_REQUIRED' 'staff login without an authenticator'
$staffOffer = Send-Json '/api/v1/auth/mfa/enrollment' @{ email = $mfaStaff; password = $oldPassword }
Expect-Status $staffOffer 200 'staff enrollment offer'
$staffSecret = ($staffOffer.Content | ConvertFrom-Json).result.secret
Expect-ErrorCode (Send-Json '/api/v1/auth/login' ($staffLoginBody + @{ totpCode = '000000' })) 401 'MFAINVALID' 'wrong enrollment code'
$staffEnrolled = Send-Json '/api/v1/auth/login' ($staffLoginBody + @{ totpCode = (Get-Totp $staffSecret) })
Expect-Status $staffEnrolled 200 'staff enrollment login'
$staffCodes = ($staffEnrolled.Content | ConvertFrom-Json).result.recoveryCodes
if ($staffCodes.Count -ne 10) { throw 'Enrollment login did not return ten recovery codes.' }
Expect-ErrorCode (Send-Json '/api/v1/auth/login' $staffLoginBody) 401 'MFAREQUIRED' 'staff login without a code'
Expect-Status (Send-Json '/api/v1/auth/login' ($staffLoginBody + @{ totpCode = (Get-Totp $staffSecret 1) })) 200 'staff login with a code'
Expect-Status (Send-Json '/api/v1/auth/login' ($staffLoginBody + @{ recoveryCode = $staffCodes[0] })) 200 'staff login with a recovery code'
Expect-ErrorCode (Send-Json '/api/v1/auth/login' ($staffLoginBody + @{ recoveryCode = $staffCodes[0] })) 401 'MFAINVALID' 'recovery code reuse'
Expect-ErrorCode (Send-Json '/api/v1/auth/mfa/enrollment' @{ email = $mfaStaff; password = $oldPassword }) 409 'MFA_ALREADY_ENROLLED' 'second enrollment offer'

# Logins are limited to ten a minute per email, so let the window pass before the next member sign-ins.
Start-Sleep -Seconds 61

# A lost device: the admin resets the member, who must enroll again; the admin cannot reset themselves.
$mfaMembers = @(($(Invoke-WebRequest -Uri "$GatewayUrl/api/v1/identity/members" -Headers $mfaAdminAuth -SkipHttpErrorCheck).Content |
    ConvertFrom-Json).result)
$staffMember = $mfaMembers | Where-Object email -eq $mfaStaff
$adminMember = $mfaMembers | Where-Object email -eq $mfaAdmin
if (-not $staffMember.mfaEnabled -or -not $adminMember.mfaEnabled) { throw 'Member directory did not report MFA as enabled.' }
Expect-ErrorCode (Send-Json "/api/v1/identity/members/$($adminMember.id)/mfa-reset" @{ reason = 'self' } 'Post' $mfaAdminAuth) `
    409 'CANNOT_CHANGE_SELF' 'admin resetting their own MFA'
Expect-Status (Send-Json "/api/v1/identity/members/$($staffMember.id)/mfa-reset" @{ reason = 'Lost phone' } 'Post' $mfaAdminAuth) 204 'member MFA reset'
Expect-ErrorCode (Send-Json '/api/v1/auth/login' $staffLoginBody) 401 'MFA_ENROLLMENT_REQUIRED' 'staff login after reset'
Expect-Status (Send-Json '/api/v1/identity/organization/mfa' @{ required = $false } 'Put' $mfaAdminAuth) 200 'MFA requirement off'
$sessionDesktop = Send-Json '/api/v1/auth/login' $staffLoginBody 'Post' @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36' }
Expect-Status $sessionDesktop 200 'staff login once the requirement is off'

# Session management: list own sessions, end one, then all the others, then sign out everywhere.
$sessionPhone = Send-Json '/api/v1/auth/login' $staffLoginBody 'Post' @{ 'User-Agent' = 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Version/17.0 Mobile/15E148 Safari/604.1' }
Expect-Status $sessionPhone 200 'staff second login'
$desktopSession = ($sessionDesktop.Content | ConvertFrom-Json).result
$phoneSession = ($sessionPhone.Content | ConvertFrom-Json).result
$desktopAuth = @{ Authorization = "Bearer $($desktopSession.accessToken)" }
$sessionList = Invoke-WebRequest -Uri "$GatewayUrl/api/v1/identity/sessions" -Headers $desktopAuth -SkipHttpErrorCheck
Expect-Status $sessionList 200 'list own sessions'
$sessions = @(($sessionList.Content | ConvertFrom-Json).result)
if ($sessions.Count -lt 2) { throw 'Expected at least two sessions.' }
$currentSessions = @($sessions | Where-Object current)
if ($currentSessions.Count -ne 1 -or $currentSessions[0].device -ne 'Chrome on Windows') { throw 'Current session not marked or device not recognised.' }
$phoneRow = $sessions | Where-Object { $_.device -eq 'Safari on iOS' } | Select-Object -First 1
if (-not $phoneRow) { throw 'Phone session was not listed.' }
Expect-ErrorCode (Send-Json "/api/v1/identity/sessions/$($currentSessions[0].id)" $null 'Delete' $desktopAuth) 409 'CURRENT_SESSION' 'ending the current session'
Expect-ErrorCode (Send-Json "/api/v1/identity/sessions/$([guid]::NewGuid())" $null 'Delete' $desktopAuth) 404 'NOT_FOUND' 'ending an unknown session'
Expect-ErrorCode (Send-Json "/api/v1/identity/sessions/$($mfaMembers[0].id)" $null 'Delete' $desktopAuth) 404 'NOT_FOUND' 'ending a session id of another account'
Expect-Status (Send-Json "/api/v1/identity/sessions/$($phoneRow.id)" $null 'Delete' $desktopAuth) 204 'ending the phone session'
Expect-Status (Send-Json '/api/v1/auth/refresh' @{ refreshToken = $phoneSession.refreshToken }) 401 'refresh of an ended session'
Expect-Status (Send-Json '/api/v1/identity/sessions/revoke-others' $null 'Post' $desktopAuth) 204 'ending the other sessions'
Expect-Status (Send-Json '/api/v1/identity/sessions/revoke-all' $null 'Post' $desktopAuth) 204 'signing out everywhere'
Expect-Status (Invoke-WebRequest -Uri "$GatewayUrl/api/v1/identity/sessions" -Headers $desktopAuth -SkipHttpErrorCheck) 401 'sessions with a signed-out token'
Expect-Status (Send-Json '/api/v1/auth/refresh' @{ refreshToken = $desktopSession.refreshToken }) 401 'refresh after signing out everywhere'

$unknownEmail = "phase2-unknown-$suffix@example.test"
for ($attempt = 0; $attempt -lt 10; $attempt++) {
    Expect-Status (Send-Json '/api/v1/auth/login' @{
        organizationCode = $tenant.organizationCode; email = $unknownEmail; password = 'Not-A-Password!'
    }) 401 'unknown-account login'
}
Expect-Status (Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = $unknownEmail; password = 'Not-A-Password!'
}) 429 'account login rate limit'
Expect-Status (Send-Json '/api/v1/auth/login' @{
    organizationCode = $tenant.organizationCode; email = "other-$unknownEmail"
    password = 'Not-A-Password!'
}) 401 'independent account remains under rate limit'

Write-Output 'Phase 2 Gateway, SMTP, activation, invitation, session, policy, member MFA and rate-limit smoke test passed.'
