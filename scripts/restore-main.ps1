<#
.SYNOPSIS
  Restores a backup made by scripts/backup-main.ps1 into a Compose stack (default: the MAIN stack).

.DESCRIPTION
  DESTRUCTIVE. Every database in the backup is dropped and recreated, and every bucket in the
  backup is made identical to the backup (objects not in the backup are deleted). Nothing is
  restored unless -Yes is given.

  Steps: verify manifest checksums (before touching anything) -> stop the application
  containers (everything except postgres and rustfs) -> apply roles (existing roles are kept,
  passwords untouched) -> pg_restore --clean --if-exists --create per database -> mc mirror
  --overwrite --remove per bucket -> start the stopped containers again and wait for health.

  The stack must already be up (so the postgres and rustfs containers exist), with the same
  .env secrets that the backed-up data was written with.

.PARAMETER Backup
  Backup folder, or 'latest' (newest folder under backups/). Default: latest.

.PARAMETER Project
  Compose project name. Default: aiyara-timesheet (the main stack).

.PARAMETER Yes
  Required to restore. Without it the script verifies the backup, prints the plan, and stops.

.PARAMETER DryRun
  Verify the backup and print the plan; change nothing (even with -Yes).

.EXAMPLE
  pwsh -NoProfile -File scripts/restore-main.ps1 -DryRun
  pwsh -NoProfile -File scripts/restore-main.ps1 -Backup backups/20261023-120000 -Yes
#>
param(
    [string]$Backup = 'latest',
    [string]$Project = 'aiyara-timesheet',
    [string]$McImage = 'minio/mc:latest',
    [int]$HealthTimeoutSeconds = 300,
    [switch]$Yes,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'backup-common.ps1')

if ($Backup -eq 'latest') {
    $latest = Get-ChildItem -LiteralPath (Join-Path $root 'backups') -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'manifest.json') } |
        Sort-Object Name | Select-Object -Last 1
    if (-not $latest) { throw 'No backup found under backups/.' }
    $Backup = $latest.FullName
}
$source = (Resolve-Path -LiteralPath $Backup).Path
$manifestPath = Join-Path $source 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "$source has no manifest.json (incomplete backup?)." }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

Write-Host "Verifying checksums of $(@($manifest.files).Count) files in $source ..."
foreach ($file in $manifest.files) {
    $path = Join-Path $source $file.path
    if (-not (Test-Path -LiteralPath $path)) { throw "Backup is incomplete: $($file.path) is missing." }
    if ((Get-Sha256 $path) -ne $file.sha256) { throw "Checksum mismatch for $($file.path); the backup is corrupt." }
}
Write-Host 'Checksums OK.'

# The manifest is not signed, so its names are checked before they reach SQL, paths or mc arguments.
foreach ($name in @($manifest.databases) + @($manifest.buckets | ForEach-Object { $_.name })) {
    if ($name -notmatch '^[A-Za-z0-9_-]+$') { throw "Manifest has an unexpected database or bucket name '$name'." }
}

$postgres = Get-ServiceContainer $Project 'postgres'
$rustfs = Get-ServiceContainer $Project 'rustfs'
$pgUser = Get-ContainerEnv $postgres 'POSTGRES_USER'
$bucketNames = @($manifest.buckets | ForEach-Object { $_.name })

Write-Host ''
Write-Host "WARNING: this OVERWRITES the data of Compose project '$Project'." -ForegroundColor Yellow
Write-Host "  Backup of project '$($manifest.project)' taken $($manifest.created)"
Write-Host "  Databases dropped and recreated: $(@($manifest.databases) -join ', ')"
Write-Host "  Buckets made identical to the backup: $($bucketNames -join ', ')"
Write-Host '  Application containers are stopped during the restore and started again afterwards.'
if ($DryRun) { Write-Host 'Dry run: nothing was changed.'; return }
if (-not $Yes) { throw 'Refusing to restore without -Yes.' }

# Every running container of the project except postgres and rustfs; restarted at the end.
$stopped = @(& docker ps -q --no-trunc --filter "label=com.docker.compose.project=$Project" |
        Where-Object { $_ -and $_ -ne $postgres -and $_ -ne $rustfs })
if ($stopped.Count) {
    Write-Host "Stopping $($stopped.Count) application containers ..."
    & docker stop @stopped | Out-Null
}
try {
    $tmp = "/tmp/aiyara-restore-$([guid]::NewGuid().ToString('N'))"
    Invoke-Native 'Creating the restore directory' { & docker exec $postgres mkdir -p $tmp } | Out-Null
    Invoke-Native 'Copying dumps in' { & docker cp "$(Join-Path $source 'postgres')/." "${postgres}:$tmp" } | Out-Null
    # Existing roles raise a harmless "already exists" error, so errors are not fatal here.
    Write-Host 'Applying roles (existing roles are kept)'
    & docker exec $postgres psql -U $pgUser -d postgres -q -f "$tmp/globals.sql" 2>&1 | Out-Null
    foreach ($db in $manifest.databases) {
        Write-Host "pg_restore $db"
        $kill = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$db' AND pid <> pg_backend_pid()"
        & docker exec $postgres psql -U $pgUser -d postgres -q -At -c $kill | Out-Null
        Invoke-Native "pg_restore $db" { & docker exec $postgres pg_restore -U $pgUser -d postgres --clean --if-exists --create --exit-on-error "$tmp/$db.dump" } | Out-Null
    }
    & docker exec $postgres rm -rf $tmp

    foreach ($bucket in $bucketNames) {
        Write-Host "mc mirror -> $bucket"
        Invoke-Mc $rustfs (Join-Path $source 'rustfs') @('mb', '--ignore-existing', "src/$bucket") | Out-Null
        Invoke-Mc $rustfs (Join-Path $source 'rustfs') @('mirror', '--quiet', '--overwrite', '--remove', "/backup/$bucket", "src/$bucket") | Out-Null
    }
}
finally {
    if ($stopped.Count) {
        Write-Host 'Starting the application containers again ...'
        & docker start @stopped | Out-Null
    }
}

$deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
do {
    $bad = @($stopped | Where-Object {
            $state = (& docker inspect -f '{{.State.Running}} {{if .State.Health}}{{.State.Health.Status}}{{end}}' $_).Trim()
            $state -notmatch '^true( healthy)?$'
        })
    if ($bad.Count -eq 0) { break }
    Start-Sleep -Seconds 5
} while ((Get-Date) -lt $deadline)
if ($bad.Count) { throw "Restore finished but $($bad.Count) container(s) are not healthy after $HealthTimeoutSeconds s; check 'docker ps' for project $Project." }
Write-Host "Restore complete; $($stopped.Count) application containers are running and healthy."
