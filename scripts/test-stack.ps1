<#
.SYNOPSIS
  Runs the test-only Compose stack (docker-compose.test.yml) built from this worktree.

.DESCRIPTION
  up      Build from the current worktree, start, wait until healthy, print the URLs.
  down    Stop the stack and keep its volumes and database.
  reset   Stop the stack and delete its volumes, so the next `up` starts on an empty database.
  status  Show containers, ports, and the URLs.
  smoke   Run tests/phaseN/Smoke.ps1 against this stack (default phase 3).
  env     Print the environment variables a test run needs (gateway and mail URLs).

  The stack never touches the main compose project. Its name and ports come from
  TEST_STACK_NAME and TEST_PORT_BASE; unset, both derive from this worktree's path, so several
  worktrees can each run their own stack at the same time without configuration.
  Ports (base = TEST_PORT_BASE): gateway +1, MailDev +2, Postgres +4, frontend +10,
  remotes +12..+15. Secrets are random and live in the git-ignored .env.test.

.EXAMPLE
  pwsh -NoProfile -File scripts/test-stack.ps1 up
  pwsh -NoProfile -File scripts/test-stack.ps1 smoke -Phase 3
  pwsh -NoProfile -File scripts/test-stack.ps1 down
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('up', 'down', 'reset', 'status', 'smoke', 'env')]
    [string]$Command,
    [switch]$Frontend,
    [ValidateRange(1, 4)]
    [int]$Phase = 3,
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$composeFile = Join-Path $root 'docker-compose.test.yml'
$envFile = Join-Path $root '.env.test'
$mainProject = 'aiyara-timesheet'

# Stable per-worktree defaults: a short hash of the checkout path.
$sha = [System.Security.Cryptography.SHA1]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($root.ToLowerInvariant()))
$hex = ([BitConverter]::ToString($sha) -replace '-', '').ToLowerInvariant()
$name = if ($env:TEST_STACK_NAME) { $env:TEST_STACK_NAME } else { "aiyara-test-$($hex.Substring(0, 6))" }
$base = if ($env:TEST_PORT_BASE) { [int]$env:TEST_PORT_BASE } else { 20000 + ([Convert]::ToInt32($hex.Substring(6, 4), 16) % 100) * 100 }

if ($name -eq $mainProject -or $name -notmatch '^[a-z0-9][a-z0-9_-]*$') {
    throw "TEST_STACK_NAME '$name' is the main stack's name or not a valid Compose project name."
}
if ($base -lt 1024 -or $base + 20 -gt 65000 -or ($base -le 3105 -and $base + 20 -ge 3100)) {
    throw "TEST_PORT_BASE $base is out of range or overlaps Playwright's 3100-3105."
}

$ports = [ordered]@{
    GATEWAY_API_PORT             = $base + 1
    MAILDEV_WEB_PORT             = $base + 2
    POSTGRES_PORT                = $base + 4
    FRONTEND_PORT                = $base + 10
    TIMESHEET_REMOTE_PORT        = $base + 12
    REPORTING_REMOTE_PORT        = $base + 13
    ADMINISTRATION_REMOTE_PORT   = $base + 14
    IDENTITY_PROFILE_REMOTE_PORT = $base + 15
}
$gatewayUrl = "http://127.0.0.1:$($ports.GATEWAY_API_PORT)"
$mailUrl = "http://127.0.0.1:$($ports.MAILDEV_WEB_PORT)"

function Get-RandomSecret([int]$Bytes = 32) {
    $buffer = [byte[]]::new($Bytes)
    [Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    [Convert]::ToBase64String($buffer)
}

function Get-Alphanumeric([int]$Length = 24) {
    (Get-RandomSecret 48) -replace '[^A-Za-z0-9]', '' | ForEach-Object { $_.Substring(0, $Length) }
}

# Secrets are created once and kept so an existing volume still matches them; ports and
# URLs are rewritten every time so a changed TEST_PORT_BASE takes effect.
function Write-EnvFile {
    $existing = @{}
    if (Test-Path -LiteralPath $envFile) {
        foreach ($line in Get-Content -LiteralPath $envFile) {
            if ($line -match '^\s*([A-Z0-9_]+)=(.*)$') { $existing[$Matches[1]] = $Matches[2] }
        }
    }
    function Keep([string]$key, [scriptblock]$make) { if ($existing.ContainsKey($key)) { $existing[$key] } else { & $make } }
    $values = [ordered]@{
        POSTGRES_USER                = 'timesheet'
        POSTGRES_PASSWORD            = Keep POSTGRES_PASSWORD { Get-Alphanumeric }
        IDENTITY_DB_PASSWORD         = Keep IDENTITY_DB_PASSWORD { Get-Alphanumeric }
        TIMESHEET_DB_PASSWORD        = Keep TIMESHEET_DB_PASSWORD { Get-Alphanumeric }
        REPORTING_DB_PASSWORD        = Keep REPORTING_DB_PASSWORD { Get-Alphanumeric }
        NOTIFICATION_DB_PASSWORD     = Keep NOTIFICATION_DB_PASSWORD { Get-Alphanumeric }
        REDIS_PASSWORD               = Keep REDIS_PASSWORD { Get-Alphanumeric }
        PASETO_SIGNING_SEED          = Keep PASETO_SIGNING_SEED { Get-RandomSecret }
        MFA_ENCRYPTION_KEY           = Keep MFA_ENCRYPTION_KEY { Get-RandomSecret }
        NOTIFICATION_INTERNAL_KEY    = Keep NOTIFICATION_INTERNAL_KEY { Get-Alphanumeric 40 }
        REPORTING_INTERNAL_KEY       = Keep REPORTING_INTERNAL_KEY { Get-Alphanumeric 40 }
        RABBITMQ_USER                = 'timesheet'
        RABBITMQ_PASSWORD            = Keep RABBITMQ_PASSWORD { Get-Alphanumeric }
        RUSTFS_ACCESS_KEY            = 'TESTSTACK'
        RUSTFS_SECRET_KEY            = Keep RUSTFS_SECRET_KEY { Get-Alphanumeric }
        # Required by the shared service definitions even though Grafana is not in this stack.
        GRAFANA_ADMIN_USER           = 'admin'
        GRAFANA_ADMIN_PASSWORD       = 'unused'
        # A pending Platform Admin for the browser tests (scripts/test-e2e-real.ps1 sets it); empty otherwise.
        BOOTSTRAP_PLATFORM_ADMIN_EMAIL = if ($env:BOOTSTRAP_PLATFORM_ADMIN_EMAIL) { $env:BOOTSTRAP_PLATFORM_ADMIN_EMAIL } else { '' }
        TEST_STACK_NAME              = $name
        TEST_PORT_BASE               = $base
    }
    foreach ($key in $ports.Keys) { $values[$key] = $ports[$key] }
    $values['FRONTEND_ORIGIN'] = "http://localhost:$($ports.FRONTEND_PORT)"
    $values['FRONTEND_PUBLIC_URL'] = "http://localhost:$($ports.FRONTEND_PORT)"
    $header = "# Generated by scripts/test-stack.ps1 for the test stack '$name'. Git-ignored; safe to delete after `reset`."
    Set-Content -LiteralPath $envFile -Value (@($header) + ($values.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }))
}

function Invoke-Compose([string[]]$Arguments) {
    $all = @('compose', '-f', $composeFile, '--env-file', $envFile, '-p', $name)
    if ($Frontend) { $all += @('--profile', 'frontend') }
    & docker @all @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Assert-PortsFree {
    $inUse = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $ports.Values -contains $_.LocalPort } | Select-Object -ExpandProperty LocalPort -Unique
    # Ports already published by this very stack are fine on a repeated `up`.
    $own = & docker ps --filter "label=com.docker.compose.project=$name" --format '{{.Ports}}' 2>$null
    $foreign = @($inUse | Where-Object { -not ($own -match ":$_->") })
    if ($foreign.Count -gt 0) {
        throw "Host port(s) $($foreign -join ', ') are in use. Set TEST_PORT_BASE to a free base (ports base+1..base+15)."
    }
}

function Show-Urls {
    Write-Host "Test stack : $name  (base port $base)"
    Write-Host "Gateway URL: $gatewayUrl"
    Write-Host "MailDev URL: $mailUrl"
    Write-Host "Postgres   : 127.0.0.1:$($ports.POSTGRES_PORT)"
    if ($Frontend) { Write-Host "Frontend   : http://127.0.0.1:$($ports.FRONTEND_PORT)" }
}

function Set-TestEnvironment {
    $env:TIMESHEET_GATEWAY_URL = $gatewayUrl
    $env:TIMESHEET_MAILDEV_URL = $mailUrl
    $env:TIMESHEET_ENV_FILE = $envFile
    $env:TIMESHEET_PROJECT_NAME = $name
    $env:TIMESHEET_COMPOSE_FILE = $composeFile
}

switch ($Command) {
    'up' {
        Write-EnvFile
        Assert-PortsFree
        Invoke-Compose @('up', '-d', '--build', '--wait', '--wait-timeout', "$TimeoutSeconds")
        Show-Urls
    }
    'down' {
        Write-EnvFile
        Invoke-Compose @('down', '--remove-orphans')
        Write-Host "Stopped $name. Its volumes were kept; use 'reset' to delete them."
    }
    'reset' {
        Write-EnvFile
        Invoke-Compose @('down', '--volumes', '--remove-orphans')
        Write-Host "Stopped $name and deleted its volumes."
    }
    'status' {
        Write-EnvFile
        Invoke-Compose @('ps', '--all')
        Show-Urls
    }
    'smoke' {
        Set-TestEnvironment
        Push-Location $root
        try {
            & pwsh -NoProfile -File (Join-Path $root "tests/phase$Phase/Smoke.ps1")
            if ($LASTEXITCODE -ne 0) { throw "tests/phase$Phase/Smoke.ps1 failed with exit code $LASTEXITCODE." }
        }
        finally { Pop-Location }
    }
    'env' {
        Show-Urls
        Write-Host "`$env:TIMESHEET_GATEWAY_URL = '$gatewayUrl'"
        Write-Host "`$env:TIMESHEET_MAILDEV_URL = '$mailUrl'"
        Write-Host "`$env:TIMESHEET_ENV_FILE = '$envFile'"
    }
}
