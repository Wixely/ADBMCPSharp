[CmdletBinding()]
param(
    [string]$ImageName = 'adbmcp-sharp:smoke',
    [ValidateRange(1024, 65535)]
    [int]$Port = 21992,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
if ($null -eq (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker CLI is required for the remote-topology smoke test.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$suffix = [Guid]::NewGuid().ToString('N')
$networkName = 'adbmcp-topology-' + $suffix
$ingressNetworkName = 'adbmcp-ingress-' + $suffix
$keyVolumeName = 'adbmcp-keys-' + $suffix
$adbContainerName = 'adbmcp-adb-' + $suffix
$serviceContainerName = 'adbmcp-service-' + $suffix
$apiKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$environmentFile = [IO.Path]::GetTempFileName()
$networkCreated = $false
$ingressNetworkCreated = $false
$volumeCreated = $false
$adbStarted = $false
$serviceStarted = $false

function Get-SseJson($Response) {
    $line = @($Response.Content -split "`n" | Where-Object { $_ -like 'data: *' })[-1]
    if ([string]::IsNullOrWhiteSpace($line)) { throw 'MCP response contained no SSE data.' }
    return $line.Substring(6) | ConvertFrom-Json
}

try {
    if (-not $SkipBuild) {
        & docker build --file (Join-Path $repositoryRoot 'deploy\Dockerfile') --tag $ImageName $repositoryRoot
        if ($LASTEXITCODE -ne 0) { throw "Docker image build failed with exit code $LASTEXITCODE." }
    }

    & docker network create --internal $networkName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Private Docker network creation failed.' }
    $networkCreated = $true
    & docker network create $ingressNetworkName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Ingress Docker network creation failed.' }
    $ingressNetworkCreated = $true
    & docker volume create $keyVolumeName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'ADB key volume creation failed.' }
    $volumeCreated = $true

    & docker run --rm --volume ($keyVolumeName + ':/var/lib/adbmcp/adb') `
        --entrypoint adb $ImageName keygen /var/lib/adbmcp/adb/adbkey | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'ADB key generation in the persistent volume failed.' }

    & docker run --detach --rm --name $adbContainerName `
        --network $networkName --network-alias adb-server `
        --volume ($keyVolumeName + ':/var/lib/adbmcp/adb') `
        --entrypoint adb $ImageName -a -P 5037 server nodaemon | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'ADB server container start failed.' }
    $adbStarted = $true

    $adbReady = $false
    for ($attempt = 0; $attempt -lt 20 -and -not $adbReady; $attempt++) {
        & docker exec $adbContainerName adb -H 127.0.0.1 -P 5037 devices *> $null
        if ($LASTEXITCODE -eq 0) { $adbReady = $true } else { Start-Sleep -Milliseconds 250 }
    }
    if (-not $adbReady) { throw 'ADB server container did not become ready.' }
    $fingerprintBefore = @(& docker exec $adbContainerName sha256sum /var/lib/adbmcp/adb/adbkey)[0].Split(' ')[0]

    $settings = [string[]]@(
        ('Server__ApiKey=' + $apiKey),
        'Adb__Servers__topology__Mode=Remote',
        'Adb__Servers__topology__Host=adb-server',
        'Adb__Servers__topology__Port=5037',
        'Adb__Devices__topology-device__Server=topology',
        'Adb__Devices__topology-device__Selector=test.invalid:5555',
        'Adb__Devices__topology-device__DisplayName=Topology device',
        'Adb__Devices__topology-device__Capabilities__Enabled=true',
        'Policy__InspectionEnabled=true'
    )
    if ($settings.Count -ne 9 -or $settings[0] -ne ('Server__ApiKey=' + $apiKey)) {
        throw 'Docker environment settings were not constructed as separate entries.'
    }
    [IO.File]::WriteAllLines($environmentFile, $settings)
    & docker run --detach --rm --name $serviceContainerName `
        --network $ingressNetworkName `
        --publish ('127.0.0.1:' + $Port + ':5719') `
        --env-file $environmentFile `
        $ImageName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'ADBMCPSharp topology container start failed.' }
    $serviceStarted = $true
    & docker network connect $networkName $serviceContainerName
    if ($LASTEXITCODE -ne 0) { throw 'ADBMCPSharp could not join the private ADB network.' }

    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -SkipProcessStart `
        -BaseUri ('http://localhost:' + $Port) -ApiKey $apiKey

    $headers = @{ Accept='application/json, text/event-stream'; Authorization=('Bearer ' + $apiKey) }
    $initialize = @{jsonrpc='2.0';id=1;method='initialize';params=@{
        protocolVersion='2025-06-18';capabilities=@{};clientInfo=@{name='remote-topology-test';version='1.0'}
    }} | ConvertTo-Json -Depth 6 -Compress
    $response = Invoke-WebRequest -UseBasicParsing -Uri ('http://localhost:' + $Port + '/mcp') `
        -Method Post -Headers $headers -ContentType 'application/json' -Body $initialize
    $sessionIdValues = @($response.Headers['Mcp-Session-Id'])
    $sessionId = [string]$sessionIdValues[0]
    if ([string]::IsNullOrWhiteSpace($sessionId)) { throw 'MCP initialize response did not include a session ID.' }
    $session = @{
        Accept='application/json, text/event-stream'; Authorization=('Bearer ' + $apiKey)
        'Mcp-Session-Id'=$sessionId; 'MCP-Protocol-Version'='2025-06-18'
    }
    $null = Invoke-WebRequest -UseBasicParsing -Uri ('http://localhost:' + $Port + '/mcp') `
        -Method Post -Headers $session -ContentType 'application/json' `
        -Body '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    $call = @{jsonrpc='2.0';id=2;method='tools/call';params=@{
        name='adb_get_connection_health';arguments=@{deviceAlias='topology-device'}
    }} | ConvertTo-Json -Depth 6 -Compress
    $response = Invoke-WebRequest -UseBasicParsing -Uri ('http://localhost:' + $Port + '/mcp') `
        -Method Post -Headers $session -ContentType 'application/json' -Body $call
    $rpc = Get-SseJson $response
    $health = $rpc.result.content[0].text | ConvertFrom-Json
    if ($health.serverMode -ne 'Remote' -or $health.connectionState -ne 'unavailable') {
        throw 'MCP did not traverse the remote ADB server topology as expected.'
    }

    $serviceUid = & docker exec $serviceContainerName id -u
    $adbUid = & docker exec $adbContainerName id -u
    & docker run --rm --volume ($keyVolumeName + ':/keys') --entrypoint sh $ImageName `
        -c 'test -s /keys/adbkey && test -s /keys/adbkey.pub'
    if ($LASTEXITCODE -ne 0) { throw 'Persistent ADB key files were not observed.' }
    if ($serviceUid -eq '0' -or $adbUid -eq '0') { throw 'A topology container ran as root.' }

    & docker stop --time 10 $adbContainerName | Out-Null
    $adbStarted = $false
    & docker run --detach --rm --name $adbContainerName `
        --network $networkName --network-alias adb-server `
        --volume ($keyVolumeName + ':/var/lib/adbmcp/adb') `
        --entrypoint adb $ImageName -a -P 5037 server nodaemon | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Replacement ADB server container start failed.' }
    $adbStarted = $true
    $replacementReady = $false
    for ($attempt = 0; $attempt -lt 20 -and -not $replacementReady; $attempt++) {
        & docker exec $adbContainerName adb -H 127.0.0.1 -P 5037 devices *> $null
        if ($LASTEXITCODE -eq 0) { $replacementReady = $true } else { Start-Sleep -Milliseconds 250 }
    }
    if (-not $replacementReady) { throw 'Replacement ADB server container did not become ready.' }
    $fingerprintAfter = @(& docker exec $adbContainerName sha256sum /var/lib/adbmcp/adb/adbkey)[0].Split(' ')[0]
    if ($fingerprintBefore -ne $fingerprintAfter) { throw 'Persisted ADB key changed after server replacement.' }

    Write-Output 'REMOTE_TOPOLOGY=passed'
    Write-Output ('SERVICE_UID=' + $serviceUid)
    Write-Output ('ADB_SERVER_UID=' + $adbUid)
    Write-Output ('REMOTE_HEALTH_STATE=' + $health.state)
    Write-Output 'PERSISTENT_ADB_KEY=stable'
}
finally {
    if ($serviceStarted) { & docker stop --time 10 $serviceContainerName | Out-Null }
    if ($adbStarted) { & docker stop --time 10 $adbContainerName | Out-Null }
    if ($networkCreated) { & docker network rm $networkName | Out-Null }
    if ($ingressNetworkCreated) { & docker network rm $ingressNetworkName | Out-Null }
    if ($volumeCreated) { & docker volume rm $keyVolumeName | Out-Null }
    Remove-Item -LiteralPath $environmentFile -Force -ErrorAction SilentlyContinue
}
