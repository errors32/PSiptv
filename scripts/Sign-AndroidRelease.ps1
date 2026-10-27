[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$PreviousVersion = '1.7.16',
    [string]$KeyStore = (Join-Path $env:LOCALAPPDATA 'Xamarin/Mono for Android/Keystore/PSiptv/PSiptv.keystore'),
    [string]$BuildTools = 'C:/Program Files (x86)/Android/android-sdk/build-tools/36.0.0',
    [string]$JavaSdk = 'C:/Program Files/Android/openjdk/jdk-21.0.8'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$previous = Join-Path $root "artifacts/PSiptv.v$PreviousVersion.apk"
if (!(Test-Path -LiteralPath $KeyStore) -or !(Test-Path -LiteralPath $previous)) {
    throw 'The existing production keystore and previous signed APK are required.'
}
$targets = @(
    @{ Source = 'PSiptv/bin/Release/net10.0-android/com.PS.PSiptv.apk'; Name = "PSiptv.v$Version"; Result = "$Version-signing-result.json" },
    @{ Source = 'PSiptv.Wear/bin/Release/net10.0-android/com.PS.PSiptv.apk'; Name = "PSiptv.Remote.Wear.v$Version"; Result = "$Version-wear-signing-result.json" }
)
foreach ($target in $targets) {
    $source = Join-Path $root $target.Source
    $metadata = & "$BuildTools/aapt.exe" dump badging $source 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $metadata -notmatch "package: name='com.PS.PSiptv'" -or
        $metadata -notmatch "versionName='$([regex]::Escape($Version))'") { throw "Build the $Version APK first: $source" }
}
$savedJavaHome = $env:JAVA_HOME
$env:JAVA_HOME = $JavaSdk
$securePassword = Read-Host 'Android production signing password' -AsSecureString
try {
    $env:PSIPTV_RELEASE_SIGNING_PASSWORD = [Net.NetworkCredential]::new('', $securePassword).Password
    if (!$env:PSIPTV_RELEASE_SIGNING_PASSWORD) { throw 'No signing password supplied.' }
    $listing = & "$JavaSdk/bin/keytool.exe" '-J-Duser.language=en' -list -v -keystore $KeyStore -storepass:env PSIPTV_RELEASE_SIGNING_PASSWORD 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Could not open the existing production keystore.' }
    $aliases = [regex]::Matches($listing, '(?m)^Alias name: (.+)\r?$')
    if ($aliases.Count -ne 1) { throw 'Expected one production signing identity.' }
    $alias = $aliases[0].Groups[1].Value.Trim()
    $listing = $null
    $certificatePattern = 'Signer #1 certificate SHA-256 digest: ([a-fA-F0-9]+)'
    $previousCertificate = & "$BuildTools/apksigner.bat" verify --print-certs $previous 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Previous production APK signature is invalid.' }
    $expectedCertificate = [regex]::Match($previousCertificate, $certificatePattern).Groups[1].Value
    if (!$expectedCertificate -or $previousCertificate -match 'CN=Android Debug') { throw 'Previous APK is not a production signing reference.' }
    $previousMetadata = & "$BuildTools/aapt.exe" dump badging $previous 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the previous production APK.' }
    $previousCode = [long][regex]::Match($previousMetadata, "versionCode='(\d+)'").Groups[1].Value
    $codes = @()
    foreach ($target in $targets) {
        $source = Join-Path $root $target.Source
        $aligned = Join-Path $root "artifacts/$($target.Name)-aligned.apk"
        $output = Join-Path $root "artifacts/$($target.Name).apk"
        $log = Join-Path $root "artifacts/$($target.Name)-signature.log"
        & "$BuildTools/zipalign.exe" -P 16 -f 4 $source $aligned *> $log
        if ($LASTEXITCODE -ne 0) { throw 'APK alignment failed.' }
        & "$BuildTools/apksigner.bat" sign --ks $KeyStore --ks-key-alias $alias --ks-pass env:PSIPTV_RELEASE_SIGNING_PASSWORD --key-pass env:PSIPTV_RELEASE_SIGNING_PASSWORD --out $output $aligned *>> $log
        if ($LASTEXITCODE -ne 0) { throw 'APK signing failed.' }
        & "$BuildTools/zipalign.exe" -c -P 16 4 $output *>> $log
        if ($LASTEXITCODE -ne 0) { throw 'Signed APK alignment verification failed.' }
        $certificate = & "$BuildTools/apksigner.bat" verify --verbose --print-certs $output 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw 'Signed APK signature verification failed.' }
        $certificate | Add-Content -LiteralPath $log
        $actualCertificate = [regex]::Match($certificate, $certificatePattern).Groups[1].Value
        if ($actualCertificate -ne $expectedCertificate) { throw 'APK signer differs from the previous production identity.' }
        $metadata = & "$BuildTools/aapt.exe" dump badging $output 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the signed APK.' }
        $code = [long][regex]::Match($metadata, "versionCode='(\d+)'").Groups[1].Value
        if ($code -le $previousCode -or $codes -contains $code) { throw 'APK versionCode must be newer and distinct for each form factor.' }
        $codes += $code
        $result = [pscustomobject]@{
            Apk = "artifacts/$($target.Name).apk"; Version = $Version; VersionCode = $code
            SignatureMatchesPrevious = $true; CertificateSHA256 = $actualCertificate
            Bytes = (Get-Item -LiteralPath $output).Length; SHA256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
        }
        $result | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $root "artifacts/$($target.Result)")
        $result | ConvertTo-Json -Compress
    }
    & (Join-Path $PSScriptRoot 'Test-WearPackage.ps1') -Apk (Join-Path $root "artifacts/PSiptv.Remote.Wear.v$Version.apk")
} finally {
    $securePassword.Dispose()
    Remove-Item Env:PSIPTV_RELEASE_SIGNING_PASSWORD -ErrorAction SilentlyContinue
    $env:JAVA_HOME = $savedJavaHome
}
