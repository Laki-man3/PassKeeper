<#
.SYNOPSIS
  Builds the PassKeeper distribution into dist\<version>\:
    PassKeeper-Setup-<version>.exe            installer (current user / all users, silent mode)
    PassKeeper-<version>-portable-<rid>.zip   portable build (data next to the exe)
    SHA256SUMS.txt
    7z\PassKeeper-Setup-<version>.7z.001 ...  installer in 10 MB volumes (when 7-Zip is installed)

  The result runs on Windows 10/11 x64 without .NET, internet or administrator rights.
  Building needs the .NET 10 SDK and (once) access to NuGet or a local package cache/mirror.
  Intermediate files go to artifacts\<version>\; other versions are left untouched.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\build.ps1
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -Runtime win-arm64 -SkipTests -VolumeSize 0
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    # Volume size of the split 7z archive of the installer ("10m", "25m"); "0" skips it.
    [string]$VolumeSize = "10m"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
$artifacts = Join-Path $root "artifacts\$version"
$dist = Join-Path $root "dist\$version"

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

Step "Building uninstaller"
# The setup program without the embedded payload works as Uninstall.exe in the installation folder.
$uninstallOut = Join-Path $artifacts "uninstall"
dotnet build (Join-Path $root "src\PassKeeper.Setup\PassKeeper.Setup.csproj") -c $Configuration "-p:PayloadZip=none" --no-incremental -o $uninstallOut --nologo
Check "uninstaller build"
Copy-Item (Join-Path $uninstallOut "PassKeeper-Setup.exe") (Join-Path $appDir "Uninstall.exe")

Step "Packing installer payload"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload = Join-Path $artifacts "payload.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($appDir, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

Step "Building installer"
$setupOut = Join-Path $artifacts "setup"
dotnet build (Join-Path $root "src\PassKeeper.Setup\PassKeeper.Setup.csproj") -c $Configuration "-p:PayloadZip=$payload" --no-incremental -o $setupOut --nologo
Check "setup build"
$installer = Join-Path $dist "PassKeeper-Setup-$version.exe"
Copy-Item (Join-Path $setupOut "PassKeeper-Setup.exe") $installer

Step "Portable package"
$portable = Join-Path $artifacts "portable\PassKeeper"
New-Item -ItemType Directory -Force $portable | Out-Null
Copy-Item "$appDir\*" $portable -Recurse -Exclude "Uninstall.exe"
Set-Content -Path (Join-Path $portable "portable.txt") -Encoding UTF8 -Value @"
PassKeeper portable mode: the vault and settings are stored in the Data folder next to PassKeeper.exe.
Портативный режим: хранилище и настройки находятся в папке Data рядом с PassKeeper.exe.
"@
[IO.Compression.ZipFile]::CreateFromDirectory((Split-Path $portable), (Join-Path $dist "PassKeeper-$version-portable-$Runtime.zip"), [IO.Compression.CompressionLevel]::Optimal, $false)

Step "Checksums"
Get-ChildItem $dist -File | Where-Object Name -ne "SHA256SUMS.txt" | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $dist "SHA256SUMS.txt") -Encoding ASCII

$sevenZip = @((Get-Command 7z -ErrorAction SilentlyContinue).Source, "$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe") |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if ($VolumeSize -ne "0" -and $sevenZip) {
    Step "Installer in $VolumeSize volumes (7-Zip)"
    $volumes = Join-Path $dist "7z"
    New-Item -ItemType Directory -Force $volumes | Out-Null
    & $sevenZip a -t7z -mx=9 "-v$VolumeSize" (Join-Path $volumes "PassKeeper-Setup-$version.7z") $installer | Out-Null
    Check "7z"
    & $sevenZip t (Join-Path $volumes "PassKeeper-Setup-$version.7z.001") | Out-Null
    Check "7z test"
    Get-ChildItem $volumes -Filter "*.7z.*" | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    } | Set-Content (Join-Path $volumes "SHA256SUMS-7z.txt") -Encoding ASCII
}
elseif ($VolumeSize -ne "0") {
    Write-Host "7-Zip not found: the split archive is skipped." -ForegroundColor Yellow
}

Step "Done: $dist"
Get-ChildItem $dist -Recurse -File | Select-Object @{ n = "File"; e = { $_.FullName.Substring($dist.Length + 1) } }, @{ n = "Size, MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
