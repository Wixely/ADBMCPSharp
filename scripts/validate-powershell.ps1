[CmdletBinding()]
param(
    [switch]$RequireWindowsPowerShell
)

$ErrorActionPreference = 'Stop'
if ($RequireWindowsPowerShell -and
    ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSEdition -ne 'Desktop')) {
    throw 'This validation run requires Windows PowerShell 5.1 Desktop.'
}

$scripts = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | Sort-Object Name)
if ($scripts.Count -eq 0) { throw 'No PowerShell scripts were found to validate.' }
$failures = New-Object System.Collections.Generic.List[string]
foreach ($script in $scripts) {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile(
        $script.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    foreach ($errorRecord in @($errors)) {
        $failures.Add("$($script.Name):$($errorRecord.Extent.StartLineNumber): $($errorRecord.Message)")
    }
}
if ($failures.Count -gt 0) {
    throw "PowerShell parsing failed:`n$($failures -join [Environment]::NewLine)"
}

Write-Output ('POWERSHELL_EDITION=' + $PSVersionTable.PSEdition)
Write-Output ('POWERSHELL_VERSION=' + $PSVersionTable.PSVersion)
Write-Output ('POWERSHELL_SCRIPTS=' + $scripts.Count)
Write-Output 'POWERSHELL_PARSE=passed'
