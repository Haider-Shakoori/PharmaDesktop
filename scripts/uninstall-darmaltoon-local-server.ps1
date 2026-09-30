$ErrorActionPreference = "Stop"

$serviceName = "BusinessOS Pharmacy Local Server"
$firewallRule = "Darmaltoon Local Server (Private LAN)"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Administrator rights are required to remove the Darmaltoon Local Server service."
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(15))
    }

    & sc.exe delete "$serviceName" | Out-Null
}

& netsh.exe advfirewall firewall delete rule name="$firewallRule" | Out-Null

Write-Host "Darmaltoon Local Server Windows Service and its firewall rule were removed."
Write-Host "Pharmacy databases, backups, activation, certificates, network configuration and logs were intentionally left untouched."
