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

function Install-Setup([string]$setup, [switch]$CreateDesktopShortcut) {
    $arguments = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-")
    if ($CreateDesktopShortcut) {
        $arguments += "/TASKS=desktopicon"
    }
    $process = Start-Process $setup -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010) {
        throw "Setup failed with exit code $($process.ExitCode): $setup"
    }
}

function Verify-FirstInteractiveLaunch {
    $exe = Join-Path $installDir "Darmaltoon.exe"
    $process = Start-Process $exe -PassThru
    Start-Sleep -Seconds 6
    if ($process.HasExited) {
        throw "Darmaltoon exited during first interactive launch with exit code $($process.ExitCode)."
    }
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
}

function Verify-DesktopShortcut([bool]$expected) {
    $desktopRoots = @(
        [Environment]::GetFolderPath("CommonDesktopDirectory"),
        [Environment]::GetFolderPath("DesktopDirectory")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $shortcut = $desktopRoots |
        ForEach-Object { Join-Path $_ "Darmaltoon.lnk" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if ($expected -and [string]::IsNullOrWhiteSpace($shortcut)) {
        throw "Desktop shortcut was requested but not installed."
    }

    if (-not $expected -and -not [string]::IsNullOrWhiteSpace($shortcut)) {
        throw "Desktop shortcut was installed even though the optional task was not selected."
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

    $settingsPath = Join-Path $installDir "appsettings.json"
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    try {
        $licenseBytes = [Convert]::FromBase64String(([string]$settings.BusinessOS.Licensing.SigningPublicKey).Trim())
    }
    catch {
        throw "Installed licensing signing public key is not valid Base64."
    }
    if ($licenseBytes.Length -ne 32) { throw "Installed licensing signing public key is not 32 bytes." }

    try {
        $rsa = [Security.Cryptography.RSA]::Create()
        $rsa.ImportFromPem(([string]$settings.BusinessOS.Updater.SigningPublicKeyPem).Trim())
        $rsa.Dispose()
    }
    catch {
        throw "Installed updater signing public key is not a valid RSA public key."
    }
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

function Verify-ProgramDataWritableAcl {
    if (-not (Test-Path $dataRoot)) { throw "Darmaltoon ProgramData directory is missing." }

    $usersSid = New-Object System.Security.Principal.SecurityIdentifier(
        [System.Security.Principal.WellKnownSidType]::BuiltinUsersSid,
        $null)
    $acl = Get-Acl $dataRoot
    $hasModify = $acl.Access | Where-Object {
        $_.IdentityReference -eq $usersSid.Translate([System.Security.Principal.NTAccount]) -and
        $_.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Allow -and
        (($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::Modify) -ne 0) -and
        (($_.InheritanceFlags -band [System.Security.AccessControl.InheritanceFlags]::ContainerInherit) -ne 0) -and
        (($_.InheritanceFlags -band [System.Security.AccessControl.InheritanceFlags]::ObjectInherit) -ne 0)
    }

    if (-not $hasModify) {
        throw "Built-in Users do not have inherited Modify access to $dataRoot."
    }
}

$productionSetups = @(
    (Join-Path $artifacts "Darmaltoon-Standalone-Setup-$Version-win-x64.exe"),
    (Join-Path $artifacts "Darmaltoon-MainServer-Setup-$Version-win-x64.exe"),
    (Join-Path $artifacts "Darmaltoon-ClientTerminal-Setup-$Version-win-x64.exe")
)
foreach ($file in $productionSetups) {
    if (-not (Test-Path $file)) { throw "Production release artifact missing: $file" }
}

# Customer Standalone/Main Server installers enforce online one-time activation.
# CI uses separately compiled gate-disabled installers for packaging smoke tests so
# no production license key or bypass is embedded in the shipped artifacts.
$smokeInstallerRoot = Join-Path $artifacts "_work\smoke-installers"
$standaloneSetup = Join-Path $smokeInstallerRoot "Darmaltoon-Standalone-Smoke-Setup-$Version-win-x64.exe"
$serverSetup = Join-Path $smokeInstallerRoot "Darmaltoon-MainServer-Smoke-Setup-$Version-win-x64.exe"
$clientSetup = Join-Path $smokeInstallerRoot "Darmaltoon-ClientTerminal-Smoke-Setup-$Version-win-x64.exe"
foreach ($file in @($standaloneSetup, $serverSetup, $clientSetup)) {
    if (-not (Test-Path $file)) { throw "Smoke-test installer missing: $file" }
}

# Clean standalone install with the optional desktop shortcut selected.
Install-Setup $standaloneSetup -CreateDesktopShortcut
Verify-InstalledApp "Standalone"
Verify-DesktopShortcut $true
Verify-ProgramDataWritableAcl
Verify-FirstInteractiveLaunch
Seed-PreservedData
Uninstall-Darmaltoon
Verify-ProgramDataPreserved
if (Test-Path (Join-Path $installDir "Darmaltoon.exe")) { throw "Application files remained after uninstall." }

# In-place upgrade from synthetic older version to release version.
$iscc = Find-Iscc
$baselineDir = Join-Path $env:TEMP "darmaltoon-baseline"
New-Item -ItemType Directory -Force -Path $baselineDir | Out-Null
$payload = Join-Path $artifacts "_work\Standalone\payload"
& $iscc "/DMyAppVersion=0.9.0" "/DSourceDir=$payload" "/DDeploymentMode=Standalone" "/DModeLabel=Standalone" "/DOutputDir=$baselineDir" "/DOutputBaseFilename=Darmaltoon-Baseline-Setup" "/DDisableInstallerLicenseGate=1" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Baseline installer compilation failed." }
$baselineSetup = Join-Path $baselineDir "Darmaltoon-Baseline-Setup.exe"
Install-Setup $baselineSetup
Verify-ProgramDataPreserved
Install-Setup $standaloneSetup
Verify-InstalledApp "Standalone"
Verify-ProgramDataWritableAcl
Verify-ProgramDataPreserved
Uninstall-Darmaltoon
Verify-ProgramDataPreserved

# Main Server package: optional desktop shortcut remains unchecked by default.
Install-Setup $serverSetup
Verify-InstalledApp "Server"
Verify-DesktopShortcut $false
Verify-ProgramDataWritableAcl
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
Verify-DesktopShortcut $false
Verify-ProgramDataWritableAcl
if (Get-Service -Name "BusinessOS Pharmacy Local Server" -ErrorAction SilentlyContinue) { throw "Client Terminal installed the Main Server service." }
Verify-ProgramDataPreserved
Uninstall-Darmaltoon
Verify-ProgramDataPreserved

Write-Host "Windows installer smoke tests passed: clean install, launch, in-place upgrade, uninstall, shortcuts, service behavior and ProgramData preservation."
