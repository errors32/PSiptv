param([Parameter(Mandatory)][string]$Target, [Parameter(Mandatory)][string]$Stage,
      [Parameter(Mandatory)][int]$ParentProcess)
$ErrorActionPreference = 'Stop'
$Target = [IO.Path]::GetFullPath($Target).TrimEnd('\')
$Stage = [IO.Path]::GetFullPath($Stage).TrimEnd('\')
$backup = $Target + '.backup-' + [guid]::NewGuid().ToString('N')
$log = Join-Path $env:TEMP 'PSiptv-windows-update.log'
try {
    # Only replace a marked portable app with a staged sibling created by the app.
    if ([IO.Path]::GetDirectoryName($Target) -ne [IO.Path]::GetDirectoryName($Stage) -or
        !$Stage.StartsWith($Target + '.update-', [StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath (Join-Path $Target '.psiptv-portable')) -or
        !(Test-Path -LiteralPath (Join-Path $Stage '.psiptv-portable')) -or
        !(Test-Path -LiteralPath (Join-Path $Stage 'PSiptv.exe'))) { throw 'Invalid update paths or package.' }
    $parent = Get-Process -Id $ParentProcess -ErrorAction SilentlyContinue
    if ($parent -and !$parent.WaitForExit(60000)) { throw 'Application did not exit within 60 seconds.' }
    # Other windows or native libraries can briefly hold file handles after exit.
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try { Move-Item -LiteralPath $Target -Destination $backup; break }
        catch { if ($attempt -eq 19) { throw }; Start-Sleep -Milliseconds 500 }
    }
    try {
        Move-Item -LiteralPath $Stage -Destination $Target
        Start-Process -FilePath (Join-Path $Target 'PSiptv.exe') -WorkingDirectory $Target -WindowStyle Hidden
    }
    catch {
        if (Test-Path -LiteralPath $Target) { Move-Item -LiteralPath $Target -Destination $Stage }
        Move-Item -LiteralPath $backup -Destination $Target
        throw
    }
    "Update completed. Previous binaries: $backup" | Set-Content -LiteralPath $log
}
catch {
    $_ | Out-String | Set-Content -LiteralPath $log
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("Não foi possível concluir a atualização. Os ficheiros anteriores foram preservados. Detalhes: $log", 'PSiptv') | Out-Null
    exit 1
}
