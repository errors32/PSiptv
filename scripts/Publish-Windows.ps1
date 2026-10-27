param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.7.16', [ValidateSet('x64','arm64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts/windows-$Version-$Architecture"
$zip = Join-Path $root "artifacts/PSiptv.v$Version.windows-$Architecture.zip"
if (Test-Path -LiteralPath $out) { throw "Output already exists: $out. Use a new version or inspect it before rebuilding." }
dotnet publish (Join-Path $root 'PSiptv/PSiptv.csproj') -f net10.0-windows10.0.19041.0 -c Release -p:AppTargetFrameworks=net10.0-windows10.0.19041.0 -p:RuntimeIdentifier=win-$Architecture -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -p:PublishTrimmed=false -p:ApplicationDisplayVersion=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
if (!(Test-Path -LiteralPath (Join-Path $out 'PSiptv.exe'))) { throw 'Missing executable.' }
Set-Content -LiteralPath (Join-Path $out '.psiptv-portable') -Value $Version
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256')
[pscustomobject]@{ Package = $zip; Bytes = (Get-Item -LiteralPath $zip).Length; SHA256 = $hash } | ConvertTo-Json
