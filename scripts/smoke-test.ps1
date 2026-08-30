[CmdletBinding()]
param(
    [string]$Executable,
    [string]$DiscoveryServerAlias,
    [string]$BaseUri = 'http://localhost:21990',
    [string]$ApiKey,
    [switch]$SkipProcessStart
)

$ErrorActionPreference = 'Stop'
if (-not $SkipProcessStart -and [string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\publish\win-x64\ADBMCPSharp.exe'
}
$serviceProcess = $null
if (-not $SkipProcessStart) {
    $resolvedExecutable = Resolve-Path $Executable
    $workingDirectory = Split-Path -Parent $resolvedExecutable
    $serviceProcess = Start-Process -FilePath $resolvedExecutable -WorkingDirectory $workingDirectory -PassThru -WindowStyle Hidden
}

try {
    $health = $null
    for ($attempt = 0; $attempt -lt 20 -and $null -eq $health; $attempt++) {
        try { $health = Invoke-RestMethod -Uri ($BaseUri + '/healthz') -TimeoutSec 1 }
        catch { Start-Sleep -Milliseconds 250 }
    }
    if ($null -eq $health) { throw 'Health endpoint did not become ready.' }
    Write-Output ('HEALTH=' + ($health | ConvertTo-Json -Compress))

    $headers = @{ Accept = 'application/json, text/event-stream' }
    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $headers.Authorization = 'Bearer ' + $ApiKey }
    $body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke-test","version":"1.0"}}}'
    $response = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
        -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 10
    Write-Output ('MCP_STATUS=' + $response.StatusCode)
    Write-Output ('MCP_CONTENT_TYPE=' + $response.Headers['Content-Type'])
    if ($response.Content -notmatch 'ADBMCPSharp') { throw 'MCP initialize response did not identify the server.' }

    $sessionId = $response.Headers['Mcp-Session-Id']
    if ([string]::IsNullOrWhiteSpace($sessionId)) { throw 'MCP initialize response did not include a session ID.' }
    $sessionHeaders = @{
        Accept = 'application/json, text/event-stream'
        'Mcp-Session-Id' = $sessionId
        'MCP-Protocol-Version' = '2025-06-18'
    }
    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $sessionHeaders.Authorization = 'Bearer ' + $ApiKey }
    $initializedBody = '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    $null = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
        -Headers $sessionHeaders -ContentType 'application/json' -Body $initializedBody -TimeoutSec 10

    $toolsBody = '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
    $toolsResponse = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
        -Headers $sessionHeaders -ContentType 'application/json' -Body $toolsBody -TimeoutSec 10
    $expectedTools = @(
        'adb_list_devices', 'adb_list_adb_servers', 'adb_discover_devices',
        'adb_get_device_status', 'adb_get_connection_health', 'adb_connect_device', 'adb_reconnect_device',
        'adb_disconnect_device', 'adb_list_allowed_apps', 'adb_get_app_status', 'adb_list_installed_apps',
        'adb_list_diagnostics', 'adb_run_diagnostic',
        'adb_get_media_status', 'adb_send_media_action', 'adb_send_volume_action',
        'adb_list_installable_apks', 'adb_install_apk', 'adb_uninstall_app', 'adb_execute_arbitrary_command',
        'adb_get_capabilities', 'adb_wake_device', 'adb_sleep_device', 'adb_send_navigation',
        'adb_launch_app', 'adb_stop_app'
    )
    foreach ($tool in $expectedTools) {
        if ($toolsResponse.Content -notmatch [Regex]::Escape($tool)) { throw "MCP tool catalogue is missing $tool." }
    }
    Write-Output ('MCP_TOOLS=' + $expectedTools.Count)

    if (-not [string]::IsNullOrWhiteSpace($DiscoveryServerAlias)) {
        $serverListBody = @{
            jsonrpc = '2.0'
            id = 3
            method = 'tools/call'
            params = @{ name = 'adb_list_adb_servers'; arguments = @{} }
        } | ConvertTo-Json -Depth 5 -Compress
        $serverListResponse = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
            -Headers $sessionHeaders -ContentType 'application/json' -Body $serverListBody -TimeoutSec 10

        $discoveryBody = @{
            jsonrpc = '2.0'
            id = 4
            method = 'tools/call'
            params = @{ name = 'adb_discover_devices'; arguments = @{ serverAlias = $DiscoveryServerAlias } }
        } | ConvertTo-Json -Depth 5 -Compress
        $discoveryResponse = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
            -Headers $sessionHeaders -ContentType 'application/json' -Body $discoveryBody -TimeoutSec 20

        $serverDataLine = @($serverListResponse.Content -split "`n" | Where-Object { $_ -like 'data: *' })[-1]
        $discoveryDataLine = @($discoveryResponse.Content -split "`n" | Where-Object { $_ -like 'data: *' })[-1]
        $serverData = $serverDataLine.Substring(6) | ConvertFrom-Json
        $discoveryData = $discoveryDataLine.Substring(6) | ConvertFrom-Json
        Write-Output ('ADB_SERVERS=' + $serverData.result.content[0].text)
        Write-Output ('ADB_DISCOVERY=' + $discoveryData.result.content[0].text)
    }
}
finally {
    if ($null -ne $serviceProcess -and -not $serviceProcess.HasExited) {
        Stop-Process -Id $serviceProcess.Id -Force
        $serviceProcess.WaitForExit()
    }
}
