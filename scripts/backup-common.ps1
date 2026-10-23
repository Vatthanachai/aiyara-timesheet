# Shared helpers for backup-main.ps1 and restore-main.ps1 (dot-sourced; not run on its own).
# Everything is found through Docker labels of the Compose project, so no .env, compose file, or
# secret is read from disk: credentials come from the running containers' own environment.

function Invoke-Native([string]$Description, [scriptblock]$Command) {
    $output = & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
    $output
}

function Get-ServiceContainer([string]$Project, [string]$Service) {
    $id = @(& docker ps -aq --no-trunc --filter "label=com.docker.compose.project=$Project" `
            --filter "label=com.docker.compose.service=$Service") | Select-Object -First 1
    if (-not $id) { throw "No '$Service' container found for Compose project '$Project'. Is the stack up?" }
    $running = (& docker inspect -f '{{.State.Running}}' $id).Trim()
    if ($running -ne 'true') { throw "Container '$Service' of project '$Project' is not running." }
    $id
}

function Get-ContainerEnv([string]$Container, [string]$Name) {
    $value = Invoke-Native "Reading $Name from the container" { & docker exec $Container printenv $Name }
    ($value | Out-String).Trim()
}

function Get-DatabaseNames([string]$Container, [string]$PgUser) {
    $sql = "SELECT datname FROM pg_database WHERE NOT datistemplate AND datname <> 'postgres' ORDER BY 1"
    @(Invoke-Native 'Listing databases' { & docker exec $Container psql -U $PgUser -d postgres -At -c $sql } |
        Where-Object { $_ })
}

# Throwaway mc (MinIO client) container on the stack's network, with the S3 credentials passed
# through the environment (not the command line) and a host directory mounted at /backup.
# Returns the container's output lines.
function Invoke-Mc([string]$RustfsContainer, [string]$HostDir, [string[]]$McArguments) {
    $network = (& docker inspect -f '{{range $name, $net := .NetworkSettings.Networks}}{{$name}}{{end}}' $RustfsContainer).Trim()
    $access = Get-ContainerEnv $RustfsContainer 'RUSTFS_ACCESS_KEY'
    $secret = Get-ContainerEnv $RustfsContainer 'RUSTFS_SECRET_KEY'
    $env:MC_HOST_src = "http://${access}:${secret}@rustfs:9000"
    try {
        $run = @('run', '--rm', '--network', $network, '-e', 'MC_HOST_src', '-v', "${HostDir}:/backup", '--entrypoint', 'mc', $McImage) + $McArguments
        $output = & docker @run
        if ($LASTEXITCODE -ne 0) { throw "mc $($McArguments -join ' ') failed with exit code $LASTEXITCODE." }
        $output
    }
    finally { Remove-Item Env:MC_HOST_src -ErrorAction SilentlyContinue }
}

function Get-BucketNames([string]$RustfsContainer, [string]$HostDir) {
    $lines = Invoke-Mc $RustfsContainer $HostDir @('--json', 'ls', 'src')
    @($lines | ForEach-Object { try { ($_ | ConvertFrom-Json).key } catch { $null } } |
        Where-Object { $_ } | ForEach-Object { $_.TrimEnd('/') })
}

function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
