[CmdletBinding()]
param(
    [string]$Executable = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\publish\win-x64\ADBMCPSharp.exe')
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = Resolve-Path $Executable
$workingDirectory = Split-Path -Parent $resolvedExecutable
$serviceProcess = Start-Process -FilePath $resolvedExecutable -WorkingDirectory $workingDirectory -PassThru -WindowStyle Hidden

try {
    $health = $null
    for ($attempt = 0; $attempt -lt 20 -and $null -eq $health; $attempt++) {
        try { $health = Invoke-RestMethod -Uri 'http://localhost:21990/healthz' -TimeoutSec 1 }
        catch { Start-Sleep -Milliseconds 250 }
    }
    if ($null -eq $health) { throw 'Health endpoint did not become ready.' }
    Write-Output ('HEALTH=' + ($health | ConvertTo-Json -Compress))

    $headers = @{ Accept = 'application/json, text/event-stream' }
    $body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke-test","version":"1.0"}}}'
    $response = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:21990/mcp' -Method Post `
        -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 10
    Write-Output ('MCP_STATUS=' + $response.StatusCode)
    Write-Output ('MCP_CONTENT_TYPE=' + $response.Headers['Content-Type'])
    if ($response.Content -notmatch 'ADBMCPSharp') { throw 'MCP initialize response did not identify the server.' }
}
finally {
    if ($null -ne $serviceProcess -and -not $serviceProcess.HasExited) {
        Stop-Process -Id $serviceProcess.Id -Force
        $serviceProcess.WaitForExit()
    }
}
