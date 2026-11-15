param([string]$Exe = "$PSScriptRoot\..\..\src\PassKeeper.Desktop\bin\Release\net10.0\PassKeeper.exe", [string]$Shots = "")
# The macOS / Linux app (Avalonia), run on Windows and driven through UI Automation: a new user with a PIN, an entry
# with a 2FA code, PIN and master password sign-in, editing, the trash, the dialogs; auto-type by the hotkey into the
# test client (window shown, in the tray, locked); then a vault made by the shared core opened by the new app.
# The hotkey is only sent while the test client is in front. The clipboard is not touched.
$s = $PSScriptRoot
. "$s\uia.ps1"
$dir = "$s\run-desktop"
$data = "$dir\data"
$fake = "$s\fakevpn\bin\Debug\net10.0-windows\fakevpn.exe"
$fakeLog = "$dir\fake.log"
$pin = "482913"

if (-not ("PkKeys" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class PkKeys
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc f, IntPtr l);
    public static int ForegroundPid() { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return (int)pid; }
    public static int VisibleWindows(int pid) { int n = 0; EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) n++; return true; }, IntPtr.Zero); return n; }
    // Ctrl+Alt+Shift+F11, only while the given process has the window in front.
    public static bool HotkeyInto(int pid)
    {
        if (ForegroundPid() != pid) return false;
        byte[] mods = { 0x11, 0x12, 0x10 };
        foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
        keybd_event(0x7A, 0, 0, UIntPtr.Zero);
        keybd_event(0x7A, 0, 2, UIntPtr.Zero);
        for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, UIntPtr.Zero);
        return true;
    }
}
"@
}

function Stop-Own {
    Get-CimInstance Win32_Process -Filter "Name='PassKeeper.exe'" | Where-Object { $_.CommandLine -like "*run-desktop*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Get-Process fakevpn -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $fake } | Stop-Process -Force
}
function Windows([int]$procId) { @($AE::RootElement.FindAll($TS::Children, (New-Object $PC ($AE::ProcessIdProperty, $procId)))) }
function Find-Any([int]$procId, [string]$id, [int]$sec = 10) {
    $deadline = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $deadline) {
        foreach ($w in (Windows $procId)) { $e = $w.FindFirst($TS::Descendants, (New-Object $PC ($AE::AutomationIdProperty, $id))); if ($e) { return $e } }
        Start-Sleep -Milliseconds 200
    }
    return $null
}
function Has-Text([int]$procId, [string]$text) {
    foreach ($w in (Windows $procId)) {
        foreach ($e in @($w.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition))) { if ($e.Current.Name -like "*$text*") { return $true } }
    }
    return $false
}
function Set-Id([int]$procId, [string]$id, [string]$value) { $e = Find-Any $procId $id; if (-not $e) { throw "no $id" }; Set-Value $e $value }
function Click-Id([int]$procId, [string]$id) { $e = Find-Any $procId $id; if (-not $e) { throw "no $id" }; Invoke-El $e }
function List-Count([int]$procId) { $list = Find-Any $procId 'EntryList'; if (-not $list) { return -1 }; @($list.FindAll($TS::Children, (New-Object $PC ($AE::ControlTypeProperty, $CT::ListItem)))).Count }
function Shot([int]$procId, [string]$name) { if ($Shots) { & "$s\capture.ps1" -ProcessId $procId -Out "$Shots\$name.png" | Out-Null } }
function Signed-In([int]$times, [int]$sec = 20) {
    $deadline = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path $fakeLog) -and @(Select-String -Path $fakeLog -Pattern "user=jdoe ok=True").Count -ge $times) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}
function Client-In-Front([string]$password) {
    $f = Start-Process $fake -ArgumentList @("`"$fakeLog`"", $password) -PassThru
    $w = Get-AppWindow $f.Id
    for ($i = 0; $i -lt 10 -and [PkKeys]::ForegroundPid() -ne $f.Id; $i++) { try { $w.SetFocus() } catch {}; Start-Sleep -Milliseconds 300 }
    return $f
}
function Pin-Credentials { @(cmdkey /list | Select-String "PassKeeper Desktop/pin-").Count }
function Start-App([string[]]$extra = @()) { Start-Process $Exe -ArgumentList (@("--data", "`"$data`"") + $extra) -PassThru }

Stop-Own
Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $data | Out-Null
'{ "language": "ru", "autoTypeHotkey": "Ctrl+Alt+Shift+F11", "closeToTray": true }' | Set-Content "$data\settings.json" -Encoding ASCII
$master = "M-" + [Convert]::ToBase64String((1..12 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
$vpnPassword = "pw-" + [Convert]::ToBase64String((1..6 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
$credentialsBefore = Pin-Credentials

Write-Output "1. a new user, a PIN and an entry"
$p = Start-App
Get-AppWindow $p.Id | Out-Null
Set-Id $p.Id 'Name' 'Тест'
Set-Id $p.Id 'NewMaster' $master
Set-Id $p.Id 'RepeatMaster' $master
Click-Id $p.Id 'Create'
Check ($null -ne (Find-Any $p.Id 'NewPin' 20)) "a PIN is offered for the new user"
Set-Id $p.Id 'NewPin' $pin
Set-Id $p.Id 'RepeatPin' $pin
Click-Id $p.Id 'SavePin'
Start-Sleep -Seconds 2
Check ((Pin-Credentials) -eq $credentialsBefore + 1) "the PIN's key is in the secret store"
Click-Id $p.Id 'NewEntry'
Set-Id $p.Id 'EditTitle' 'Корпоративная почта'
Set-Id $p.Id 'EditLogin' 'ivanov'
Set-Id $p.Id 'EditPassword' 'Qq-test-1234'
Set-Id $p.Id 'EditUrl' 'mail.example.org'
Set-Id $p.Id 'EditTotp' 'JBSWY3DPEHPK3PXP'
Click-Id $p.Id 'SaveEntry'
Start-Sleep -Seconds 1
Check ((List-Count $p.Id) -eq 1) "the entry is in the list"
Check ($null -ne (Find-Any $p.Id 'CopyTotp' 5)) "the 2FA code is shown"
Shot $p.Id "desktop-vault"

Write-Output "2. lock: PIN and master password"
Click-Id $p.Id 'Lock'
Check ($null -ne (Find-Any $p.Id 'Pin' 10)) "locked: the PIN is asked"
Set-Id $p.Id 'Pin' '111111'
Click-Id $p.Id 'SignIn'
Start-Sleep -Seconds 3
Check (Has-Text $p.Id 'Неверный PIN') "a wrong PIN is refused"
Set-Id $p.Id 'Pin' $pin
Click-Id $p.Id 'SignIn'
Check ($null -ne (Find-Any $p.Id 'NewEntry' 20)) "the PIN opens the vault"
Click-Id $p.Id 'Lock'
Click-Id $p.Id 'SwitchMode'
Set-Id $p.Id 'Master' 'wrong-password'
Click-Id $p.Id 'SignIn'
Start-Sleep -Seconds 3
Check (Has-Text $p.Id 'Неверный мастер-пароль') "a wrong master password is refused"
Set-Id $p.Id 'Master' $master
Click-Id $p.Id 'SignIn'
Check ($null -ne (Find-Any $p.Id 'NewEntry' 20)) "the master password opens the vault"

Write-Output "3. edit and the trash"
Click-Id $p.Id 'Edit'
Set-Id $p.Id 'EditTitle' 'Почта (рабочая)'
Click-Id $p.Id 'SaveEntry'
Start-Sleep -Seconds 1
Check (Has-Text $p.Id 'Почта (рабочая)') "the new title is shown"
Click-Id $p.Id 'Delete'
$confirm = $null
$deadline = (Get-Date).AddSeconds(5)
while (-not $confirm -and (Get-Date) -lt $deadline) {
    $buttons = @(foreach ($w in (Windows $p.Id)) { $w.FindAll($TS::Descendants, (New-Object $PC ($AE::ControlTypeProperty, $CT::Button))) })
    $confirm = @($buttons | Where-Object { $_.Current.Name -eq 'Удалить' -and $_.Current.AutomationId -ne 'Delete' })[0]
    Start-Sleep -Milliseconds 200
}
if ($confirm) { Invoke-El $confirm }
Start-Sleep -Seconds 1
Check ((List-Count $p.Id) -eq 0) "deleted: the list is empty"

Write-Output "4. settings, generator and export open and close"
foreach ($d in @(@('Settings', 'Настройки'), @('Generator', 'Генератор паролей'), @('Export', 'Экспорт'))) {
    Click-Id $p.Id $d[0]
    $dialog = $null
    $byName = New-Object System.Windows.Automation.AndCondition((New-Object $PC ($AE::ControlTypeProperty, $CT::Window)), (New-Object $PC ($AE::NameProperty, $d[1])))
    $deadline = (Get-Date).AddSeconds(6)
    while (-not $dialog -and (Get-Date) -lt $deadline) {
        foreach ($w in (Windows $p.Id)) { $found = $w.FindFirst($TS::Descendants, $byName); if ($found) { $dialog = $found } }
        Start-Sleep -Milliseconds 200
    }
    Check ($null -ne $dialog) "$($d[1]) opens"
    if ($dialog) { $dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 500 }
}

Write-Output "5. auto-type by the hotkey into the test client"
Click-Id $p.Id 'NewEntry'
Set-Id $p.Id 'EditTitle' 'FakeVPN'
Set-Id $p.Id 'EditLogin' 'jdoe'
Set-Id $p.Id 'EditPassword' $vpnPassword
Set-Id $p.Id 'EditWindows' 'fakevpn'
(Find-Any $p.Id 'EditAutoLogin').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
Click-Id $p.Id 'SaveEntry'
Start-Sleep -Seconds 1
$f = Client-In-Front $vpnPassword
Check ([PkKeys]::HotkeyInto($f.Id)) "a. the hotkey is pressed in the client's window"
Check (Signed-In 1) "a. signed in to the client, PassKeeper's window open"

(Get-AppWindow $p.Id).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Start-Sleep -Seconds 1
Check ((-not $p.HasExited) -and [PkKeys]::VisibleWindows($p.Id) -eq 0) "b. closing the window keeps PassKeeper in the tray"
$f = Client-In-Front $vpnPassword
Check ([PkKeys]::HotkeyInto($f.Id)) "b. the hotkey is pressed in the client's window"
Check (Signed-In 2) "b. signed in to the client from the tray"

Stop-Process -Id $p.Id -Force
$p = Start-App @("--minimized")
Start-Sleep -Seconds 4
Check ((-not $p.HasExited) -and [PkKeys]::VisibleWindows($p.Id) -eq 0) "c. started in the tray, locked"
$f = Client-In-Front $vpnPassword
Check ([PkKeys]::HotkeyInto($f.Id)) "c. the hotkey is pressed in the client's window"
Check ($null -ne (Find-Any $p.Id 'Pin' 10)) "c. the PIN is asked first"
Set-Id $p.Id 'Pin' $pin
Click-Id $p.Id 'SignIn'
Check (Signed-In 3 25) "c. signed in to the client after the PIN"
Get-Process fakevpn -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $fake } | Stop-Process -Force

# d. Two entries for the same client: the list asks which one.
$main = Get-AppWindow $p.Id
try { $main.SetFocus() } catch {}
Click-Id $p.Id 'NewEntry'
Set-Id $p.Id 'EditTitle' 'FakeVPN другой'
Set-Id $p.Id 'EditLogin' 'other'
Set-Id $p.Id 'EditPassword' 'not-this-one'
Set-Id $p.Id 'EditWindows' 'fakevpn'
Click-Id $p.Id 'SaveEntry'
Start-Sleep -Seconds 1
$f = Client-In-Front $vpnPassword
Check ([PkKeys]::HotkeyInto($f.Id)) "d. the hotkey is pressed in the client's window"
$list = Find-Any $p.Id 'PickerList' 10
Check ($null -ne $list) "d. the list of entries is shown"
if ($list) {
    $item = @($list.FindAll($TS::Children, (New-Object $PC ($AE::ControlTypeProperty, $CT::ListItem))) | Where-Object { $_.FindFirst($TS::Descendants, (New-Object $PC ($AE::NameProperty, 'FakeVPN'))) })[0]
    if ($item) { Select-El $item }
    Click-Id $p.Id 'PickerType'
}
Check (Signed-In 4 20) "d. the chosen entry is typed in"
Get-Process fakevpn -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $fake } | Stop-Process -Force

Write-Output "6. removing the PIN"
$main = Get-AppWindow $p.Id
try { $main.SetFocus() } catch {}
Click-Id $p.Id 'Settings'
Click-Id $p.Id 'RemovePin'
Start-Sleep -Seconds 1
Check ((Pin-Credentials) -eq $credentialsBefore) "the PIN's key left the secret store"
Click-Id $p.Id 'SaveSettings'
Stop-Process -Id $p.Id -Force

Write-Output "7. a vault made by the shared core (as the Windows version makes it)"
$data2 = "$dir\from-windows"
& dotnet "$s\seed\bin\Debug\net10.0-windows\seed.dll" $data2 multi | Out-Null
$p = Start-Process $Exe -ArgumentList @("--data", "`"$data2`"") -PassThru
Get-AppWindow $p.Id | Out-Null
if (Find-Any $p.Id 'SwitchMode' 3) { Click-Id $p.Id 'SwitchMode' }
Set-Id $p.Id 'Master' ((Get-Content "$data2.master" -Raw).Trim())
Click-Id $p.Id 'SignIn'
Check ($null -ne (Find-Any $p.Id 'NewEntry' 20)) "opened with its master password"
Check ((List-Count $p.Id) -eq 5) "all 5 entries are there ($(List-Count $p.Id))"
Stop-Process -Id $p.Id -Force

Stop-Own
Write-Output ("result: " + $(if ($script:Failures -eq 0) { "all passed" } else { "$script:Failures failed" }))
