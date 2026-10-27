[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Apk,
    [string]$AndroidSdk = $env:ANDROID_HOME
)
$ErrorActionPreference = 'Stop'
if (!$AndroidSdk) { $AndroidSdk = $env:ANDROID_SDK_ROOT }
if (!$AndroidSdk) { $AndroidSdk = Join-Path ${env:ProgramFiles(x86)} 'Android/android-sdk' }
$aapt = Get-ChildItem (Join-Path $AndroidSdk 'build-tools/*/aapt.exe') |
    Sort-Object { [version]$_.Directory.Name } -Descending | Select-Object -First 1
if (!$aapt) { throw 'Android SDK aapt.exe was not found. Pass -AndroidSdk.' }
$package = & $aapt.FullName dump badging $Apk 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw "Cannot inspect APK: $package" }
if ($package -notmatch "package: name='com.PS.PSiptv'") { throw 'Unexpected Wear package id.' }
if ($package -notmatch "launchable-activity: name='[^']+'[^\r\n]*label='PSiptv Remote'") {
    throw 'PSiptv Remote is missing from the Android launcher.'
}
if ($package -notmatch "launchable-activity:[^\r\n]*icon='[^']+'") {
    throw 'Wear launcher activity must have an explicit icon.'
}
if ($package -notmatch "uses-feature: name='android.hardware.type.watch'") { throw 'APK does not target Wear OS.' }
if ($package -notmatch "uses-permission: name='android.permission.BLUETOOTH_CONNECT'") {
    throw 'APK is missing the permission to connect to paired Bluetooth devices.'
}
if ($package -notmatch "native-code:[^\r\n]*'armeabi-v7a'" -or $package -notmatch "native-code:[^\r\n]*'arm64-v8a'") {
    throw 'APK must include both 32-bit and 64-bit ARM runtimes for watches.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Apk).Path)
try {
    foreach ($resource in @('drawable/app_icon.xml', 'drawable/app_icon_foreground.xml',
        'drawable/app_icon_monochrome.xml',
        'mipmap-anydpi-v26/app_icon.xml', 'mipmap-anydpi-v33/app_icon.xml')) {
        if (!$zip.GetEntry("res/$resource")) { throw "APK is missing launcher icon resource: $resource" }
    }
    # Fast Deployment APKs contain the native runtime but omit managed code.
    foreach ($abi in @('armeabi-v7a', 'arm64-v8a')) {
        $store = $zip.Entries | Where-Object {
            $_.FullName -match "^lib/$abi/lib(assemblies.*|assembly-store)\.so$" -and $_.Length -gt 0
        }
        $assembly = $zip.Entries | Where-Object {
            ($_.FullName -match '^assemblies/(.*/)?PSiptv\.Wear\.dll$' -or
             $_.FullName -eq "lib/$abi/lib_PSiptv.Wear.dll.so") -and $_.Length -gt 0
        }
        if (!$store -and !$assembly) { throw "APK lacks embedded .NET assemblies for $abi and cannot be sideloaded independently." }
    }
} finally { $zip.Dispose() }
Write-Output 'PASS: Wear launcher, legacy/adaptive/monochrome icons, watch feature, Bluetooth permission, ARM runtimes and embedded .NET code are present.'
