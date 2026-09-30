param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [ValidateRange(1024, 65535)]
    [int]$Port = 5280
)

$ErrorActionPreference = "Stop"

$serviceName = "BusinessOS Pharmacy Local Server"
$firewallRule = "Darmaltoon Local Server (Private LAN)"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Administrator rights are required to install the Darmaltoon Local Server service."
    }
}

Assert-Administrator

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "Local Server executable was not found: $ExecutablePath"
}

$profiles = Get-NetConnectionProfile | Where-Object {
    $_.IPv4Connectivity -ne "Disconnected" -or
    $_.IPv6Connectivity -ne "Disconnected"
}

$hasPrivate = @($profiles | Where-Object {
    $_.NetworkCategory -eq "Private" -or
    $_.NetworkCategory -eq "DomainAuthenticated"
}).Count -gt 0

$hasPublic = @($profiles | Where-Object {
    $_.NetworkCategory -eq "Public"
}).Count -gt 0

if ($hasPublic -and -not $hasPrivate) {
    throw "Windows identifies the connected network as Public. Mark the trusted pharmacy LAN as Private before installing/exposing the Local Server."
}

if (-not $hasPrivate) {
    throw "No connected Private/Domain Windows network was detected."
}

$quotedBinaryPath = '"' + $resolvedExecutable + '"'

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $existing) {
    & sc.exe create "$serviceName" binPath= "$quotedBinaryPath" start= auto | Out-Null
}
else {
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(15))
    }

    & sc.exe config "$serviceName" binPath= "$quotedBinaryPath" start= auto | Out-Null
}

& sc.exe description "$serviceName" "Darmaltoon authoritative local pharmacy LAN server." | Out-Null
& sc.exe failure "$serviceName" reset= 86400 actions= restart/5000/restart/15000/restart/30000 | Out-Null

& netsh.exe advfirewall firewall delete rule name="$firewallRule" | Out-Null
& netsh.exe advfirewall firewall add rule name="$firewallRule" dir=in action=allow protocol=TCP localport=$Port profile=private | Out-Null

Start-Service -Name $serviceName

Write-Host "Darmaltoon Local Server installed and started."
Write-Host "Service: $serviceName"
Write-Host "TCP port: $Port"
Write-Host "Firewall profile: Private only"
Write-Host "No pharmacy database or configuration data was deleted or reset."
