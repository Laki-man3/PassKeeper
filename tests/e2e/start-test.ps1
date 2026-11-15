param([string]$Scenario = "tray", [string]$Exe = "$PSScriptRoot\..\..\src\PassKeeper\bin\Release\net10.0-windows\PassKeeper.exe")
# A fresh test profile in its own data folder, a separate instance on it (--data), signed in with its master password.
$ErrorActionPreference = "Stop"
$s = $PSScriptRoot
. "$s\uia.ps1"
$data = "$s\run-$Scenario\data"
New-Item -ItemType Directory -Force "$s\run-$Scenario" | Out-Null
& dotnet "$s\seed\bin\Debug\net10.0-windows\seed.dll" $data $Scenario | Out-Null
$p = Start-Process $Exe -ArgumentList @("--data", "`"$data`"") -PassThru
$win = Get-AppWindow $p.Id
Start-Sleep -Seconds 2
$switch = Find-ById $win 'SwitchMode' 3
if ($switch) { Invoke-El $switch; Start-Sleep -Seconds 1 }
$master = (Get-Content "$data.master" -Raw).Trim()
$pass = $win.FindFirst($TS::Descendants, (New-Object $PC ($AE::IsPasswordProperty, $true)))
Set-Value $pass $master
$master = $null
$login = @($win.FindAll($TS::Descendants, (New-Object $PC ($AE::ControlTypeProperty, $CT::Button))) | Where-Object { $_.Current.AutomationId -match 'Login|Unlock' })[0]
Invoke-El $login
Start-Sleep -Seconds 3
$p.Id | Set-Content "$s\run-$Scenario\pid.txt"
