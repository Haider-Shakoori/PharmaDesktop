param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$serviceName = "BusinessOS Pharmacy Local Server"
$displayName = "Darmaltoon Main Pharmacy Server"

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "Local Server executable was not found: $ExecutablePath"
}

$quotedBinaryPath = '"' + $resolvedExecutable + '"'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    New-Service -Name $serviceName -BinaryPathName $quotedBinaryPath -DisplayName $displayName -StartupType Automatic | Out-Null
}
else {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(15))
    }

    & sc.exe config "$serviceName" binPath= "$quotedBinaryPath" start= auto DisplayName= "$displayName" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not update the Darmaltoon Local Server service."
    }
}

& sc.exe description "$serviceName" "Darmaltoon authoritative local pharmacy LAN server." | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Could not set the Darmaltoon Local Server service description."
}

& sc.exe failure "$serviceName" reset= 86400 actions= restart/5000/restart/15000/restart/30000 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Could not configure Darmaltoon Local Server recovery actions."
}
