param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.7.7', [ValidateSet('x64','arm64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$package = Join-Path $root "artifacts/PSiptv.v$Version.windows-$Architecture.zip"
if (!(Test-Path -LiteralPath $package)) { throw 'Build the Windows package first.' }
$apk = Join-Path $root "artifacts/PSiptv.v$Version.apk"
$resultPath = Join-Path $root "artifacts/$Version-signing-result.json"
if (!(Test-Path -LiteralPath $apk) -or !(Test-Path -LiteralPath $resultPath)) { throw 'Sign and validate the Android APK first.' }
$result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
if ($result.Version -ne $Version -or !$result.SignatureMatchesPrevious -or
    $result.SHA256 -ne (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash) { throw 'Android signing verification does not match this APK.' }
$lines = "protocol=https`nhost=github.com`n`n" | git -c credential.interactive=false credential fill 2>$null
if ($LASTEXITCODE -ne 0) { throw 'GitHub credentials unavailable.' }
$credentials = @{}
foreach ($line in $lines) { $parts = $line.Split('=', 2); if ($parts.Length -eq 2) { $credentials[$parts[0]] = $parts[1] } }
if (!$credentials['password']) { throw 'GitHub token unavailable.' }
$headers = @{ Authorization = 'Bearer ' + $credentials['password']; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'PSiptv-release' }
$api = 'https://api.github.com/repos/errors32/PSiptv'
$releases = Invoke-RestMethod -Uri "$api/releases?per_page=100" -Headers $headers
$release = $releases | Where-Object tag_name -eq $Version | Select-Object -First 1
if (!$release) {
    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve release commit.' }
    $body = @{ tag_name = $Version; target_commitish = $commit; name = "PSiptv $Version — Windows e Android";
        body = [IO.File]::ReadAllText((Join-Path $root "artifacts/RELEASE_NOTES_$Version.md")); draft = $true; prerelease = $false } | ConvertTo-Json
    $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
}
foreach ($path in @($package, ($package + '.sha256'), $apk)) {
    $name = [IO.Path]::GetFileName($path)
    $digest = 'sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $asset = $release.assets | Where-Object name -eq $name | Select-Object -First 1
    if (!$asset) {
        $url = $release.upload_url.Split('{')[0] + '?name=' + [Uri]::EscapeDataString($name)
        $asset = Invoke-RestMethod -Method Post -Uri $url -Headers $headers -ContentType 'application/octet-stream' -InFile $path
    }
    if ($asset.state -ne 'uploaded' -or $asset.size -ne (Get-Item -LiteralPath $path).Length -or $asset.digest -ne $digest) { throw "Asset verification failed: $name" }
}
if ($release.draft) {
    # Both platform packages have been uploaded and verified.
    $body = @{ draft = $false; make_latest = 'true' } | ConvertTo-Json
    $release = Invoke-RestMethod -Method Patch -Uri "$api/releases/$($release.id)" -Headers $headers -ContentType 'application/json' -Body $body
}
$verified = Invoke-RestMethod -Uri "$api/releases/tags/$Version" -Headers $headers
if ($verified.draft) { throw 'Release is still a draft.' }
[pscustomobject]@{ Url = $verified.html_url; Assets = @($verified.assets | Select-Object name,size,digest,browser_download_url) } | ConvertTo-Json -Depth 4
