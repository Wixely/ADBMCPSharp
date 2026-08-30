[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string[]]$Runtime = @('win-x64', 'linux-x64'),
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\ADBMCPSharp\ADBMCPSharp.csproj'
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$stageRoot = Join-Path $releaseRoot 'stage'
$isWindowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProperties = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
    $Version = [string]$buildProperties.Project.PropertyGroup.VersionPrefix
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a supported release version."
}

$releaseRootFull = [IO.Path]::GetFullPath($releaseRoot)
$stageRootFull = [IO.Path]::GetFullPath($stageRoot)
if (-not $stageRootFull.StartsWith($releaseRootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The staging directory resolved outside the release artifact directory.'
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stageRoot | Out-Null

$archiveTimestamp = [DateTime]::SpecifyKind([DateTime]'1980-01-01T00:00:00', [DateTimeKind]::Utc)
$documentation = @('LICENSE', 'README.md', 'SECURITY.md', 'THIRD-PARTY-NOTICES.md')

foreach ($runtimeIdentifier in $Runtime) {
    $packageName = "ADBMCPSharp-$Version-$runtimeIdentifier"
    $publishDirectory = Join-Path $repoRoot ("artifacts\publish\" + $runtimeIdentifier)
    $packageDirectory = Join-Path $stageRoot $packageName

    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }

    $publishArguments = @(
        'publish', $project,
        '--configuration', 'Release',
        '--runtime', $runtimeIdentifier,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:DebugType=embedded',
        '-p:ContinuousIntegrationBuild=true',
        "-p:PathMap=$repoRoot=/_/",
        "-p:Version=$Version",
        '--output', $publishDirectory
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $runtimeIdentifier with exit code $LASTEXITCODE." }

    New-Item -ItemType Directory -Path $packageDirectory | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $packageDirectory -Recurse
    foreach ($file in $documentation) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $packageDirectory
    }
    if ($runtimeIdentifier -eq 'linux-x64') {
        Copy-Item -LiteralPath (Join-Path $repoRoot 'deploy\systemd\adbmcp.service') -Destination $packageDirectory
    }

    Get-Item -LiteralPath $packageDirectory | ForEach-Object { $_.LastWriteTimeUtc = $archiveTimestamp }
    Get-ChildItem -LiteralPath $packageDirectory -Recurse -Force | ForEach-Object { $_.LastWriteTimeUtc = $archiveTimestamp }

    if ($runtimeIdentifier -eq 'win-x64') {
        $archive = Join-Path $releaseRoot ($packageName + '.zip')
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
        Compress-Archive -Path $packageDirectory -DestinationPath $archive -CompressionLevel Optimal

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
            foreach ($required in @('ADBMCPSharp.exe', 'ADBMCPSharp.json', 'LICENSE', 'README.md', 'SECURITY.md', 'THIRD-PARTY-NOTICES.md')) {
                if ($entries -notcontains "$packageName/$required") { throw "$archive is missing $required." }
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    else {
        $archive = Join-Path $releaseRoot ($packageName + '.tar.gz')
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }

        if ($isWindowsHost) {
            $wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
            if ($null -eq $wsl) { throw 'Packaging linux-x64 on Windows requires WSL so Unix file modes can be recorded correctly.' }

            $wslStageRoot = (& $wsl.Source --exec wslpath -a $stageRoot).Trim()
            $wslArchive = (& $wsl.Source --exec wslpath -a $archive).Trim()
            if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($wslStageRoot) -or [string]::IsNullOrWhiteSpace($wslArchive)) {
                throw 'WSL could not translate the release artifact paths.'
            }

            $temporaryWslDirectory = '/tmp/adbmcp-release-' + $PID + '-' + [Guid]::NewGuid().ToString('N')
            if ($temporaryWslDirectory -notmatch '^/tmp/adbmcp-release-\d+-[0-9a-f]{32}$') { throw 'Unexpected WSL staging path.' }
            try {
                & $wsl.Source --exec mkdir -p $temporaryWslDirectory
                & $wsl.Source --exec cp -R ($wslStageRoot + '/' + $packageName) $temporaryWslDirectory
                & $wsl.Source --exec find ($temporaryWslDirectory + '/' + $packageName) -type d -exec chmod 0755 '{}' '+'
                & $wsl.Source --exec find ($temporaryWslDirectory + '/' + $packageName) -type f -exec chmod 0644 '{}' '+'
                & $wsl.Source --exec chmod 0755 ($temporaryWslDirectory + '/' + $packageName + '/ADBMCPSharp')
                & $wsl.Source --exec tar --sort=name '--mtime=@0' --owner=0 --group=0 --numeric-owner -czf ($temporaryWslDirectory + '/' + [IO.Path]::GetFileName($archive)) -C $temporaryWslDirectory $packageName
                & $wsl.Source --exec cp ($temporaryWslDirectory + '/' + [IO.Path]::GetFileName($archive)) $wslArchive
                if ($LASTEXITCODE -ne 0) { throw 'WSL failed to create the Linux release archive.' }
            }
            finally {
                & $wsl.Source --exec rm -rf -- $temporaryWslDirectory
            }
        }
        else {
            & find $packageDirectory -type d -exec chmod 0755 '{}' '+'
            & find $packageDirectory -type f -exec chmod 0644 '{}' '+'
            & chmod 0755 (Join-Path $packageDirectory 'ADBMCPSharp')
            & tar --sort=name '--mtime=@0' --owner=0 --group=0 --numeric-owner -czf $archive -C $stageRoot $packageName
            if ($LASTEXITCODE -ne 0) { throw 'tar failed to create the Linux release archive.' }
        }

        $entries = @(& tar -tzf $archive)
        if ($LASTEXITCODE -ne 0) { throw 'The Linux release archive could not be read.' }
        foreach ($required in @('ADBMCPSharp', 'ADBMCPSharp.json', 'LICENSE', 'README.md', 'SECURITY.md', 'THIRD-PARTY-NOTICES.md', 'adbmcp.service')) {
            if ($entries -notcontains "$packageName/$required") { throw "$archive is missing $required." }
        }
    }
}

$checksumPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$checksumLines = Get-ChildItem -LiteralPath $releaseRoot -File |
    Where-Object { $_.Name -match '\.(zip|tar\.gz)$' } |
    Sort-Object Name |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $_.Name }
[IO.File]::WriteAllLines($checksumPath, [string[]]$checksumLines, [Text.Encoding]::ASCII)

Remove-Item -LiteralPath $stageRoot -Recurse -Force
Write-Output "Release artifacts created in $releaseRoot"
Get-ChildItem -LiteralPath $releaseRoot -File | Sort-Object Name | Select-Object Name, Length
