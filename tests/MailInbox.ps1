# Shared mail lookup for the Compose smoke tests. Dot-source it, then call Find-MailMessage.
#
# Two local mail catchers are supported on the same -MailDevUrl:
# - MailDev (this repo's `maildev` Compose profile) lists every message at an open `/email`.
# - aiyara-maildev lists messages at `/api/v1/emails` and requires a Bearer API token
#   (create one on its API Tokens page). A token selects aiyara-maildev.

function Resolve-MailDevToken([string]$Token, [string]$EnvFile = '.env') {
    if ($Token) { return $Token }
    if ($env:MAILDEV_API_TOKEN) { return $env:MAILDEV_API_TOKEN }
    if ($EnvFile -and (Test-Path -LiteralPath $EnvFile)) {
        $line = Get-Content -LiteralPath $EnvFile | Where-Object { $_ -match '^\s*MAILDEV_API_TOKEN\s*=' } |
            Select-Object -Last 1
        if ($line) { return ($line -split '=', 2)[1].Trim().Trim('"', "'") }
    }
    return ''
}

function Assert-MailDevShape([string]$MailDevUrl, $messages) {
    # A different local service can already own the MailDev host port. That wrong service
    # usually still answers HTTP 200, so without this check callers just retry until they
    # time out with a misleading "email was not delivered" - this fails fast instead.
    if ($messages.Count -gt 0 -and -not ($messages[0].PSObject.Properties.Name -contains 'subject')) {
        throw "MailDevUrl '$MailDevUrl' did not return MailDev's email list API (unexpected response shape). If it is aiyara-maildev, pass -MailDevToken or set MAILDEV_API_TOKEN; otherwise pass the correct -MailDevUrl."
    }
}

function Invoke-MailDevJson([string]$Uri, [hashtable]$Headers) {
    $response = Invoke-WebRequest -Uri $Uri -Headers $Headers -UseBasicParsing -SkipHttpErrorCheck
    if ($response.StatusCode -in 401, 403) {
        throw "aiyara-maildev rejected the API token ($($response.StatusCode)). Check -MailDevToken or MAILDEV_API_TOKEN."
    }
    if ($response.StatusCode -ne 200) { throw "GET $Uri returned $($response.StatusCode)." }
    try { return $response.Content | ConvertFrom-Json }
    catch { throw "GET $Uri did not return valid JSON. If this is aiyara-maildev, pass -MailDevToken or set MAILDEV_API_TOKEN; otherwise a different local service may own this host port. $($_.Exception.Message)" }
}

# Returns the newest message to $To whose subject matches the -like pattern, as
# @{ Subject; Text }, or $null when none has arrived yet.
function Find-MailMessage([string]$MailDevUrl, [string]$Token, [string]$To, [string]$SubjectLike) {
    if ($Token) {
        $headers = @{ Authorization = "Bearer $Token" }
        $page = Invoke-MailDevJson "$MailDevUrl/api/v1/emails?to=$([uri]::EscapeDataString($To))&pageSize=50" $headers
        if (-not ($page.PSObject.Properties.Name -contains 'items')) {
            throw "MailDevUrl '$MailDevUrl' did not return aiyara-maildev's email page (no 'items'). Check -MailDevUrl."
        }
        $summary = @($page.items) | Where-Object { $_.subject -like $SubjectLike } | Select-Object -First 1
        if (-not $summary) { return $null }
        $detail = Invoke-MailDevJson "$MailDevUrl/api/v1/emails/$($summary.id)" $headers
        return @{ Subject = $detail.subject; Text = [string]$detail.textBody }
    }

    $messages = @(Invoke-MailDevJson "$MailDevUrl/email" @{})
    Assert-MailDevShape $MailDevUrl $messages
    $message = $messages | Where-Object {
        $_.subject -like $SubjectLike -and $_.to[0].address -eq $To
    } | Select-Object -Last 1
    if (-not $message) { return $null }
    return @{ Subject = $message.subject; Text = [string]$message.text }
}
