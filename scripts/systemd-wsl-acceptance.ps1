[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$Distribution,
    [string]$Archive,
    [string]$BaseUri = 'http://localhost:21990'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Archive)) {
    [xml]$buildProperties = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
    $version = [string]$buildProperties.Project.PropertyGroup.VersionPrefix
    $Archive = Join-Path $repoRoot ("artifacts\release\ADBMCPSharp-$version-linux-x64.tar.gz")
}
$resolvedArchive = (Resolve-Path $Archive).Path
$wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
if ($null -eq $wsl) { throw 'WSL is required for this systemd acceptance harness.' }

$serviceName = 'adbmcp.service'
$installDirectory = '/opt/adbmcp'
$configurationDirectory = '/etc/adbmcp'
$unitPath = '/etc/systemd/system/adbmcp.service'
$accountName = 'adbmcp'
$accountCreated = $false
$installCreated = $false
$configurationCreated = $false
$unitInstalled = $false
$unitEnabled = $false
$temporaryEnvironmentFile = $null
$apiKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')

function Invoke-WslCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,
        [switch]$AllowFailure
    )

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $wsl.Source -d $Distribution -u root --exec @ArgumentList 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "WSL command '$($ArgumentList[0])' failed with exit code $exitCode."
    }
    return $output
}

function Test-WslPath([string]$Path) {
    & $wsl.Source -d $Distribution -u root --exec /usr/bin/test -e $Path 2>$null
    return $LASTEXITCODE -eq 0
}

function Get-UnitProperty([string]$Property) {
    return [string](@(Invoke-WslCommand @(
        '/usr/bin/systemctl', 'show', $serviceName, '--property', $Property, '--value'
    ))[0]).Trim()
}

try {
    $systemdState = [string](@(Invoke-WslCommand @(
        '/usr/bin/systemctl', 'is-system-running'
    ))[0]).Trim()
    if ($systemdState -ne 'running') { throw "The selected distribution's systemd state is '$systemdState'." }

    if (Test-WslPath $installDirectory) { throw "$installDirectory already exists; refusing to overwrite it." }
    if (Test-WslPath $configurationDirectory) { throw "$configurationDirectory already exists; refusing to overwrite it." }
    if (Test-WslPath $unitPath) { throw "$unitPath already exists; refusing to overwrite it." }
    & $wsl.Source -d $Distribution -u root --exec /usr/bin/getent passwd $accountName *> $null
    if ($LASTEXITCODE -eq 0) { throw "The $accountName account already exists; refusing to reuse it." }
    if ($null -ne (Get-NetTCPConnection -State Listen -LocalPort 21990 -ErrorAction SilentlyContinue)) {
        throw 'Port 21990 is already in use on the Windows host.'
    }

    $linuxArchive = [string](@(Invoke-WslCommand @(
        '/usr/bin/wslpath', '-a', $resolvedArchive
    ))[0]).Trim()
    if ([string]::IsNullOrWhiteSpace($linuxArchive)) { throw 'WSL could not translate the archive path.' }
    $temporaryEnvironmentFile = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllText(
        $temporaryEnvironmentFile,
        'Server__ApiKey=' + $apiKey + [Environment]::NewLine,
        (New-Object Text.UTF8Encoding($false)))
    $linuxEnvironmentFile = [string](@(Invoke-WslCommand @(
        '/usr/bin/wslpath', '-a', $temporaryEnvironmentFile
    ))[0]).Trim()
    if ([string]::IsNullOrWhiteSpace($linuxEnvironmentFile)) {
        throw 'WSL could not translate the temporary environment path.'
    }

    Invoke-WslCommand @(
        '/usr/sbin/useradd', '--system', '--home-dir', '/nonexistent', '--shell', '/usr/sbin/nologin', $accountName
    ) | Out-Null
    $accountCreated = $true
    Invoke-WslCommand @('/usr/bin/mkdir', '-p', $installDirectory, "$installDirectory/logs") | Out-Null
    $installCreated = $true
    Invoke-WslCommand @('/usr/bin/mkdir', '-p', $configurationDirectory) | Out-Null
    $configurationCreated = $true
    Invoke-WslCommand @('/usr/bin/cp', $linuxEnvironmentFile, "$configurationDirectory/environment") | Out-Null
    Invoke-WslCommand @('/usr/bin/chown', 'root:root', "$configurationDirectory/environment") | Out-Null
    Invoke-WslCommand @('/usr/bin/chmod', '0600', "$configurationDirectory/environment") | Out-Null
    Invoke-WslCommand @(
        '/usr/bin/tar', '-xzf', $linuxArchive, '-C', $installDirectory, '--strip-components=1'
    ) | Out-Null
    Invoke-WslCommand @('/usr/bin/chown', '-R', 'root:root', $installDirectory) | Out-Null
    Invoke-WslCommand @('/usr/bin/chown', 'adbmcp:adbmcp', "$installDirectory/logs") | Out-Null
    Invoke-WslCommand @('/usr/bin/chmod', '0755', "$installDirectory/ADBMCPSharp") | Out-Null
    Invoke-WslCommand @('/usr/bin/cp', "$installDirectory/adbmcp.service", $unitPath) | Out-Null
    $unitInstalled = $true
    Invoke-WslCommand @('/usr/bin/systemctl', 'daemon-reload') | Out-Null
    Invoke-WslCommand @('/usr/bin/systemctl', 'enable', $serviceName) | Out-Null
    $unitEnabled = $true
    Invoke-WslCommand @('/usr/bin/systemctl', 'start', $serviceName) | Out-Null

    foreach ($expected in @{
        ActiveState = 'active'
        SubState = 'running'
        User = 'adbmcp'
        Group = 'adbmcp'
        NoNewPrivileges = 'yes'
        PrivateTmp = 'yes'
        ProtectSystem = 'strict'
        ProtectHome = 'yes'
    }.GetEnumerator()) {
        $actual = Get-UnitProperty $expected.Key
        if ($actual -ne $expected.Value) {
            throw "Unexpected $($expected.Key): expected '$($expected.Value)', observed '$actual'."
        }
    }
    $unauthenticatedAccepted = $false
    try {
        $null = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUri + '/mcp') -Method Post `
            -Headers @{ Accept = 'application/json, text/event-stream' } -ContentType 'application/json' `
            -Body '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"systemd-auth-test","version":"1.0"}}}' `
            -TimeoutSec 10
        $unauthenticatedAccepted = $true
    }
    catch {
        $statusCode = if ($null -ne $_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        if ($statusCode -ne 401) { throw }
    }
    if ($unauthenticatedAccepted) { throw 'The systemd service accepted an unauthenticated MCP request.' }
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -SkipProcessStart -BaseUri $BaseUri -ApiKey $apiKey

    $startPid = Get-UnitProperty 'MainPID'
    Invoke-WslCommand @('/usr/bin/systemctl', 'restart', $serviceName) | Out-Null
    $restartPid = Get-UnitProperty 'MainPID'
    if ($restartPid -eq '0' -or $restartPid -eq $startPid) {
        throw 'systemd restart did not produce a replacement service process.'
    }
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -SkipProcessStart -BaseUri $BaseUri -ApiKey $apiKey

    Invoke-WslCommand @(
        '/usr/bin/systemctl', 'kill', '--kill-whom=main', '--signal=KILL', $serviceName
    ) | Out-Null
    $recoveredPid = $null
    for ($attempt = 0; $attempt -lt 40 -and $null -eq $recoveredPid; $attempt++) {
        Start-Sleep -Milliseconds 250
        $candidate = Get-UnitProperty 'MainPID'
        if ((Get-UnitProperty 'ActiveState') -eq 'active' -and $candidate -ne '0' -and $candidate -ne $restartPid) {
            $recoveredPid = $candidate
        }
    }
    if ($null -eq $recoveredPid) { throw 'systemd did not recover the failed service process.' }
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -SkipProcessStart -BaseUri $BaseUri -ApiKey $apiKey

    Invoke-WslCommand @('/usr/bin/systemctl', 'stop', $serviceName) | Out-Null
    if ((Get-UnitProperty 'ActiveState') -ne 'inactive') { throw 'systemd did not stop the service.' }

    Write-Output 'SYSTEMD_ACCEPTANCE=passed'
    Write-Output ('SYSTEMD_DISTRIBUTION=' + $Distribution)
    Write-Output 'SYSTEMD_START=passed'
    Write-Output 'SYSTEMD_RESTART=passed'
    Write-Output 'SYSTEMD_FAILURE_RECOVERY=passed'
    Write-Output 'SYSTEMD_STOP=passed'
    Write-Output 'SYSTEMD_HARDENING=passed'
    Write-Output 'SYSTEMD_AUTHENTICATION=passed'
}
finally {
    if ($null -ne $temporaryEnvironmentFile) {
        Remove-Item -LiteralPath $temporaryEnvironmentFile -Force -ErrorAction SilentlyContinue
    }
    if ($unitInstalled) {
        Invoke-WslCommand @('/usr/bin/systemctl', 'stop', $serviceName) -AllowFailure | Out-Null
    }
    if ($unitEnabled) {
        Invoke-WslCommand @('/usr/bin/systemctl', 'disable', $serviceName) -AllowFailure | Out-Null
    }
    if ($unitInstalled -and (Test-WslPath $unitPath)) {
        $resolvedUnit = [string](@(Invoke-WslCommand @('/usr/bin/realpath', '-e', $unitPath))[0]).Trim()
        if ($resolvedUnit -ne $unitPath) { throw 'Refusing to remove an unexpectedly resolved systemd unit.' }
        Invoke-WslCommand @('/usr/bin/rm', '-f', '--', $unitPath) | Out-Null
    }
    if ($installCreated -and (Test-WslPath $installDirectory)) {
        $resolvedInstall = [string](@(Invoke-WslCommand @('/usr/bin/realpath', '-e', $installDirectory))[0]).Trim()
        if ($resolvedInstall -ne $installDirectory) { throw 'Refusing to remove an unexpectedly resolved install directory.' }
        Invoke-WslCommand @('/usr/bin/rm', '-rf', '--', $installDirectory) | Out-Null
    }
    if ($configurationCreated -and (Test-WslPath $configurationDirectory)) {
        $resolvedConfiguration = [string](@(Invoke-WslCommand @(
            '/usr/bin/realpath', '-e', $configurationDirectory
        ))[0]).Trim()
        if ($resolvedConfiguration -ne $configurationDirectory) {
            throw 'Refusing to remove an unexpectedly resolved configuration directory.'
        }
        Invoke-WslCommand @('/usr/bin/rm', '-rf', '--', $configurationDirectory) | Out-Null
    }
    if ($accountCreated) {
        Invoke-WslCommand @('/usr/sbin/userdel', $accountName) -AllowFailure | Out-Null
    }
    Invoke-WslCommand @('/usr/bin/systemctl', 'daemon-reload') -AllowFailure | Out-Null
    Invoke-WslCommand @('/usr/bin/systemctl', 'reset-failed') -AllowFailure | Out-Null
}
