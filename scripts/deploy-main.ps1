<#
.SYNOPSIS
  Rebuilds the user's MAIN Compose stack from develop. Run only by the primary agent, after a
  branch has been validated and merged into develop.

.DESCRIPTION
  Refuses to run unless this is the main checkout (not a linked worktree), the current branch
  is develop, and the working tree is clean. Targets only docker-compose.yml under the project
  name 'aiyara-timesheet' with the checkout's .env; the test stack (docker-compose.test.yml,
  project aiyara-test-*) is never touched. Volumes are never removed.

.PARAMETER Services
  Rebuild only these services (for example gateway-api timesheet-api). Default: all services of
  the main file, which keeps the stack consistent.

.PARAMETER Yes
  Skip the confirmation prompt.

.PARAMETER DryRun
  Run every check and print the command without executing it.
#>
param(
    [string[]]$Services = @(),
    [switch]$Yes,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root

$gitDir = (& git rev-parse --absolute-git-dir).Trim()
$commonDir = (Resolve-Path (& git rev-parse --git-common-dir)).Path
if ((Resolve-Path $gitDir).Path -ne $commonDir) {
    throw 'Refusing to deploy from a linked worktree. Run this from the main checkout.'
}
$branch = (& git branch --show-current).Trim()
if ($branch -ne 'develop') { throw "Refusing to deploy: current branch is '$branch', not 'develop'." }
if (& git status --porcelain) { throw 'Refusing to deploy: the working tree is not clean.' }
if (-not (Test-Path -LiteralPath (Join-Path $root '.env'))) { throw 'Missing .env for the main stack.' }

$command = @('compose', '-f', 'docker-compose.yml', '--env-file', '.env', '-p', 'aiyara-timesheet')
& docker @command config --quiet
if ($LASTEXITCODE -ne 0) { throw 'docker-compose.yml is invalid.' }

$commit = (& git rev-parse --short HEAD).Trim()
$target = if ($Services.Count) { $Services -join ' ' } else { 'all services' }
$up = $command + @('up', '-d', '--build', '--wait') + $Services
Write-Host "Deploy develop @ $commit to the main stack 'aiyara-timesheet' ($target)."
Write-Host "docker $($up -join ' ')"
if ($DryRun) { Write-Host 'Dry run: nothing was changed.'; return }
if (-not $Yes) {
    if ((Read-Host 'Type "deploy" to rebuild the main stack') -ne 'deploy') { throw 'Cancelled.' }
}

& docker @up
if ($LASTEXITCODE -ne 0) { throw "docker compose up failed with exit code $LASTEXITCODE." }
& docker @command ps
Write-Host "Main stack is running develop @ $commit."
