<#
.SYNOPSIS
  Builds the PassKeeper distribution:
    dist\PassKeeper-Setup-<version>.exe            installer (current user / all users, silent mode)
    dist\PassKeeper-<version>-portable-<rid>.zip   portable build (data next to the exe)
    dist\SHA256SUMS.txt

  The result runs on Windows 10/11 x64 without .NET, internet or administrator rights.
  Building needs the .NET 10 SDK and (once) access to NuGet or a local package cache/mirror.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\build.ps1
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -Runtime win-arm64 -SkipTests
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$dist = Join-Path $root "dist"
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" } }

Remove-Item -Recurse -Force $artifacts, $dist -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $artifacts, $dist | Out-Null

if (-not $SkipTests) {
    Step "Unit tests"
    dotnet test (Join-Path $root "tests\PassKeeper.Tests\PassKeeper.Tests.csproj") -c $Configuration --nologo
    Check "dotnet test"
}

Step "Publishing PassKeeper $version ($Runtime, self-contained)"
$appDir = Join-Path $artifacts "app"
dotnet publish (Join-Path $root "src\PassKeeper\PassKeeper.csproj") -c $Configuration -r $Runtime --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false -o $appDir --nologo
Check "dotnet publish"
Get-ChildItem $appDir -Recurse -Include *.pdb, *.xml | Remove-Item -Force

Step "Packing installer payload"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload = Join-Path $artifacts "payload.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($appDir, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

Step "Building installer"
$setupOut = Join-Path $artifacts "setup"
dotnet build (Join-Path $root "src\PassKeeper.Setup\PassKeeper.Setup.csproj") -c $Configuration "-p:PayloadZip=$payload" -o $setupOut --nologo
Check "setup build"
Copy-Item (Join-Path $setupOut "PassKeeper-Setup.exe") (Join-Path $dist "PassKeeper-Setup-$version.exe")

Step "Portable package"
$portable = Join-Path $artifacts "portable\PassKeeper"
New-Item -ItemType Directory -Force $portable | Out-Null
Copy-Item "$appDir\*" $portable -Recurse
Set-Content -Path (Join-Path $portable "portable.txt") -Encoding UTF8 -Value @"
PassKeeper portable mode: the vault and settings are stored in the Data folder next to PassKeeper.exe.
Портативный режим: хранилище и настройки находятся в папке Data рядом с PassKeeper.exe.
"@
[IO.Compression.ZipFile]::CreateFromDirectory((Split-Path $portable), (Join-Path $dist "PassKeeper-$version-portable-$Runtime.zip"), [IO.Compression.CompressionLevel]::Optimal, $false)

Step "Checksums"
Get-ChildItem $dist -File | Where-Object Name -ne "SHA256SUMS.txt" | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $dist "SHA256SUMS.txt") -Encoding ASCII

Step "Done"
Get-ChildItem $dist | Select-Object Name, @{ n = "Size, MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
