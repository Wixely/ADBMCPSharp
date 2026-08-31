[CmdletBinding()]
param(
    [string]$Executable,
    [ValidatePattern('^ADBMCPSharp-Acceptance(?:-[A-Za-z0-9]+)?$')]
    [string]$ServiceName = 'ADBMCPSharp-Acceptance',
    [ValidateRange(1024, 65535)]
    [int]$Port = 21991
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Windows Service acceptance must run on Windows.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Windows Service acceptance requires an elevated Administrator PowerShell session.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $repoRoot 'artifacts\publish\win-x64\ADBMCPSharp.exe'
}
$resolvedExecutable = (Resolve-Path $Executable).Path
$sourceDirectory = Split-Path -Parent $resolvedExecutable
$sourceConfiguration = Join-Path $sourceDirectory 'ADBMCPSharp.json'
if (-not (Test-Path -LiteralPath $sourceConfiguration)) {
    throw 'The published executable directory does not contain ADBMCPSharp.json.'
}
if ($null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    throw "Service '$ServiceName' already exists; refusing to replace it."
}
if ($null -ne (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)) {
    throw "Port $Port is already in use."
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$installDirectory = Join-Path $temporaryRoot ('ADBMCPSharp-service-' + [Guid]::NewGuid().ToString('N'))
$resolvedInstallDirectory = [IO.Path]::GetFullPath($installDirectory)
if (-not $resolvedInstallDirectory.StartsWith(
    $temporaryRoot + [IO.Path]::DirectorySeparatorChar + 'ADBMCPSharp-service-',
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The temporary service directory resolved outside the expected location.'
}

$serviceCreated = $false
$apiKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$baseUri = 'http://localhost:' + $Port
$sc = (Get-Command sc.exe).Source

function Invoke-ServiceControl {
    param([Parameter(Mandatory = $true)][string[]]$ArgumentList, [switch]$AllowFailure)
    $output = @(& $sc @ArgumentList 2>&1)
    $exitCode = $LASTEXITCODE
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "sc.exe $($ArgumentList[0]) failed with exit code $exitCode."
    }
    return $output
}

function Get-ServiceProcessId {
    $escapedName = $ServiceName.Replace("'", "''")
    $service = Get-CimInstance Win32_Service -Filter ("Name = '" + $escapedName + "'")
    if ($null -eq $service) { return 0 }
    return [int]$service.ProcessId
}

function Wait-ServiceState([string]$ExpectedState, [int]$Attempts = 40) {
    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -ne $service -and [string]$service.Status -eq $ExpectedState) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Service '$ServiceName' did not reach state '$ExpectedState'."
}

try {
    New-Item -ItemType Directory -Path $resolvedInstallDirectory | Out-Null
    Copy-Item -LiteralPath $resolvedExecutable -Destination $resolvedInstallDirectory
    Copy-Item -LiteralPath $sourceConfiguration -Destination $resolvedInstallDirectory
    $installedExecutable = Join-Path $resolvedInstallDirectory 'ADBMCPSharp.exe'
    $binaryPath = '"' + $installedExecutable + '" --Server:Host=localhost --Server:Port=' + $Port

    Invoke-ServiceControl @(
        'create', $ServiceName, 'binPath=', $binaryPath, 'start=', 'demand',
        'DisplayName=', 'ADBMCPSharp acceptance service'
    ) | Out-Null
    $serviceCreated = $true

    $serviceRegistryPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\' + $ServiceName
    New-ItemProperty -Path $serviceRegistryPath -Name Environment -PropertyType MultiString -Value @(
        'ADBMCP_Server__ApiKey=' + $apiKey
    ) -Force | Out-Null
    Invoke-ServiceControl @(
        'failure', $ServiceName, 'reset=', '0', 'actions=', 'restart/1000'
    ) | Out-Null

    Start-Service -Name $ServiceName
    Wait-ServiceState 'Running'
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -SkipProcessStart -BaseUri $baseUri -ApiKey $apiKey
    $startPid = Get-ServiceProcessId
    if ($startPid -le 0) { throw 'The running Windows Service has no process ID.' }

    Restart-Service -Name $ServiceName
    Wait-ServiceState 'Running'
    $restartPid = Get-ServiceProcessId
    if ($restartPid -le 0 -or $restartPid -eq $startPid) {
        throw 'Windows Service restart did not produce a replacement process.'
    }
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -SkipProcessStart -BaseUri $baseUri -ApiKey $apiKey

    $managedProcess = Get-CimInstance Win32_Process -Filter ("ProcessId = " + $restartPid)
    if ($null -eq $managedProcess -or -not [string]::Equals(
        [string]$managedProcess.ExecutablePath, $installedExecutable, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing the recovery test because the service process path was unexpected.'
    }
    Stop-Process -Id $restartPid -Force
    $recoveredPid = 0
    for ($attempt = 0; $attempt -lt 60 -and $recoveredPid -eq 0; $attempt++) {
        Start-Sleep -Milliseconds 250
        $candidate = Get-ServiceProcessId
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status -eq 'Running' -and
            $candidate -gt 0 -and $candidate -ne $restartPid) {
            $recoveredPid = $candidate
        }
    }
    if ($recoveredPid -eq 0) { throw 'Windows Service recovery did not replace the failed process.' }
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -SkipProcessStart -BaseUri $baseUri -ApiKey $apiKey

    Stop-Service -Name $ServiceName
    Wait-ServiceState 'Stopped'

    Write-Output 'WINDOWS_SERVICE_ACCEPTANCE=passed'
    Write-Output 'WINDOWS_SERVICE_START=passed'
    Write-Output 'WINDOWS_SERVICE_RESTART=passed'
    Write-Output 'WINDOWS_SERVICE_FAILURE_RECOVERY=passed'
    Write-Output 'WINDOWS_SERVICE_STOP=passed'
    Write-Output 'WINDOWS_SERVICE_AUTHENTICATION=passed'
}
finally {
    if ($serviceCreated) {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status -ne 'Stopped') {
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        }
        Invoke-ServiceControl @('delete', $ServiceName) -AllowFailure | Out-Null
        for ($attempt = 0; $attempt -lt 40 -and
            $null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue); $attempt++) {
            Start-Sleep -Milliseconds 250
        }
    }
    if (Test-Path -LiteralPath $resolvedInstallDirectory) {
        $cleanupPath = [IO.Path]::GetFullPath((Resolve-Path $resolvedInstallDirectory).Path)
        if ($cleanupPath -ne $resolvedInstallDirectory -or -not $cleanupPath.StartsWith(
            $temporaryRoot + [IO.Path]::DirectorySeparatorChar + 'ADBMCPSharp-service-',
            [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove an unexpectedly resolved temporary service directory.'
        }
        Remove-Item -LiteralPath $cleanupPath -Recurse -Force
    }
}
