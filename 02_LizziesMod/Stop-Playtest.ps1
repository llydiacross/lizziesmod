<#!
.SYNOPSIS
Stops locally running 7 Days To Die test-client processes.

.DESCRIPTION
Stops the game client and its Easy Anti-Cheat helper when present, allowing a
fresh playtest launch to load rebuilt mod assemblies.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param()

$ErrorActionPreference = 'Stop'

$runningClient = Get-Process -Name '7DaysToDie', '7DaysToDie_EAC' -ErrorAction SilentlyContinue
if ($null -eq $runningClient) {
    Write-Host 'No 7 Days To Die client processes are running.'
    return
}

foreach ($process in $runningClient | Sort-Object ProcessName, Id) {
    $target = "$($process.ProcessName) (PID $($process.Id))"
    if ($PSCmdlet.ShouldProcess($target, 'Stop 7 Days To Die test client process')) {
        Stop-Process -Id $process.Id -Force
        Write-Host "Stopped $target."
    }
}