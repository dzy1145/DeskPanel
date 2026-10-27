# Build a release: self-contained multi-file publish, zip it, write sha256.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
#
# Output:
#   dist\DeskPanel-<version>-win-x64.zip
#   dist\DeskPanel-<version>-win-x64.zip.sha256
#
# Note: ASCII only on purpose. Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI,
# so non-ASCII text here would break parsing on some machines.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\DeskPanel\DeskPanel.csproj'
$out  = Join-Path $root 'publish'
$dist = Join-Path $root 'dist'

# locate dotnet: PATH first, then the usual per-user install location
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $candidate = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $candidate) { $dotnet = $candidate }
}
if (-not $dotnet) { throw 'dotnet SDK not found. Install .NET 8 SDK or add it to PATH.' }

$version = ([xml](Get-Content $proj -Raw)).Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { $version = '0.0.0' }
Write-Host "DeskPanel version: $version"

Write-Host '==> cleaning'
foreach ($dir in @($out, $dist)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}
New-Item -ItemType Directory -Path $dist -Force | Out-Null

Write-Host '==> publish (self-contained, multi-file)'
# PublishSingleFile MUST stay false: single-file self-extraction fails to start
# on some machines ("Failure processing application bundle").
& $dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none `
    -p:Version=$version -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Write-Host '==> zip'
$zip = Join-Path $dist "DeskPanel-$version-win-x64.zip"
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
if (-not (Test-Path $zip)) { throw 'zip was not created' }

Write-Host '==> sha256'
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
$hashFile = "$zip.sha256"
[IO.File]::WriteAllText($hashFile, "$hash  DeskPanel-$version-win-x64.zip`n", (New-Object Text.UTF8Encoding($false)))

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ''
Write-Host "done: $zip ($size MB)"
Write-Host "sha256: $hash"
