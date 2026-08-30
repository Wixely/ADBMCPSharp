[CmdletBinding()]
param(
    [string]$ImageName = 'adbmcp-sharp:smoke',
    [ValidateRange(1024, 65535)]
    [int]$Port = 21991,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
if ($null -eq $dockerCommand) { throw 'Docker CLI is required for the container smoke test.' }

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = 'adbmcp-smoke-' + [Guid]::NewGuid().ToString('N')
$apiKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$environmentFile = [IO.Path]::GetTempFileName()
$containerStarted = $false

try {
    [IO.File]::WriteAllText($environmentFile, 'Server__ApiKey=' + $apiKey + [Environment]::NewLine)
    if (-not $SkipBuild) {
        & docker build --file (Join-Path $repositoryRoot 'deploy\Dockerfile') --tag $ImageName $repositoryRoot
        if ($LASTEXITCODE -ne 0) { throw "Docker image build failed with exit code $LASTEXITCODE." }
    }

    $publishedPort = '127.0.0.1:' + $Port + ':8080'
    & docker run --detach --rm --name $containerName `
        --publish $publishedPort `
        --env-file $environmentFile `
        $ImageName
    if ($LASTEXITCODE -ne 0) { throw "Docker container start failed with exit code $LASTEXITCODE." }
    $containerStarted = $true

    & (Join-Path $PSScriptRoot 'smoke-test.ps1') `
        -SkipProcessStart `
        -BaseUri ('http://localhost:' + $Port) `
        -ApiKey $apiKey
    if ($LASTEXITCODE -ne 0) { throw "Container MCP smoke test failed with exit code $LASTEXITCODE." }
}
finally {
    if ($containerStarted) {
        & docker stop --time 10 $containerName | Out-Null
    }
    Remove-Item -LiteralPath $environmentFile -Force -ErrorAction SilentlyContinue
}
