<#
.SYNOPSIS
  Runs the real-stack browser tests (src/Frontend/app/tests/e2e-real) against a throwaway test stack.

.DESCRIPTION
  Brings up the test-only Compose stack with scripts/test-stack.ps1 (database reset first, so the
  bootstrap Platform Admin is pending), including the Shell and remotes, runs
  `playwright.real.config.ts` against it, and tears the stack down again. It never touches the main
  stack (project aiyara-timesheet) and starts no dev servers, so it can run beside the mocked suite
  that uses ports 3100-3105.

  -Repeat N      Run the specs N times in a row against the same stack (stability check).
  -KeepStack     Leave the stack running afterwards; pass -SkipReset on the next run to reuse its data.
  -SkipReset     Do not reset the stack's database before starting. A reused stack must be one this
                 script left behind, because the Platform Admin's authenticator is kept in a state file.
  -Spec <text>   Only run spec files whose path matches the text (a Playwright file filter).

.EXAMPLE
  pwsh -NoProfile -File scripts/test-e2e-real.ps1
  pwsh -NoProfile -File scripts/test-e2e-real.ps1 -Repeat 3
  pwsh -NoProfile -File scripts/test-e2e-real.ps1 -Spec member-mfa -KeepStack
#>
param(
    [ValidateRange(1, 20)]
    [int]$Repeat = 1,
    [switch]$KeepStack,
    [switch]$SkipReset,
    [string]$Spec = '',
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stackScript = Join-Path $PSScriptRoot 'test-stack.ps1'
$appDir = Join-Path $root 'src/Frontend/app'
$stateDir = Join-Path $appDir 'tests/e2e-real/.state'

# The pending Platform Admin the stack bootstraps; the specs activate it and enroll an authenticator.
$env:BOOTSTRAP_PLATFORM_ADMIN_EMAIL = 'platform-e2e@example.test'

function Invoke-Stack([string[]]$Arguments) {
    & pwsh -NoProfile -File $stackScript @Arguments
    if ($LASTEXITCODE -ne 0) { throw "test-stack.ps1 $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

if (-not (Get-Command bun -ErrorAction SilentlyContinue)) { throw 'bun is required (see docs/developer-runbook.md).' }
if (-not (Test-Path -LiteralPath (Join-Path $appDir 'node_modules'))) {
    & bun install --cwd $appDir
    if ($LASTEXITCODE -ne 0) { throw 'bun install failed.' }
}

$exitCode = 1
try {
    if (-not $SkipReset) {
        Invoke-Stack @('reset', '-Frontend')
        if (Test-Path -LiteralPath $stateDir) { Remove-Item -LiteralPath $stateDir -Recurse -Force }
    }
    Invoke-Stack @('up', '-Frontend', '-TimeoutSeconds', "$TimeoutSeconds")

    $info = (& pwsh -NoProfile -File $stackScript env -Frontend) -join "`n"
    $gateway = [regex]::Match($info, 'Gateway URL:\s*(\S+)').Groups[1].Value
    $mail = [regex]::Match($info, 'MailDev URL:\s*(\S+)').Groups[1].Value
    $frontendPort = [regex]::Match($info, 'Frontend\s*:\s*http://127\.0\.0\.1:(\d+)').Groups[1].Value
    if (-not $gateway -or -not $mail -or -not $frontendPort) { throw "Could not read the stack's URLs from test-stack.ps1 env: $info" }

    # The Shell is opened as localhost, the origin the Gateway's CORS policy and the remotes allow.
    $env:E2E_REAL_BASE_URL = "http://localhost:$frontendPort"
    $env:E2E_REAL_GATEWAY_URL = $gateway
    $env:E2E_REAL_MAILDEV_URL = $mail
    $env:E2E_REAL_PLATFORM_ADMIN_EMAIL = $env:BOOTSTRAP_PLATFORM_ADMIN_EMAIL
    Write-Host "Real-stack tests against $($env:E2E_REAL_BASE_URL) (gateway $gateway, mail $mail)"

    Push-Location $appDir
    try {
        $playwright = @('run', 'test:e2e:real')
        if ($Spec) { $playwright += $Spec }
        for ($run = 1; $run -le $Repeat; $run++) {
            Write-Host "== Run $run of $Repeat =="
            & bun @playwright
            if ($LASTEXITCODE -ne 0) { throw "Run $run of $Repeat failed with exit code $LASTEXITCODE." }
        }
        $exitCode = 0
    }
    finally { Pop-Location }
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
}
finally {
    if ($KeepStack) { Write-Host 'Leaving the test stack running (-KeepStack).' }
    else { try { Invoke-Stack @('reset', '-Frontend') } catch { Write-Host "Tear-down failed: $($_.Exception.Message)" -ForegroundColor Red } }
}
exit $exitCode
