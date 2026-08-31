[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')]
    [string]$DeviceAlias,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')]
    [string]$AppAlias,
    [ValidateSet('Start', 'Foreground', 'WakeAndForeground')]
    [string]$Mode = 'WakeAndForeground',
    [switch]$StopAfterVerification,
    [string]$Executable,
    [string]$LocalConfig,
    [string]$BaseUri = 'http://localhost:21990',
    [string]$ApiKey,
    [switch]$SkipProcessStart
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $repoRoot 'artifacts\publish\win-x64\ADBMCPSharp.exe'
}
if ([string]::IsNullOrWhiteSpace($LocalConfig)) {
    $LocalConfig = Join-Path $repoRoot 'src\ADBMCPSharp\ADBMCPSharp.Local.json'
}

$configuration = Get-Content -Raw (Resolve-Path $LocalConfig) | ConvertFrom-Json
$deviceProperty = $configuration.Adb.Devices.PSObject.Properties |
    Where-Object { $_.Name -eq $DeviceAlias } | Select-Object -First 1
if ($null -eq $deviceProperty) { throw "Unknown local device alias '$DeviceAlias'." }
$device = $deviceProperty.Value
$appProperty = $device.AllowedApps.PSObject.Properties |
    Where-Object { $_.Name -eq $AppAlias } | Select-Object -First 1
if ($null -eq $appProperty) { throw "Unknown allowlisted application alias '$AppAlias'." }
$app = $appProperty.Value
$serverAlias = [string]$device.Server
$serverProperty = if ($null -ne $configuration.Adb.Servers) {
    $configuration.Adb.Servers.PSObject.Properties |
        Where-Object { $_.Name -eq $serverAlias } | Select-Object -First 1
} else { $null }
if ($null -eq $serverProperty -and $serverAlias -ne 'local') {
    throw "Unknown configured ADB server alias '$serverAlias'."
}
$server = if ($null -eq $serverProperty) { $null } else { $serverProperty.Value }

$resolvedExecutable = if ($SkipProcessStart) { $null } else { (Resolve-Path $Executable).Path }
$environmentNames = New-Object System.Collections.Generic.List[string]
$serviceProcess = $null

function Set-AcceptanceEnvironment([string]$Name, [string]$Value) {
    Set-Item -Path ('Env:' + $Name) -Value $Value
    $environmentNames.Add($Name)
}

function Get-SseJson($Response) {
    $dataLine = $Response.Content -split "`n" |
        Where-Object { $_ -match '^data:' } | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($dataLine)) { throw 'MCP response contained no SSE data.' }
    return ($dataLine -replace '^data:\s*', '') | ConvertFrom-Json
}

try {
    Set-AcceptanceEnvironment 'ADBMCP_Adb__ExecutablePath' ([string]$configuration.Adb.ExecutablePath)
    if ($null -ne $server) {
        $serverPrefix = 'ADBMCP_Adb__Servers__' + $serverAlias + '__'
        Set-AcceptanceEnvironment ($serverPrefix + 'Mode') ([string]$server.Mode)
        Set-AcceptanceEnvironment ($serverPrefix + 'Port') ([string]$server.Port)
        if (-not [string]::IsNullOrWhiteSpace([string]$server.Host)) {
            Set-AcceptanceEnvironment ($serverPrefix + 'Host') ([string]$server.Host)
        }
    }

    $devicePrefix = 'ADBMCP_Adb__Devices__' + $DeviceAlias + '__'
    Set-AcceptanceEnvironment ($devicePrefix + 'Server') $serverAlias
    Set-AcceptanceEnvironment ($devicePrefix + 'Selector') ([string]$device.Selector)
    Set-AcceptanceEnvironment ($devicePrefix + 'DisplayName') ([string]$device.DisplayName)
    Set-AcceptanceEnvironment ($devicePrefix + 'Capabilities__AllowAppLaunch') `
        ([string]$device.Capabilities.AllowAppLaunch)
    Set-AcceptanceEnvironment ($devicePrefix + 'Capabilities__AllowPower') `
        ([string]$device.Capabilities.AllowPower)
    Set-AcceptanceEnvironment ($devicePrefix + 'Capabilities__AllowAppStop') `
        ([string]$device.Capabilities.AllowAppStop)
    $appPrefix = $devicePrefix + 'AllowedApps__' + $AppAlias + '__'
    Set-AcceptanceEnvironment ($appPrefix + 'Package') ([string]$app.Package)
    Set-AcceptanceEnvironment ($appPrefix + 'DisplayName') ([string]$app.DisplayName)
    Set-AcceptanceEnvironment 'ADBMCP_Policy__AppLaunchEnabled' ([string]$configuration.Policy.AppLaunchEnabled)
    Set-AcceptanceEnvironment 'ADBMCP_Policy__PowerControlEnabled' ([string]$configuration.Policy.PowerControlEnabled)
    Set-AcceptanceEnvironment 'ADBMCP_Policy__AppStopEnabled' ([string]$configuration.Policy.AppStopEnabled)

    if (-not $SkipProcessStart) {
        $uri = [Uri]$BaseUri
        if ($null -ne (Get-NetTCPConnection -State Listen -LocalPort $uri.Port -ErrorAction SilentlyContinue)) {
            throw "Port $($uri.Port) is already in use."
        }
        $serviceProcess = Start-Process -FilePath $resolvedExecutable `
            -WorkingDirectory (Split-Path -Parent $resolvedExecutable) -PassThru -WindowStyle Hidden
    }

    $health = $null
    for ($attempt = 0; $attempt -lt 20 -and $null -eq $health; $attempt++) {
        try { $health = Invoke-RestMethod -Uri ($BaseUri + '/healthz') -TimeoutSec 1 }
        catch { Start-Sleep -Milliseconds 250 }
    }
    if ($null -eq $health) { throw 'Health endpoint did not become ready.' }

    $headers = @{ Accept = 'application/json, text/event-stream' }
    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $headers.Authorization = 'Bearer ' + $ApiKey }
    $initialize = @{
        jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{
            protocolVersion = '2025-06-18'; capabilities = @{}
            clientInfo = @{ name = 'app-control-acceptance'; version = '1.0' }
        }
    } | ConvertTo-Json -Depth 6 -Compress
    $response = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
        -Headers $headers -ContentType 'application/json' -Body $initialize -TimeoutSec 10
    $sessionId = [string]@($response.Headers['Mcp-Session-Id'])[0]
    if ([string]::IsNullOrWhiteSpace($sessionId)) { throw 'MCP initialize response did not include a session ID.' }
    $sessionHeaders = @{
        Accept = 'application/json, text/event-stream'
        'Mcp-Session-Id' = $sessionId
        'MCP-Protocol-Version' = '2025-06-18'
    }
    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
        $sessionHeaders.Authorization = 'Bearer ' + $ApiKey
    }
    $null = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
        -Headers $sessionHeaders -ContentType 'application/json' `
        -Body '{"jsonrpc":"2.0","method":"notifications/initialized"}' -TimeoutSec 10

    function Invoke-AcceptanceTool([int]$Id, [string]$Name, [hashtable]$Arguments) {
        $body = @{
            jsonrpc = '2.0'; id = $Id; method = 'tools/call'
            params = @{ name = $Name; arguments = $Arguments }
        } | ConvertTo-Json -Depth 8 -Compress
        $toolResponse = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
            -Headers $sessionHeaders -ContentType 'application/json' -Body $body -TimeoutSec 30
        $rpc = Get-SseJson $toolResponse
        if ($null -ne $rpc.error) { throw ('MCP error: ' + $rpc.error.message) }
        return $rpc.result.content[0].text | ConvertFrom-Json
    }

    $connection = Invoke-AcceptanceTool 2 'adb_get_connection_health' @{ deviceAlias = $DeviceAlias }
    if (-not $connection.reachable -or -not $connection.authorized) {
        throw "The configured device is not ready: $($connection.connectionState)."
    }
    $launch = Invoke-AcceptanceTool 3 'adb_launch_app' @{
        deviceAlias = $DeviceAlias; appAlias = $AppAlias; mode = $Mode
    }
    $status = $null
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        $status = Invoke-AcceptanceTool (4 + $attempt) 'adb_get_app_status' @{
            deviceAlias = $DeviceAlias; appAlias = $AppAlias
        }
        $observed = $status.installed -eq $true -and $status.running -eq $true
        if ($Mode -ne 'Start') { $observed = $observed -and $status.foreground -eq $true }
        if ($observed) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($launch.state -notin @('Accepted', 'ObservedComplete')) {
        throw "Application launch failed with state '$($launch.state)'."
    }
    if ($status.installed -ne $true -or $status.running -ne $true) {
        throw 'The application was not observed as installed and running.'
    }
    if ($Mode -ne 'Start' -and ($launch.verified -ne $true -or $status.foreground -ne $true)) {
        throw 'The application was not verified in the foreground.'
    }

    $stopState = $null
    $stopVerified = $null
    if ($StopAfterVerification) {
        $stop = Invoke-AcceptanceTool 20 'adb_stop_app' @{
            deviceAlias = $DeviceAlias; appAlias = $AppAlias
        }
        $stopState = $stop.state
        $stopVerified = $stop.verified
        if ($stop.state -ne 'ObservedComplete' -or $stop.verified -ne $true) {
            throw "Application stop was not verified; state '$($stop.state)'."
        }
    }

    [pscustomobject]@{
        DeviceAlias = $DeviceAlias
        AppAlias = $AppAlias
        Mode = $Mode
        ConnectionState = $connection.connectionState
        LaunchState = $launch.state
        LaunchVerified = $launch.verified
        Installed = $status.installed
        Running = $status.running
        Foreground = $status.foreground
        StopRequested = [bool]$StopAfterVerification
        StopState = $stopState
        StopVerified = $stopVerified
    }
}
finally {
    if ($null -ne $serviceProcess -and -not $serviceProcess.HasExited) {
        Stop-Process -Id $serviceProcess.Id -Force
        $null = $serviceProcess.WaitForExit(5000)
    }
    foreach ($name in $environmentNames) {
        Remove-Item -Path ('Env:' + $name) -ErrorAction SilentlyContinue
    }
}
