[CmdletBinding()]
param(
    [string]$Adb = 'adb',
    [Parameter(Mandatory = $true)][string]$Serial,
    [string]$PackageName = 'com.PS.PSiptv'
)
$ErrorActionPreference = 'Stop'
# Start with a search editor focused and its keyboard visible.
function Invoke-Adb {
    param([string[]]$Arguments)
    $result = & $Adb -s $Serial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "ADB failed: $result" }
    return ($result -join "`n")
}
function Get-FocusedControl {
    $null = Invoke-Adb -Arguments @('shell', 'uiautomator', 'dump', '/sdcard/psiptv-focus-probe.xml')
    [xml]$tree = Invoke-Adb -Arguments @('shell', 'cat', '/sdcard/psiptv-focus-probe.xml')
    $node = $tree.SelectNodes('//node') | Where-Object {
        $_.GetAttribute('package') -eq $PackageName -and $_.GetAttribute('focused') -eq 'true'
    } | Select-Object -First 1
    if ($null -eq $node) { throw 'No focused app control was reported.' }
    return [pscustomobject]@{
        Class = $node.GetAttribute('class')
        Resource = $node.GetAttribute('resource-id')
        Bounds = $node.GetAttribute('bounds')
    }
}
function Test-KeyboardVisible {
    $state = Invoke-Adb -Arguments @('shell', 'dumpsys', 'input_method')
    return $state -match '(mInputShown|isInputViewShown)\s*[=:]\s*true'
}
try {
$initial = Get-FocusedControl
if ($initial.Class -notmatch 'EditText|SearchAutoComplete' -or !(Test-KeyboardVisible)) {
    throw 'Open a search field in PSiptv and leave the keyboard visible before running this test.'
}
$null = Invoke-Adb -Arguments @('shell', 'input', 'keyevent', '4')
Start-Sleep -Milliseconds 700
$closed = Get-FocusedControl
if (Test-KeyboardVisible) { throw 'Back did not close the keyboard.' }
if ($closed.Bounds -ne $initial.Bounds -or $closed.Resource -ne $initial.Resource -or $closed.Class -ne $initial.Class) {
    throw 'Back changed the search focus.'
}
Write-Output 'PASS: Back closes the keyboard and preserves search focus.'
$null = Invoke-Adb -Arguments @('shell', 'input', 'keyevent', '20')
Start-Sleep -Milliseconds 400
$below = Get-FocusedControl
if ($below.Bounds -eq $initial.Bounds -and $below.Resource -eq $initial.Resource) {
    throw 'D-pad Down did not leave the search field.'
}
if (Test-KeyboardVisible) { throw 'The keyboard reopened while moving down.' }
Write-Output 'PASS: D-pad Down leaves search without reopening the keyboard.'
$null = Invoke-Adb -Arguments @('shell', 'input', 'keyevent', '19')
Start-Sleep -Milliseconds 700
$returned = Get-FocusedControl
if ($returned.Bounds -ne $initial.Bounds -or $returned.Resource -ne $initial.Resource -or !(Test-KeyboardVisible)) {
    throw 'D-pad Up did not restore search focus and reopen the keyboard.'
}
Write-Output 'PASS: Returning to search reopens the keyboard.'
}
finally { $null = Invoke-Adb -Arguments @('shell', 'rm', '-f', '/sdcard/psiptv-focus-probe.xml') }
