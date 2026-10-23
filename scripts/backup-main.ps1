<#
.SYNOPSIS
  Backs up the PostgreSQL databases and RustFS buckets of a Compose stack (default: the MAIN stack).

.DESCRIPTION
  Writes backups/<yyyyMMdd-HHmmss>/ (git-ignored) containing:
    postgres/<db>.dump    pg_dump custom format, one per database found in the postgres container
    postgres/globals.sql  pg_dumpall --globals-only --no-role-passwords (roles; passwords are
                          re-applied by db-provisioner from .env, never stored in a backup)
    rustfs/<bucket>/...   every object, copied through the S3 API
    manifest.json         project, time, databases, buckets, and a SHA-256 of every file

  Why the S3 API and not a copy of the rustfs-data volume: a volume copy of a running RustFS is
  not guaranteed consistent and ties the backup to RustFS's on-disk format and version, while an
  S3 mirror is a logical copy that restores into any S3-compatible store. It is done with a
  throwaway minio/mc container on the stack's network, so nothing is installed or published.

  The stack keeps running; the script only reads from it. Databases are dumped one by one (each
  is internally consistent; no cross-database snapshot), so run it when the application is quiet
  if cross-service consistency matters. Secrets in .env are NOT backed up.

.PARAMETER Project
  Compose project name. Default: aiyara-timesheet (the main stack). Test stacks: aiyara-test-xxxxxx.

.PARAMETER OutputRoot
  Directory that receives the timestamped backup folder. Default: <repo>/backups.

.PARAMETER McImage
  Image providing the `mc` client. Default: minio/mc:latest.

.PARAMETER DryRun
  Discover databases and buckets and print what would be written; write nothing.

.EXAMPLE
  pwsh -NoProfile -File scripts/backup-main.ps1 -DryRun
  pwsh -NoProfile -File scripts/backup-main.ps1
  pwsh -NoProfile -File scripts/backup-main.ps1 -Project aiyara-test-1a2b3c
#>
param(
    [string]$Project = 'aiyara-timesheet',
    [string]$OutputRoot = '',
    [string]$McImage = 'minio/mc:latest',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'backup-common.ps1')
if (-not $OutputRoot) { $OutputRoot = Join-Path $root 'backups' }

$postgres = Get-ServiceContainer $Project 'postgres'
$rustfs = Get-ServiceContainer $Project 'rustfs'
$pgUser = Get-ContainerEnv $postgres 'POSTGRES_USER'
$databases = Get-DatabaseNames $postgres $pgUser
$probe = Join-Path ([IO.Path]::GetTempPath()) "aiyara-backup-probe-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $probe | Out-Null
try { $buckets = Get-BucketNames $rustfs $probe }
finally { Remove-Item -LiteralPath $probe -Recurse -Force -ErrorAction SilentlyContinue }

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$target = Join-Path $OutputRoot $stamp
Write-Host "Backup of Compose project '$Project' -> $target"
Write-Host "Databases ($($databases.Count)): $($databases -join ', ')"
Write-Host "Buckets   ($($buckets.Count)): $($buckets -join ', ')"
if ($databases.Count -eq 0) { throw 'No application databases found; refusing to write an empty backup.' }
if ($DryRun) { Write-Host 'Dry run: nothing was written.'; return }

New-Item -ItemType Directory -Path (Join-Path $target 'postgres'), (Join-Path $target 'rustfs') -Force | Out-Null
try {
    $tmp = "/tmp/aiyara-backup-$stamp"
    Invoke-Native 'Creating the dump directory' { & docker exec $postgres mkdir -p $tmp } | Out-Null
    foreach ($db in $databases) {
        Write-Host "pg_dump $db"
        Invoke-Native "pg_dump $db" { & docker exec $postgres pg_dump -U $pgUser -Fc -f "$tmp/$db.dump" $db } | Out-Null
    }
    Write-Host 'pg_dumpall --globals-only'
    Invoke-Native 'pg_dumpall' { & docker exec $postgres pg_dumpall -U $pgUser --globals-only --no-role-passwords -f "$tmp/globals.sql" } | Out-Null
    Invoke-Native 'Copying dumps out' { & docker cp "${postgres}:$tmp/." (Join-Path $target 'postgres') } | Out-Null
    & docker exec $postgres rm -rf $tmp

    foreach ($bucket in $buckets) {
        Write-Host "mc mirror $bucket"
        New-Item -ItemType Directory -Path (Join-Path $target 'rustfs' $bucket) -Force | Out-Null
        Invoke-Mc $rustfs (Join-Path $target 'rustfs') @('mirror', '--quiet', "src/$bucket", "/backup/$bucket") | Out-Null
    }

    $files = @(Get-ChildItem -LiteralPath $target -Recurse -File | ForEach-Object {
            [ordered]@{
                path   = [IO.Path]::GetRelativePath($target, $_.FullName).Replace('\', '/')
                bytes  = $_.Length
                sha256 = Get-Sha256 $_.FullName
            }
        })
    $bucketSummary = @(foreach ($bucket in $buckets) {
            $own = @($files | Where-Object { $_.path -like "rustfs/$bucket/*" })
            [ordered]@{ name = $bucket; objects = $own.Count; bytes = [long](($own | Measure-Object bytes -Sum).Sum) }
        })
    [ordered]@{
        format    = 1
        created   = (Get-Date).ToUniversalTime().ToString('o')
        project   = $Project
        databases = @($databases)
        buckets   = $bucketSummary
        files     = $files
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $target 'manifest.json')
}
catch {
    Write-Warning "Backup failed; the partial folder is left for inspection (no manifest.json): $target"
    throw
}
Write-Host "Backup complete: $target ($($files.Count) files, manifest.json written)."
Write-Host 'Reminder: .env (passwords, PASETO and MFA keys) is not part of this backup; keep it safe separately.'
