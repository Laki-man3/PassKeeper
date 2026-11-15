param([string]$Exe = "C:\Users\admin\Documents\PassKeeper\src\PassKeeper\bin\Release\net10.0-windows\PassKeeper.exe", [string]$Label = "build")
# Clients that take their time: PassKeeper started with Windows (minimized, locked) while the client is loading,
# the PIN given, the form appearing later; and a separate sign-in window after the client's main window.
# Only the test client and PassKeeper's own PIN prompt are touched (UI Automation patterns, no keys).
$s = $PSScriptRoot
. "$s\uia.ps1"
$dir = "$s\run-slow"
$data = "$dir\data"
$fake = "$s\fakevpn\bin\Debug\net10.0-windows\fakevpn.exe"
$fakeLog = "$dir\fake.log"

function Stop-Own {
    Get-CimInstance Win32_Process -Filter "Name='PassKeeper.exe'" | Where-Object { $_.CommandLine -like "*run-slow*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Get-Process fakevpn -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $fake } | Stop-Process -Force
}
function Signed-In([int]$sec) {
    $deadline = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path $fakeLog) -and (Select-String -Path $fakeLog -Pattern "ok=True" -Quiet)) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}
function New-Profile {
    Stop-Own
    Start-Sleep -Milliseconds 500
    New-Item -ItemType Directory -Force $dir | Out-Null
    Remove-Item $fakeLog -ErrorAction SilentlyContinue
    & dotnet "$s\seed\bin\Debug\net10.0-windows\seed.dll" $data vpn | Out-Null
    $settings = Get-Content "$data\settings.json" -Raw | ConvertFrom-Json
    $settings | Add-Member -NotePropertyName autoLoginTriggers -NotePropertyValue @("fakevpn.exe") -Force
    $settings | ConvertTo-Json | Set-Content "$data\settings.json" -Encoding UTF8
}
function Enter-Pin([int]$procId) {
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        foreach ($el in @($AE::RootElement.FindAll($TS::Children, (New-Object $PC ($AE::ProcessIdProperty, $procId))))) {
            $box = $el.FindFirst($TS::Descendants, (New-Object $PC ($AE::IsPasswordProperty, $true)))
            if ($box) {
                Set-Value $box (Get-Content "$data.pin" -Raw).Trim()
                Start-Sleep -Milliseconds 400
                $button = @($el.FindAll($TS::Descendants, (New-Object $PC ($AE::ControlTypeProperty, $CT::Button))) | Where-Object { $_.Current.AutomationId -match 'Login|Unlock' })[0]
                if ($button) { Invoke-El $button }
                return $true
            }
        }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

Write-Output "== $Label"
# 1. Started with Windows while the client is still loading (its fields come 20 s later).
New-Profile
$pw = (Get-Content "$data.vpnpw" -Raw).Trim()
Start-Process $fake -ArgumentList @("`"$fakeLog`"", $pw, "slow", "20") | Out-Null
Start-Sleep -Seconds 2
$p = Start-Process $Exe -ArgumentList @("--data", "`"$data`"", "--minimized") -PassThru
Check (Enter-Pin $p.Id) "1. PIN asked for the loading client"
Check (Signed-In 40) "1. signed in once the client had loaded"
Stop-Own
Copy-Item "$data\autofill.log" "$dir\scenario1.log" -ErrorAction SilentlyContinue

# 2. The client's main window first, its sign-in window 10 s later (PassKeeper unlocked meanwhile).
New-Profile
$pw = (Get-Content "$data.vpnpw" -Raw).Trim()
Start-Process $fake -ArgumentList @("`"$fakeLog`"", $pw, "dialog", "10") | Out-Null
Start-Sleep -Seconds 2
$p = Start-Process $Exe -ArgumentList @("--data", "`"$data`"", "--minimized") -PassThru
Check (Enter-Pin $p.Id) "2. PIN asked for the client's main window"
Check (Signed-In 30) "2. signed in to the sign-in window that came after the main one"
Stop-Own
Write-Output ("result: " + $(if ($script:Failures -eq 0) { "all passed" } else { "$script:Failures failed" }))
