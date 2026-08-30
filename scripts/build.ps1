[CmdletBinding()]
param(
    [ValidateSet('Restore', 'Build', 'Test', 'Publish')]
    [string]$Action = 'Test',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'ADBMCPSharp.slnx'
$project = Join-Path $repoRoot 'src\ADBMCPSharp\ADBMCPSharp.csproj'
$publishDirectory = Join-Path $repoRoot ("artifacts\publish\" + $Runtime)

switch ($Action) {
    'Restore' { dotnet restore $solution }
    'Build' { dotnet build $solution --configuration $Configuration }
    'Test' { dotnet test $solution --configuration $Configuration }
    'Publish' {
        dotnet publish $project --configuration $Configuration --runtime $Runtime --self-contained true `
            -p:PublishSingleFile=true -p:DebugType=embedded --output $publishDirectory
    }
}

if ($LASTEXITCODE -ne 0) { throw "dotnet $Action failed with exit code $LASTEXITCODE." }
