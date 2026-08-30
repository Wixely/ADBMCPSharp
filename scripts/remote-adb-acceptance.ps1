[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DeviceAlias,
    [string]$Executable,
    [string]$LocalConfig,
    [ValidateRange(1024, 65535)]
    [int]$AdbServerPort = 5041
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($LocalConfig)) {
    $LocalConfig = Join-Path $repositoryRoot 'src\ADBMCPSharp\ADBMCPSharp.Local.json'
}

$configuration = Get-Content -Raw (Resolve-Path $LocalConfig) | ConvertFrom-Json
$deviceProperty = $configuration.Adb.Devices.PSObject.Properties |
    Where-Object { $_.Name -eq $DeviceAlias } |
    Select-Object -First 1
if ($null -eq $deviceProperty) { throw "Unknown local device alias '$DeviceAlias'." }

$adbExecutable = (Resolve-Path ([string]$configuration.Adb.ExecutablePath)).Path
$deviceSelector = [string]$deviceProperty.Value.Selector
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $AdbServerPort)
try { $listener.Start() }
catch { throw "ADB acceptance port $AdbServerPort is already in use." }
finally { $listener.Stop() }

$stdoutPath = [IO.Path]::GetTempFileName()
$stderrPath = [IO.Path]::GetTempFileName()
$adbServerProcess = $null

function Invoke-AdbQuiet([string[]]$Arguments) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $adbExecutable @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
        return [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
    }
    finally { $ErrorActionPreference = $previousPreference }
}

try {
    $adbServerProcess = Start-Process -FilePath $adbExecutable `
        -ArgumentList @('-L', ('tcp:' + $AdbServerPort), 'server', 'nodaemon') `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru `
        -WindowStyle Hidden

    $serverReady = $false
    for ($attempt = 0; $attempt -lt 20 -and -not $serverReady; $attempt++) {
        if ($adbServerProcess.HasExited) { break }
        $probe = Invoke-AdbQuiet @('-H', '127.0.0.1', '-P', [string]$AdbServerPort, 'devices')
        if ($probe.ExitCode -eq 0) { $serverReady = $true }
        else { Start-Sleep -Milliseconds 250 }
    }
    if (-not $serverReady) { throw 'The isolated ADB server did not become ready.' }
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $AdbServerPort -ErrorAction SilentlyContinue)
    if ($listeners.Count -eq 0 -or @($listeners | Where-Object {
        $_.LocalAddress -notin @('127.0.0.1', '::1')
    }).Count -gt 0) {
        throw 'The isolated ADB server did not bind exclusively to loopback.'
    }

    $connect = Invoke-AdbQuiet @('-H', '127.0.0.1', '-P', [string]$AdbServerPort, 'connect', $deviceSelector)
    if ($connect.ExitCode -ne 0) { throw 'The isolated ADB server did not accept the configured device connection.' }

    $deviceReady = $false
    for ($attempt = 0; $attempt -lt 20 -and -not $deviceReady; $attempt++) {
        $state = Invoke-AdbQuiet @('-H', '127.0.0.1', '-P', [string]$AdbServerPort, '-s', $deviceSelector, 'get-state')
        if ($state.ExitCode -eq 0 -and $state.Output -contains 'device') { $deviceReady = $true }
        else { Start-Sleep -Milliseconds 250 }
    }
    if (-not $deviceReady) { throw 'The configured device did not become ready through the isolated ADB server.' }

    $acceptanceArguments = @{
        DeviceAlias = $DeviceAlias
        LocalConfig = $LocalConfig
        AdbServerAlias = 'remote-acceptance'
        AdbServerMode = 'Remote'
        AdbServerHost = '127.0.0.1'
        AdbServerPort = $AdbServerPort
        IncludeConnectionLifecycle = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($Executable)) { $acceptanceArguments.Executable = $Executable }
    & (Join-Path $PSScriptRoot 'device-acceptance.ps1') @acceptanceArguments
}
finally {
    $null = Invoke-AdbQuiet @('-H', '127.0.0.1', '-P', [string]$AdbServerPort, 'kill-server')
    if ($null -ne $adbServerProcess -and -not $adbServerProcess.HasExited) {
        Stop-Process -Id $adbServerProcess.Id -Force
        $adbServerProcess.WaitForExit()
    }
    Remove-Item -LiteralPath $stdoutPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
}
