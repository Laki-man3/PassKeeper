param([string]$Exe = "$PSScriptRoot\..\..\src\PassKeeper\bin\Release\net10.0-windows\PassKeeper.exe")
# Automatic sign-in to a client: successful sign-ins do not use up the limit, refused ones stop after two.
$s = $PSScriptRoot
. "$s\uia.ps1"
$dir = "$s\run-vpn"
$fake = "$s\fakevpn\bin\Debug\net10.0-windows\fakevpn.exe"
$fakeLog = "$dir\fake.log"
$pkLog = "$dir\data\autofill.log"

function Count-Log([string]$file, [string]$pattern) { if (Test-Path $file) { @(Select-String -Path $file -Pattern $pattern -SimpleMatch).Count } else { 0 } }
function Wait-Log([string]$file, [string]$pattern, [int]$sec, [int]$atLeast = 1) {
    $deadline = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $deadline) {
        if ((Count-Log $file $pattern) -ge $atLeast) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}
function Stop-Fakes { Get-Process fakevpn -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $fake } | Stop-Process -Force }

Stop-Fakes
& "$s\start-test.ps1" -Scenario vpn -Exe $Exe
$id = [int](Get-Content "$dir\pid.txt")
(Get-AppWindow $id).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Remove-Item $fakeLog -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$password = (Get-Content "$dir\data.vpnpw" -Raw).Trim()

Write-Output "1. three successful sign-ins in a row"
foreach ($i in 1..3) {
    Start-Process $fake -ArgumentList @("`"$fakeLog`"", $password) | Out-Null
    Check (Wait-Log $fakeLog "submit user=jdoe ok=True" 25 $i) "sign-in $i went through"
    Check (Wait-Log $pkLog "the sign-in window is gone, the attempt does not count" 25 $i) "sign-in $i recognised as successful"
    Start-Sleep -Seconds 31
}

Write-Output "2. a refused password stops after two attempts"
foreach ($i in 1..2) {
    Start-Process $fake -ArgumentList @("`"$fakeLog`"", "wrong-on-the-server") | Out-Null
    Check (Wait-Log $fakeLog "submit user=jdoe ok=False" 25 $i) "refused attempt $i"
    Start-Sleep -Seconds 2
    Stop-Fakes
    Start-Sleep -Seconds 2
}
Start-Process $fake -ArgumentList @("`"$fakeLog`"", "wrong-on-the-server") | Out-Null
Start-Sleep -Seconds 12
Check ((Count-Log $fakeLog "ok=False") -eq 2) "no third attempt ($(Count-Log $fakeLog 'ok=False'))"

Write-Output "3. the window comes back after every watch has run out"
Start-Sleep -Seconds 20
Stop-Fakes
Start-Sleep -Seconds 2
Start-Process $fake -ArgumentList @("`"$fakeLog`"", "wrong-on-the-server") | Out-Null
Start-Sleep -Seconds 12
Check ((Count-Log $fakeLog "ok=False") -eq 2) "still no third attempt ($(Count-Log $fakeLog 'ok=False'))"
Check ((Count-Log $pkLog "the sign-in window is gone") -eq 3) "refused attempts were not taken for successful ($(Count-Log $pkLog 'the sign-in window is gone'))"

Stop-Fakes
Stop-Process -Id $id -Force
Write-Output ("result: " + $(if ($script:Failures -eq 0) { "all passed" } else { "$script:Failures failed" }))
