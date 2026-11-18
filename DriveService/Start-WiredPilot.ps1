param(
    [string]$OAuthJson,
    [string]$AdbPath
)

$ErrorActionPreference = 'Stop'
$pilotOrigin = 'http://127.0.0.1:48180'
$callback = "$pilotOrigin/oauth/callback"
$pilotConfig = Join-Path $PSScriptRoot '.env.pilot'
$port = 'tcp:48180'

if ($OAuthJson) {
    try {
        $client = (Get-Content -LiteralPath $OAuthJson -Raw | ConvertFrom-Json).web
    }
    catch {
        throw 'Could not read the downloaded Google Web application OAuth JSON.'
    }
    if ($client.client_id -notmatch '^[A-Za-z0-9._-]+\.apps\.googleusercontent\.com$' -or
        $client.client_secret -notmatch '^[A-Za-z0-9._-]+$' -or $client.redirect_uris -cnotcontains $callback) {
        throw "Download a Google Web application OAuth JSON with $callback registered as a redirect URI."
    }

    if (Test-Path -LiteralPath $pilotConfig) {
        $oldConfig = Get-Content -LiteralPath $pilotConfig -Raw
        $key = [regex]::Match($oldConfig, '(?m)^STORAGE_KEY=([A-Za-z0-9+/]{43}=)\r?$').Groups[1].Value
        $oldClientId = [regex]::Match($oldConfig, '(?m)^GOOGLE_CLIENT_ID=([^\r\n]+)').Groups[1].Value
        if (!$key -or $oldClientId -ne $client.client_id) {
            throw 'The saved pilot uses a different client or is incomplete. Keep its database and key together.'
        }
    } else {
        $bytes = New-Object byte[] 32
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($bytes) }
        finally { $random.Dispose() }
        $key = [Convert]::ToBase64String($bytes)
    }

    $settings = @(
        "PUBLIC_ORIGIN=$pilotOrigin"
        "GOOGLE_CLIENT_ID=$($client.client_id)"
        "GOOGLE_CLIENT_SECRET=$($client.client_secret)"
        "STORAGE_KEY=$key"
        'DATABASE_PATH=./data/pilot.sqlite'
        'HOST=127.0.0.1'
        'PORT=48180'
        'TRUST_PROXY=0'
    )
    [IO.File]::WriteAllLines($pilotConfig, [string[]]$settings)
}

if (!(Test-Path -LiteralPath $pilotConfig)) {
    throw 'Pass -OAuthJson with the downloaded Google Web application credential on the first run.'
}

$node = Get-Command node.exe -ErrorAction SilentlyContinue
$npm = Get-Command npm.cmd -ErrorAction SilentlyContinue
if (!$node -or !$npm) { throw 'Install Node.js 22.13 or later and reopen PowerShell.' }
$nodeVersion = & $node.Source --version
if ($LASTEXITCODE -ne 0 -or [version]$nodeVersion.TrimStart('v') -lt [version]'22.13') {
    throw 'Install Node.js 22.13 or later.'
}

if (!$AdbPath) {
    $AdbPath = (Get-Command adb.exe -ErrorAction SilentlyContinue).Source
}
if (!$AdbPath) {
    $versionFile = Join-Path $PSScriptRoot '../Unity/ProjectSettings/ProjectVersion.txt'
    $unityVersion = [regex]::Match((Get-Content -LiteralPath $versionFile -Raw), '(?m)^m_EditorVersion: (\S+)').Groups[1].Value
    $AdbPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$unityVersion/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
}
if (!(Test-Path -LiteralPath $AdbPath -PathType Leaf)) {
    throw 'Pass -AdbPath with the adb.exe from Android SDK platform-tools.'
}
$AdbPath = (Resolve-Path -LiteralPath $AdbPath).Path

Push-Location $PSScriptRoot
$createdReverse = $false
try {
    if (!(Test-Path -LiteralPath 'node_modules/qrcode/package.json')) {
        & $npm.Source ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Could not install DVR companion dependencies.' }
    }

    $reverses = & $AdbPath -d reverse --list
    if ($LASTEXITCODE -ne 0) { throw 'Connect one Quest by USB and approve USB debugging on the headset.' }
    if (!($reverses -match "\s${port}\s+${port}\s*$")) {
        & $AdbPath -d reverse --no-rebind $port $port
        if ($LASTEXITCODE -ne 0) { throw 'Could not open the USB connection on port 48180.' }
        $createdReverse = $true
    }

    $running = $null
    try {
        $running = Invoke-RestMethod -Uri "$pilotOrigin/health" -TimeoutSec 2 -ErrorAction Stop
    } catch {
        # The first launch starts the service below.
    }
    if ($running.ok -and $running.mode -eq 'wired-pilot') {
        $createdReverse = $false
        Write-Output "DVR companion is connected over USB. Pair at $pilotOrigin/pair"
        return
    }
    & $node.Source "--env-file=$pilotConfig" server.mjs --wired-pilot
    if ($LASTEXITCODE -ne 0) { throw 'The DVR companion did not start. Check whether port 48180 is available.' }
}
finally {
    if ($createdReverse) { & $AdbPath -d reverse --remove $port | Out-Null }
    Pop-Location
}
