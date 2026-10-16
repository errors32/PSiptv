param([switch]$Child, [string]$Target, [string]$Stage, [switch]$FailLaunch)
$ErrorActionPreference = 'Stop'
$updater = Join-Path (Split-Path $PSScriptRoot -Parent) 'PSiptv/Resources/Raw/apply-windows-update.ps1'
if ($Child) {
    # Replace process launch and the error dialog; exercise the real file replacement script.
    Add-Type 'namespace System.Windows { public static class MessageBox { public static void Show(string message, string title) {} } }'
    function Add-Type { param($AssemblyName) }
    function Start-Process {
        param($FilePath, $WorkingDirectory, $WindowStyle)
        if ($FailLaunch) { throw 'Simulated launch failure' }
        if (!(Test-Path -LiteralPath $FilePath)) { throw 'Executable missing' }
    }
    & $updater -Target $Target -Stage $Stage -ParentProcess 2147483647
    exit $LASTEXITCODE
}
$testRoot = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts/updater-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
foreach ($scenario in @('success','rollback','invalid')) {
    $target = Join-Path $testRoot $scenario
    $stage = $target + '.update-fixture'
    New-Item -ItemType Directory -Path $target,$stage | Out-Null
    'old' | Set-Content (Join-Path $target 'PSiptv.exe')
    'new' | Set-Content (Join-Path $stage 'PSiptv.exe')
    'marker' | Set-Content (Join-Path $target '.psiptv-portable')
    if ($scenario -ne 'invalid') { 'marker' | Set-Content (Join-Path $stage '.psiptv-portable') }
    $arguments = @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File', $PSCommandPath,
        '-Child', '-Target', $target, '-Stage', $stage)
    if ($scenario -eq 'rollback') { $arguments += '-FailLaunch' }
    & powershell.exe @arguments
    $result = $LASTEXITCODE
    $content = (Get-Content -LiteralPath (Join-Path $target 'PSiptv.exe') -Raw).Trim()
    if ($scenario -eq 'success') {
        if ($result -ne 0 -or $content -ne 'new' -or @(Get-ChildItem $testRoot -Filter 'success.backup-*').Count -ne 1) { throw 'Success scenario failed' }
    }
    elseif ($result -eq 0 -or $content -ne 'old') { throw "$scenario scenario failed" }
    "PASS: updater $scenario"
}
# Keep fixtures for inspection; no production installation or data is touched.
