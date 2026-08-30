[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DeviceAlias,
    [string]$Executable,
    [string]$LocalConfig,
    [string]$BaseUri = 'http://localhost:21990',
    [string]$ApiKey,
    [switch]$SkipProcessStart,
    [string]$AdbServerAlias,
    [ValidateSet('Local', 'Remote')]
    [string]$AdbServerMode,
    [string]$AdbServerHost,
    [ValidateRange(1, 65535)]
    [int]$AdbServerPort = 5037,
    [switch]$IncludeControls,
    [string]$ControlAppAlias = 'settings',
    [switch]$IncludeConnectionLifecycle,
    [switch]$IncludePackageAdministration,
    [string]$ArtifactAlias = 'acceptance-demo'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipProcessStart -and [string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $repositoryRoot 'artifacts\publish\win-x64\ADBMCPSharp.exe'
}
if ([string]::IsNullOrWhiteSpace($LocalConfig)) {
    $LocalConfig = Join-Path $repositoryRoot 'src\ADBMCPSharp\ADBMCPSharp.Local.json'
}

$resolvedExecutable = if ($SkipProcessStart) { $null } else { (Resolve-Path $Executable).Path }
$configuration = Get-Content -Raw (Resolve-Path $LocalConfig) | ConvertFrom-Json
$deviceProperty = $configuration.Adb.Devices.PSObject.Properties |
    Where-Object { $_.Name -eq $DeviceAlias } |
    Select-Object -First 1
if ($null -eq $deviceProperty) { throw "Unknown local device alias '$DeviceAlias'." }
$device = $deviceProperty.Value
$effectiveServerAlias = if ([string]::IsNullOrWhiteSpace($AdbServerAlias)) { [string]$device.Server } else { $AdbServerAlias }
if ($AdbServerMode -eq 'Remote' -and [string]::IsNullOrWhiteSpace($AdbServerHost)) {
    throw 'A remote ADB server override requires AdbServerHost.'
}
$diagnostics = @('Battery', 'Memory', 'Storage', 'CpuLoad', 'Runtime', 'Display', 'Security')
$environmentNames = New-Object System.Collections.Generic.List[string]

function Set-AcceptanceEnvironment([string]$Name, [string]$Value) {
    Set-Item -Path ('Env:' + $Name) -Value $Value
    $environmentNames.Add($Name)
}

function Get-SseJson($Response) {
    $dataLine = $Response.Content -split "`n" |
        Where-Object { $_ -match '^data:' } |
        Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($dataLine)) { throw 'MCP response contained no SSE data.' }
    return ($dataLine -replace '^data:\s*', '') | ConvertFrom-Json
}

$serviceProcess = $null
try {
    Set-AcceptanceEnvironment 'ADBMCP_Adb__ExecutablePath' ([string]$configuration.Adb.ExecutablePath)
    Set-AcceptanceEnvironment ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__Server') $effectiveServerAlias
    Set-AcceptanceEnvironment ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__Selector') ([string]$device.Selector)
    Set-AcceptanceEnvironment ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__DisplayName') ([string]$device.DisplayName)
    Set-AcceptanceEnvironment ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__Capabilities__AllowInstalledAppListing') 'true'
    Set-AcceptanceEnvironment ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__Capabilities__AllowDiagnostics') 'true'
    Set-AcceptanceEnvironment ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__Capabilities__AllowMediaInspection') 'true'
    Set-AcceptanceEnvironment 'ADBMCP_Policy__InstalledAppListingEnabled' 'true'
    Set-AcceptanceEnvironment 'ADBMCP_Policy__DiagnosticsEnabled' 'true'
    Set-AcceptanceEnvironment 'ADBMCP_Policy__MediaInspectionEnabled' 'true'
    Set-AcceptanceEnvironment 'ADBMCP_Policy__DiscoveryEnabled' ([string]$configuration.Policy.DiscoveryEnabled)
    if (-not [string]::IsNullOrWhiteSpace($AdbServerMode)) {
        $serverPrefix = 'ADBMCP_Adb__Servers__' + $effectiveServerAlias + '__'
        Set-AcceptanceEnvironment ($serverPrefix + 'Mode') $AdbServerMode
        Set-AcceptanceEnvironment ($serverPrefix + 'Port') ([string]$AdbServerPort)
        if ($AdbServerMode -eq 'Remote') {
            Set-AcceptanceEnvironment ($serverPrefix + 'Host') $AdbServerHost
        }
    }
    for ($index = 0; $index -lt $diagnostics.Count; $index++) {
        Set-AcceptanceEnvironment ('ADBMCP_Policy__AllowedDiagnostics__' + $index) $diagnostics[$index]
    }
    if ($IncludeControls) {
        foreach ($capability in @('AllowMediaControl', 'AllowVolumeControl', 'AllowPower', 'AllowNavigation', 'AllowAppLaunch', 'AllowAppStop')) {
            Set-AcceptanceEnvironment `
                ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__Capabilities__' + $capability) `
                ([string]$device.Capabilities.$capability)
        }
        foreach ($policyName in @('MediaControlEnabled', 'VolumeControlEnabled', 'PowerControlEnabled', 'NavigationControlEnabled', 'AppLaunchEnabled', 'AppStopEnabled')) {
            Set-AcceptanceEnvironment ('ADBMCP_Policy__' + $policyName) ([string]$configuration.Policy.$policyName)
        }
        $actionSets = @{
            AllowedNavigationActions = @($configuration.Policy.AllowedNavigationActions)
            AllowedMediaActions = @($configuration.Policy.AllowedMediaActions)
            AllowedVolumeActions = @($configuration.Policy.AllowedVolumeActions)
        }
        foreach ($actionSet in $actionSets.GetEnumerator()) {
            for ($index = 0; $index -lt $actionSet.Value.Count; $index++) {
                Set-AcceptanceEnvironment ('ADBMCP_Policy__' + $actionSet.Key + '__' + $index) ([string]$actionSet.Value[$index])
            }
        }
    }
    if ($IncludeConnectionLifecycle) {
        Set-AcceptanceEnvironment `
            ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__Capabilities__AllowConnectionManagement') `
            'true'
        Set-AcceptanceEnvironment 'ADBMCP_Policy__ConnectionManagementEnabled' 'true'
    }
    foreach ($app in $device.AllowedApps.PSObject.Properties) {
        $prefix = 'ADBMCP_Adb__Devices__' + $DeviceAlias + '__AllowedApps__' + $app.Name + '__'
        Set-AcceptanceEnvironment ($prefix + 'Package') ([string]$app.Value.Package)
        Set-AcceptanceEnvironment ($prefix + 'DisplayName') ([string]$app.Value.DisplayName)
        Set-AcceptanceEnvironment ($prefix + 'AllowUninstall') ([string]$app.Value.AllowUninstall)
    }
    if ($IncludePackageAdministration) {
        Set-AcceptanceEnvironment ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__Capabilities__AllowPackageInstall') `
            ([string]$device.Capabilities.AllowPackageInstall)
        Set-AcceptanceEnvironment ('ADBMCP_Adb__Devices__' + $DeviceAlias + '__Capabilities__AllowPackageUninstall') `
            ([string]$device.Capabilities.AllowPackageUninstall)
        Set-AcceptanceEnvironment 'ADBMCP_Policy__PackageInstallEnabled' ([string]$configuration.Policy.PackageInstallEnabled)
        Set-AcceptanceEnvironment 'ADBMCP_Policy__PackageUninstallEnabled' ([string]$configuration.Policy.PackageUninstallEnabled)
        $artifactProperty = $configuration.Adb.ApkArtifacts.PSObject.Properties |
            Where-Object { $_.Name -eq $ArtifactAlias } |
            Select-Object -First 1
        if ($null -eq $artifactProperty) { throw "Unknown local APK artifact alias '$ArtifactAlias'." }
        $artifact = $artifactProperty.Value
        $artifactPrefix = 'ADBMCP_Adb__ApkArtifacts__' + $ArtifactAlias + '__'
        Set-AcceptanceEnvironment ($artifactPrefix + 'Package') ([string]$artifact.Package)
        Set-AcceptanceEnvironment ($artifactPrefix + 'Source') ([string]$artifact.Source)
        Set-AcceptanceEnvironment ($artifactPrefix + 'Sha256') ([string]$artifact.Sha256)
        Set-AcceptanceEnvironment ($artifactPrefix + 'DisplayName') ([string]$artifact.DisplayName)
        Set-AcceptanceEnvironment ($artifactPrefix + 'AllowReplace') ([string]$artifact.AllowReplace)
        for ($index = 0; $index -lt $artifact.AllowedDevices.Count; $index++) {
            Set-AcceptanceEnvironment ($artifactPrefix + 'AllowedDevices__' + $index) ([string]$artifact.AllowedDevices[$index])
        }
    }

    if (-not $SkipProcessStart) {
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
        jsonrpc = '2.0'
        id = 1
        method = 'initialize'
        params = @{
            protocolVersion = '2025-06-18'
            capabilities = @{}
            clientInfo = @{ name = 'device-acceptance'; version = '1.0' }
        }
    } | ConvertTo-Json -Depth 6 -Compress
    $initializeResponse = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
        -Headers $headers -ContentType 'application/json' -Body $initialize -TimeoutSec 10
    $sessionHeaders = @{
        Accept = 'application/json, text/event-stream'
        'Mcp-Session-Id' = $initializeResponse.Headers['Mcp-Session-Id']
        'MCP-Protocol-Version' = '2025-06-18'
    }
    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $sessionHeaders.Authorization = 'Bearer ' + $ApiKey }
    $null = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
        -Headers $sessionHeaders -ContentType 'application/json' `
        -Body '{"jsonrpc":"2.0","method":"notifications/initialized"}' -TimeoutSec 10

    function Invoke-AcceptanceTool([int]$Id, [string]$Name, [hashtable]$Arguments) {
        $body = @{
            jsonrpc = '2.0'
            id = $Id
            method = 'tools/call'
            params = @{ name = $Name; arguments = $Arguments }
        } | ConvertTo-Json -Depth 8 -Compress
        $response = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
            -Headers $sessionHeaders -ContentType 'application/json' -Body $body -TimeoutSec 30
        $rpc = Get-SseJson $response
        if ($null -ne $rpc.error) { throw ('MCP error: ' + $rpc.error.message) }
        return $rpc.result.content[0].text | ConvertFrom-Json
    }

    $devices = Invoke-AcceptanceTool 2 'adb_list_devices' @{}
    $connectionHealth = Invoke-AcceptanceTool 3 'adb_get_connection_health' @{ deviceAlias = $DeviceAlias }
    $status = Invoke-AcceptanceTool 4 'adb_get_device_status' @{ deviceAlias = $DeviceAlias }
    $options = Invoke-AcceptanceTool 5 'adb_list_diagnostics' @{ deviceAlias = $DeviceAlias }
    $diagnosticResults = @()
    $requestId = 6
    foreach ($diagnostic in $diagnostics) {
        $result = Invoke-AcceptanceTool $requestId 'adb_run_diagnostic' @{
            deviceAlias = $DeviceAlias
            diagnostic = $diagnostic
        }
        $diagnosticResults += [pscustomobject]@{
            Name = $diagnostic
            State = $result.state
            HasData = $null -ne $result.data
        }
        $requestId++
    }
    $apps = Invoke-AcceptanceTool $requestId 'adb_list_installed_apps' @{
        deviceAlias = $DeviceAlias
        scope = 'User'
    }
    $requestId++
    $media = Invoke-AcceptanceTool $requestId 'adb_get_media_status' @{ deviceAlias = $DeviceAlias }
    $requestId++
    $servers = Invoke-AcceptanceTool $requestId 'adb_list_adb_servers' @{}
    $requestId++
    $discovery = Invoke-AcceptanceTool $requestId 'adb_discover_devices' @{ serverAlias = $effectiveServerAlias }
    $requestId++

    [pscustomobject]@{
        ConfiguredDeviceCount = @($devices).Count
        DeviceState = $status.state
        ConnectionState = $status.connectionState
        ConnectionHealthState = $connectionHealth.state
        ConnectionReachable = $connectionHealth.reachable
        ConnectionAuthorized = $connectionHealth.authorized
        DiagnosticsEnabledCount = @($options | Where-Object { $_.enabled }).Count
        UserAppCount = $apps.count
        AppListTruncated = $apps.truncated
        MediaState = $media.state
        RecognizedMediaSessions = @($media.sessions).Count
        ConfiguredServerCount = @($servers).Count
        DiscoveryState = $discovery.state
        MdnsAvailable = $discovery.mdnsAvailable
        AdvertisementCount = $discovery.advertisementCount
    }
    $diagnosticResults
    if ($IncludeConnectionLifecycle) {
        $connectionResults = @()
        $connect = Invoke-AcceptanceTool $requestId 'adb_connect_device' @{
            deviceAlias = $DeviceAlias
            confirmChange = $true
        }
        $requestId++
        $connectionResults += [pscustomobject]@{
            ConnectionOperation = 'Connect'
            State = $connect.state
            ConnectionState = $connect.connectionState
            Verified = $connect.verified
        }

        $reconnect = Invoke-AcceptanceTool $requestId 'adb_reconnect_device' @{
            deviceAlias = $DeviceAlias
            confirmChange = $true
        }
        $requestId++
        $connectionResults += [pscustomobject]@{
            ConnectionOperation = 'Reconnect'
            State = $reconnect.state
            ConnectionState = $reconnect.connectionState
            Verified = $reconnect.verified
        }

        $disconnect = Invoke-AcceptanceTool $requestId 'adb_disconnect_device' @{
            deviceAlias = $DeviceAlias
            confirmChange = $true
        }
        $requestId++
        $connectionResults += [pscustomobject]@{
            ConnectionOperation = 'Disconnect'
            State = $disconnect.state
            ConnectionState = $disconnect.connectionState
            Verified = $disconnect.verified
        }

        $restore = Invoke-AcceptanceTool $requestId 'adb_connect_device' @{
            deviceAlias = $DeviceAlias
            confirmChange = $true
        }
        $requestId++
        $connectionResults += [pscustomobject]@{
            ConnectionOperation = 'RestoreConnection'
            State = $restore.state
            ConnectionState = $restore.connectionState
            Verified = $restore.verified
        }

        $finalHealth = Invoke-AcceptanceTool $requestId 'adb_get_connection_health' @{ deviceAlias = $DeviceAlias }
        $requestId++
        if (-not $finalHealth.reachable -or -not $finalHealth.authorized -or $finalHealth.connectionState -ne 'online') {
            throw 'Connection lifecycle acceptance did not restore an online, authorized device.'
        }
        $connectionResults += [pscustomobject]@{
            ConnectionOperation = 'FinalHealth'
            State = $finalHealth.state
            ConnectionState = $finalHealth.connectionState
            Verified = $true
        }
        $connectionResults
    }
    if ($IncludeControls) {
        $controlResults = @()
        $wake = Invoke-AcceptanceTool $requestId 'adb_wake_device' @{ deviceAlias = $DeviceAlias }
        $requestId++
        $controlResults += [pscustomobject]@{ Control = 'Wake'; State = $wake.state; Verified = $wake.verified }
        $homeResult = Invoke-AcceptanceTool $requestId 'adb_send_navigation' @{ deviceAlias = $DeviceAlias; action = 'Home' }
        $requestId++
        $controlResults += [pscustomobject]@{ Control = 'Home'; State = $homeResult.state; Verified = $homeResult.verified }
        $launch = Invoke-AcceptanceTool $requestId 'adb_launch_app' @{ deviceAlias = $DeviceAlias; appAlias = $ControlAppAlias }
        $requestId++
        $controlResults += [pscustomobject]@{ Control = ('Launch:' + $ControlAppAlias); State = $launch.state; Verified = $launch.verified }
        $stop = Invoke-AcceptanceTool $requestId 'adb_stop_app' @{ deviceAlias = $DeviceAlias; appAlias = $ControlAppAlias }
        $requestId++
        $controlResults += [pscustomobject]@{ Control = ('Stop:' + $ControlAppAlias); State = $stop.state; Verified = $stop.verified }
        $restoreHome = Invoke-AcceptanceTool $requestId 'adb_send_navigation' @{ deviceAlias = $DeviceAlias; action = 'Home' }
        $requestId++
        $controlResults += [pscustomobject]@{ Control = 'RestoreHome'; State = $restoreHome.state; Verified = $restoreHome.verified }
        foreach ($mediaAction in @('Pause', 'Play')) {
            $result = Invoke-AcceptanceTool $requestId 'adb_send_media_action' @{ deviceAlias = $DeviceAlias; action = $mediaAction }
            $requestId++
            $controlResults += [pscustomobject]@{ Control = ('Media' + $mediaAction); State = $result.state; Verified = $result.verified }
        }
        foreach ($volumeAction in @('Down', 'Up')) {
            $result = Invoke-AcceptanceTool $requestId 'adb_send_volume_action' @{ deviceAlias = $DeviceAlias; action = $volumeAction }
            $requestId++
            $controlResults += [pscustomobject]@{ Control = ('Volume' + $volumeAction); State = $result.state; Verified = $result.verified }
        }
        $controlResults
    }
    if ($IncludePackageAdministration) {
        $availableArtifacts = Invoke-AcceptanceTool $requestId 'adb_list_installable_apks' @{ deviceAlias = $DeviceAlias }
        $requestId++
        $install = Invoke-AcceptanceTool $requestId 'adb_install_apk' @{
            deviceAlias = $DeviceAlias
            artifactAlias = $ArtifactAlias
            confirmChange = $true
        }
        $requestId++
        $installedStatus = Invoke-AcceptanceTool $requestId 'adb_get_app_status' @{
            deviceAlias = $DeviceAlias
            appAlias = $ArtifactAlias
        }
        $requestId++
        $uninstall = Invoke-AcceptanceTool $requestId 'adb_uninstall_app' @{
            deviceAlias = $DeviceAlias
            appAlias = $ArtifactAlias
            confirmChange = $true
        }
        $requestId++
        $removedStatus = Invoke-AcceptanceTool $requestId 'adb_get_app_status' @{
            deviceAlias = $DeviceAlias
            appAlias = $ArtifactAlias
        }
        [pscustomobject]@{
            PackageAcceptance = $ArtifactAlias
            ArtifactEnabled = @($availableArtifacts | Where-Object { $_.alias -eq $ArtifactAlias -and $_.allowedForDevice }).Count -eq 1
            InstallState = $install.state
            InstallVerified = $install.verified
            InstalledObserved = $installedStatus.installed
            UninstallState = $uninstall.state
            UninstallVerified = $uninstall.verified
            RemovedObserved = $removedStatus.installed -eq $false
        }
    }
}
finally {
    if ($null -ne $serviceProcess -and -not $serviceProcess.HasExited) {
        Stop-Process -Id $serviceProcess.Id -Force
    }
    foreach ($name in $environmentNames) { Remove-Item -Path ('Env:' + $name) -ErrorAction SilentlyContinue }
}
