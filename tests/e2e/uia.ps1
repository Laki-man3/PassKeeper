Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]
$CT = [System.Windows.Automation.ControlType]
function Get-AppWindow([int]$procId, [int]$timeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $w = $AE::RootElement.FindFirst($TS::Children, (New-Object $PC ($AE::ProcessIdProperty, $procId)))
        if ($w) { return $w }
        Start-Sleep -Milliseconds 300
    }
    throw "window of process $procId not found"
}
function Find-ById($root, [string]$id, [int]$timeoutSec = 10) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $e = $root.FindFirst($TS::Descendants, (New-Object $PC ($AE::AutomationIdProperty, $id)))
        if ($e) { return $e }
        Start-Sleep -Milliseconds 200
    }
    return $null
}
function Invoke-El($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Select-El($e) { $e.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function Set-Value($e, [string]$v) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v) }
function Get-Items($list) { @($list.FindAll($TS::Children, (New-Object $PC ($AE::ControlTypeProperty, $CT::ListItem))) | ForEach-Object { $_ }) }
$script:Failures = 0
function Check([bool]$ok, [string]$what) { if ($ok) { Write-Output "  OK   $what" } else { Write-Output "  FAIL $what"; $script:Failures++ } }
