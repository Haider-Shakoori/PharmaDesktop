param(
    [string]$Version = "1.0.0",
    [string]$ArtifactsDirectory = "artifacts/windows"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifacts = [IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactsDirectory))
$installDir = Join-Path $env:ProgramFiles "BusinessOS\Darmaltoon"
$dataRoot = Join-Path $env:ProgramData "BusinessOS\Pharmacy"
$verifyPath = Join-Path $env:TEMP "darmaltoon-install-verify.json"
$installerScript = Join-Path $repoRoot "packaging/windows/Darmaltoon.iss"

function Find-Iscc {
    if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_COMPILER) -and (Test-Path $env:INNO_SETUP_COMPILER)) {
        return $env:INNO_SETUP_COMPILER
    }
    foreach ($path in @(
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Inno Setup 7\ISCC.exe"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Inno Setup 6\ISCC.exe"))) {
        if (Test-Path $path) { return $path }
    }
    throw "ISCC.exe not found."
}

function Install-Setup([string]$setup) {
    $process = Start-Process $setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-" -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010) {
        throw "Setup failed with exit code $($process.ExitCode): $setup"
    }
}

function Uninstall-Darmaltoon {
    $uninstaller = Get-ChildItem $installDir -Filter "unins*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $uninstaller) { throw "Darmaltoon uninstaller was not found." }
    $process = Start-Process $uninstaller.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010) { throw "Uninstall failed with exit code $($process.ExitCode)." }
}

function Verify-InstalledApp([string]$expectedMode) {
    $exe = Join-Path $installDir "Darmaltoon.exe"
    if (-not (Test-Path $exe)) { throw "Darmaltoon.exe is missing after installation." }

    if (Test-Path $verifyPath) { Remove-Item $verifyPath -Force }
    $process = Start-Process $exe -ArgumentList "--verify-install=$verifyPath" -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Darmaltoon launch verification failed with exit code $($process.ExitCode)." }
    if (-not (Test-Path $verifyPath)) { throw "Darmaltoon did not write installation verification output." }

    $verify = Get-Content $verifyPath -Raw | ConvertFrom-Json
    if ($verify.product -ne "Darmaltoon") { throw "Unexpected product identity." }
    if ($verify.version -ne $Version) { throw "Installed version '$($verify.version)' does not match '$Version'." }
    if ($verify.deployment_hint -ne $expectedMode) { throw "Deployment hint '$($verify.deployment_hint)' does not match '$expectedMode'." }

    $marker = (Get-Content (Join-Path $installDir "deployment-default.txt") -Raw).Trim()
    if ($marker -ne $expectedMode) { throw "Installer deployment marker is incorrect." }

    $shortcut = Get-ChildItem (Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs") -Filter "Darmaltoon.lnk" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $shortcut) { throw "Start Menu shortcut was not installed." }

    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($fileVersion.ProductName -ne "Darmaltoon") { throw "Windows version metadata does not identify Darmaltoon." }
}

function Seed-PreservedData {
    New-Item -ItemType Directory -Force -Path $dataRoot, (Join-Path $dataRoot "Config"), (Join-Path $dataRoot "licensing") | Out-Null
    Set-Content (Join-Path $dataRoot "pharmacy.db") "KEEP-DATABASE" -NoNewline
    Set-Content (Join-Path $dataRoot "Config\network.json") "KEEP-CONFIG" -NoNewline
    Set-Content (Join-Path $dataRoot "licensing\activation.bin") "KEEP-LICENSE" -NoNewline
}

function Verify-ProgramDataPreserved {
    if ((Get-Content (Join-Path $dataRoot "pharmacy.db") -Raw) -ne "KEEP-DATABASE") { throw "pharmacy.db was modified or removed." }
    if ((Get-Content (Join-Path $dataRoot "Config\network.json") -Raw) -ne "KEEP-CONFIG") { throw "network.json was modified or removed." }
    if ((Get-Content (Join-Path $dataRoot "licensing\activation.bin") -Raw) -ne "KEEP-LICENSE") { throw "activation data was modified or removed." }
}

$standaloneSetup = Join-Path $artifacts "Darmaltoon-Standalone-Setup-$Version-win-x64.exe"
$serverSetup = Join-Path $artifacts "Darmaltoon-MainServer-Setup-$Version-win-x64.exe"
$clientSetup = Join-Path $artifacts "Darmaltoon-ClientTerminal-Setup-$Version-win-x64.exe"
foreach ($file in @($standaloneSetup, $serverSetup, $clientSetup)) {
    if (-not (Test-Path $file)) { throw "Release artifact missing: $file" }
}

# Clean standalone install.
Install-Setup $standaloneSetup
Verify-InstalledApp "Standalone"
Seed-PreservedData
Uninstall-Darmaltoon
Verify-ProgramDataPreserved
if (Test-Path (Join-Path $installDir "Darmaltoon.exe")) { throw "Application files remained after uninstall." }

# In-place upgrade from synthetic older version to release version.
$iscc = Find-Iscc
$baselineDir = Join-Path $env:TEMP "darmaltoon-baseline"
New-Item -ItemType Directory -Force -Path $baselineDir | Out-Null
$payload = Join-Path $artifacts "_work\Standalone\payload"
& $iscc "/DMyAppVersion=0.9.0" "/DSourceDir=$payload" "/DDeploymentMode=Standalone" "/DModeLabel=Standalone" "/DOutputDir=$baselineDir" "/DOutputBaseFilename=Darmaltoon-Baseline-Setup" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Baseline installer compilation failed." }
$baselineSetup = Join-Path $baselineDir "Darmaltoon-Baseline-Setup.exe"
Install-Setup $baselineSetup
Verify-ProgramDataPreserved
Install-Setup $standaloneSetup
Verify-InstalledApp "Standalone"
Verify-ProgramDataPreserved
Uninstall-Darmaltoon
Verify-ProgramDataPreserved

# Main Server package.
Install-Setup $serverSetup
Verify-InstalledApp "Server"
$service = Get-Service -Name "BusinessOS Pharmacy Local Server" -ErrorAction Stop
if ($service.StartType -ne "Automatic") { throw "Main Server service is not configured for automatic startup." }
if (-not (Test-Path (Join-Path $installDir "Server\BusinessOS.Pharmacy.LocalServer.exe"))) { throw "Main Server executable is missing." }
Verify-ProgramDataPreserved
Uninstall-Darmaltoon
Start-Sleep -Seconds 1
if (Get-Service -Name "BusinessOS Pharmacy Local Server" -ErrorAction SilentlyContinue) { throw "Main Server service remained after uninstall." }
Verify-ProgramDataPreserved

# Client Terminal package must not install server service.
Install-Setup $clientSetup
Verify-InstalledApp "Client"
if (Get-Service -Name "BusinessOS Pharmacy Local Server" -ErrorAction SilentlyContinue) { throw "Client Terminal installed the Main Server service." }
Verify-ProgramDataPreserved
Uninstall-Darmaltoon
Verify-ProgramDataPreserved

Write-Host "Windows installer smoke tests passed: clean install, launch, in-place upgrade, uninstall, shortcuts, service behavior and ProgramData preservation."
