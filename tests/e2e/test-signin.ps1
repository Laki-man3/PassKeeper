param([string]$Exe = "$PSScriptRoot\..\..\src\PassKeeper\bin\Release\net10.0-windows\PassKeeper.exe", [string]$Browser = "C:\Program Files\Google\Chrome\Application\chrome.exe", [string]$ProfileName = "chrome-profile")
# Automatic sign-in on local pages: every kind of page once, repeated successful sign-ins, then a refused password.
# PassKeeper runs hidden in the tray as usual; it types only into the separate test browser profile.
$s = $PSScriptRoot
. "$s\uia.ps1"
$port = 8772
$dir = "$s\run-multi"
$serverLog = "$dir\server.log"
$pkLog = "$dir\data\autofill.log"
$profileDir = "$s\site\$ProfileName"
$browserName = Split-Path $Browser -Leaf

function Test-Browser { @(Get-CimInstance Win32_Process -Filter "Name='$browserName'" | Where-Object { $_.CommandLine -like "*$profileDir*" }) }
function Stop-Browser {
    foreach ($p in (Test-Browser)) { try { (Get-Process -Id $p.ProcessId -ErrorAction Stop).CloseMainWindow() | Out-Null } catch {} }
    Start-Sleep -Seconds 2
    foreach ($p in (Test-Browser)) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
}
function Stop-Own {
    Get-CimInstance Win32_Process -Filter "Name='PassKeeper.exe'" | Where-Object { $_.CommandLine -like "*run-multi*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Get-CimInstance Win32_Process -Filter "Name='python.exe'" | Where-Object { $_.CommandLine -like "*site\sites.py*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
}
function Count-Log([string]$file, [string]$pattern) { if (Test-Path $file) { @(Select-String -Path $file -Pattern $pattern -SimpleMatch -Encoding UTF8).Count } else { 0 } }
function Wait-Log([string]$file, [string]$pattern, [int]$sec, [int]$atLeast = 1) {
    $deadline = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $deadline) {
        if ((Count-Log $file $pattern) -ge $atLeast) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}
function Browser-Window {
    foreach ($p in (Test-Browser)) {
        $w = $AE::RootElement.FindFirst($TS::Children, (New-Object $PC ($AE::ProcessIdProperty, [int]$p.ProcessId)))
        if ($w) { return $w }
    }
    return $null
}
function Open-Url([string]$url) {
    Start-Process $Browser -ArgumentList @("--user-data-dir=`"$profileDir`"", $url) | Out-Null
    Start-Sleep -Milliseconds 1200
    $w = Browser-Window
    if ($w) { try { $w.SetFocus() } catch {} }
}
function Mem([string]$what) { $p = Get-Process -Id $id; Write-Output ("       memory {0}: working set {1} MB, commit {2} MB" -f $what, [math]::Round($p.WorkingSet64 / 1MB, 1), [math]::Round($p.PrivateMemorySize64 / 1MB, 1)) }

Stop-Browser
Stop-Own
& "$s\start-test.ps1" -Scenario multi -Exe $Exe
$id = [int](Get-Content "$dir\pid.txt")
(Get-AppWindow $id).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Remove-Item $serverLog -ErrorAction SilentlyContinue
Start-Process python -ArgumentList @("`"$s\site\sites.py`"", $port, "`"$dir\data.passwords.json`"", "`"$serverLog`"") -WindowStyle Hidden | Out-Null
Start-Sleep -Seconds 1
Start-Process $Browser -ArgumentList @("--user-data-dir=`"$profileDir`"", "--no-first-run", "--no-default-browser-check", "--disable-sync", "--password-store=basic", "--disable-features=Translate,AutofillServerCommunication,PasswordManagerOnboarding", "--new-window", "about:blank") | Out-Null
Start-Sleep -Seconds 3

Write-Output "1. every kind of page"
foreach ($site in @(@("mail-box.localhost", "/signin", "bob@mail-box.test", "two steps"), @("shop-spa.localhost", "/", "carol", "late form"), @("bank-big.localhost", "/login", "dave", "form below the fold"), @("frame-site.localhost", "/", "erin", "form in a frame"))) {
    Open-Url "http://$($site[0]):$port$($site[1])"
    Check (Wait-Log $serverLog "submit host=$($site[0]) user=$($site[2]) ok=True" 30) "$($site[3]): signed in"
    Start-Sleep -Seconds 1
}
Mem "after signing in on 4 sites"
Start-Sleep -Seconds 35
Mem "35 s later"
Start-Sleep -Seconds 15
Mem "50 s later"

Write-Output "2. repeated successful sign-ins on one site (one-time links)"
$goneBefore = Count-Log $pkLog "the attempt does not count"
Write-Output "       sign-ins recognised as successful on the pages above: $goneBefore of 4"
foreach ($i in 1..4) {
    Open-Url "http://sso-corp.localhost:$port/auth"
    Check (Wait-Log $serverLog "submit host=sso-corp.localhost user=alice ok=True" 25 $i) "sign-in $i went through"
    Check (Wait-Log $pkLog "the attempt does not count" 15 ($goneBefore + $i)) "sign-in $i recognised as successful"
    Start-Sleep -Seconds 9
}
Check ((Count-Log $pkLog "not signed in:") -eq 0) "the limit was never reached"

Write-Output "3. a refused password still stops after two attempts"
$passwords = Get-Content "$dir\data.passwords.json" -Raw | ConvertFrom-Json
$passwords.'sso-corp.localhost' = "changed-on-the-server"
$passwords | ConvertTo-Json | Set-Content "$dir\data.passwords.json" -Encoding ASCII
Open-Url "http://sso-corp.localhost:$port/auth"
Check (Wait-Log $serverLog "submit host=sso-corp.localhost user=alice ok=False" 30 2) "two attempts were made"
Start-Sleep -Seconds 12
Open-Url "http://sso-corp.localhost:$port/auth"
Start-Sleep -Seconds 12
Check ((Count-Log $serverLog "submit host=sso-corp.localhost user=alice ok=False") -eq 2) "no third attempt ($(Count-Log $serverLog 'submit host=sso-corp.localhost user=alice ok=False'))"
Check ((Count-Log $pkLog "the attempt does not count") -le ((Count-Log $serverLog "ok=True"))) "refused attempts were not taken for successful"

Stop-Browser
Stop-Own
Write-Output ("result: " + $(if ($script:Failures -eq 0) { "all passed" } else { "$script:Failures failed" }))
