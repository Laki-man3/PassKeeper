<#
.SYNOPSIS
  Builds PassKeeper for macOS and Linux (src/PassKeeper.Desktop, Avalonia) into dist\<version>\:
    PassKeeper-<version>-macos-arm64.zip   PassKeeper.app for Apple Silicon
    PassKeeper-<version>-macos-x64.zip     PassKeeper.app for Intel Macs
    PassKeeper-<version>-linux-x64.tar.gz  folder with PassKeeper and install.sh
  and adds them to dist\<version>\SHA256SUMS.txt. Run after build.ps1 (which empties dist\<version>).
  Self-contained: no .NET installation is needed on the target system.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\build-desktop.ps1
#>
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
$artifacts = Join-Path $root "artifacts\$version\desktop"
$dist = Join-Path $root "dist\$version"
$icon = Join-Path $root "src\PassKeeper.Desktop\Assets\PassKeeper.png"

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" } }

Remove-Item -Recurse -Force $artifacts -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $artifacts, $dist | Out-Null

Step "Packager"
dotnet build (Join-Path $root "tools\Packager\Packager.csproj") -c Release -o (Join-Path $artifacts "packager") --nologo -v q
Check "packager build"
$packager = Join-Path $artifacts "packager\Packager.dll"

$targets = @(
    @{ Rid = "osx-arm64"; Kind = "macos"; File = "PassKeeper-$version-macos-arm64.zip" },
    @{ Rid = "osx-x64"; Kind = "macos"; File = "PassKeeper-$version-macos-x64.zip" },
    @{ Rid = "linux-x64"; Kind = "linux"; File = "PassKeeper-$version-linux-x64.tar.gz" }
)
foreach ($t in $targets) {
    Step "Publishing PassKeeper $version ($($t.Rid), self-contained)"
    $out = Join-Path $artifacts $t.Rid
    dotnet publish (Join-Path $root "src\PassKeeper.Desktop\PassKeeper.Desktop.csproj") -c Release -r $t.Rid --self-contained true `
        -p:DebugType=none -p:DebugSymbols=false -o $out --nologo
    Check "dotnet publish $($t.Rid)"
    & dotnet $packager $t.Kind $out (Join-Path $dist $t.File) $version $icon
    Check "packing $($t.Rid)"
}

Step "Checksums"
$sums = Join-Path $dist "SHA256SUMS.txt"
$kept = if (Test-Path $sums) { @(Get-Content $sums | Where-Object { $_ -notmatch '-(macos|linux)-' }) } else { @() }
$new = foreach ($t in $targets) { "{0}  {1}" -f (Get-FileHash (Join-Path $dist $t.File) -Algorithm SHA256).Hash.ToLowerInvariant(), $t.File }
@($kept) + @($new) | Set-Content $sums -Encoding ASCII
Get-ChildItem $dist -File | Select-Object Name, @{ n = "Size, MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
