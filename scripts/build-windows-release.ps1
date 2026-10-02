param(
    [string]$Version = "1.0.0",
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "artifacts/windows",
    [switch]$RequireProductionKeys
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$desktopProject = Join-Path $repoRoot "src/BusinessOS.Pharmacy.Desktop/BusinessOS.Pharmacy.Desktop.csproj"
$serverProject = Join-Path $repoRoot "src/BusinessOS.Pharmacy.LocalServer/BusinessOS.Pharmacy.LocalServer.csproj"
$updaterProject = Join-Path $repoRoot "src/BusinessOS.Pharmacy.Updater/BusinessOS.Pharmacy.Updater.csproj"
$installerScript = Join-Path $repoRoot "packaging/windows/Darmaltoon.iss"
$appIcon = Join-Path $repoRoot "src/BusinessOS.Pharmacy.Desktop/Assets/Darmaltoon.ico"
$serviceRegistrationScript = Join-Path $repoRoot "scripts/register-darmaltoon-local-server-service.ps1"
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$workRoot = Join-Path $outputRoot "_work"
$publishRoot = Join-Path $workRoot "publish"
$desktopPublish = Join-Path $publishRoot "desktop"
$serverPublish = Join-Path $publishRoot "server"
$updaterPublish = Join-Path $publishRoot "updater"

if (Test-Path $outputRoot) { Remove-Item $outputRoot -Recurse -Force }
New-Item -ItemType Directory -Path $outputRoot, $workRoot, $publishRoot | Out-Null

function Find-Iscc {
    if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_COMPILER) -and (Test-Path $env:INNO_SETUP_COMPILER)) {
        return $env:INNO_SETUP_COMPILER
    }
    $candidates = @(
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Inno Setup 6\ISCC.exe"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Inno Setup 7\ISCC.exe")
    )
    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Assert-ReleaseVerificationKeys($settings) {
    $licenseKey = [string]$settings.BusinessOS.Licensing.SigningPublicKey
    $updateKey = [string]$settings.BusinessOS.Updater.SigningPublicKeyPem

    if ([string]::IsNullOrWhiteSpace($licenseKey) -or [string]::IsNullOrWhiteSpace($updateKey)) {
        throw "Release packaging requires the committed Darmaltoon license and update verification public keys."
    }

    try {
        $licenseBytes = [Convert]::FromBase64String($licenseKey.Trim())
    }
    catch {
        throw "BusinessOS:Licensing:SigningPublicKey in appsettings.json is not valid Base64."
    }

    if ($licenseBytes.Length -ne 32) {
        throw "BusinessOS:Licensing:SigningPublicKey must decode to exactly 32 bytes."
    }

    try {
        $rsa = [Security.Cryptography.RSA]::Create()
        $rsa.ImportFromPem($updateKey.Trim())
        $rsa.Dispose()
    }
    catch {
        throw "BusinessOS:Updater:SigningPublicKeyPem in appsettings.json is not a valid RSA public key."
    }
}

function Set-ReleaseConfiguration([string]$payloadRoot) {
    $settingsPath = Join-Path $payloadRoot "appsettings.json"
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json

    # These are public verification keys, not secrets. They are intentionally pinned
    # in the released appsettings.json. Do not let CI secret values silently replace
    # them, because an incorrectly pasted repository secret can make the desktop app
    # fail before the activation screen is shown.
    Assert-ReleaseVerificationKeys $settings

    $settings | ConvertTo-Json -Depth 12 | Set-Content -Path $settingsPath -Encoding utf8

    # Re-read the exact file that will be packaged and validate it again.
    $writtenSettings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    Assert-ReleaseVerificationKeys $writtenSettings
}

function Find-SignTool {
    $kits = Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Windows Kits/10/bin"
    if (-not (Test-Path $kits)) { return $null }
    return Get-ChildItem $kits -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Sign-File([string]$path) {
    if ([string]::IsNullOrWhiteSpace($env:DARMALTOON_SIGNING_PFX_BASE64)) { return }
    $signTool = Find-SignTool
    if ([string]::IsNullOrWhiteSpace($signTool)) { throw "Windows SignTool was not found." }
    $pfxPath = Join-Path $workRoot "codesign.pfx"
    if (-not (Test-Path $pfxPath)) {
        [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($env:DARMALTOON_SIGNING_PFX_BASE64))
    }
    & $signTool sign /fd SHA256 /td SHA256 /tr "http://timestamp.digicert.com" /f $pfxPath /p $env:DARMALTOON_SIGNING_PFX_PASSWORD $path
    if ($LASTEXITCODE -ne 0) { throw "Code signing failed for $path" }
}

$iscc = Find-Iscc
if ([string]::IsNullOrWhiteSpace($iscc)) { throw "Inno Setup compiler ISCC.exe was not found." }

if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_LICENSE_KEY)) {
    New-Item -Path "HKCU:\Software\Jordan Russell\Inno Setup" -Force | Out-Null
    Set-ItemProperty -Path "HKCU:\Software\Jordan Russell\Inno Setup" -Name LicenseKey -Value $env:INNO_SETUP_LICENSE_KEY
}

dotnet publish $desktopProject -c $Configuration -r $RuntimeIdentifier --self-contained true -p:Version=$Version -p:PublishReadyToRun=true -p:PublishSingleFile=false -o $desktopPublish
dotnet publish $updaterProject -c $Configuration -r $RuntimeIdentifier --self-contained true -p:Version=$Version -p:PublishReadyToRun=true -p:PublishSingleFile=false -o $updaterPublish
$embeddedUpdater = Join-Path $desktopPublish "Updater"
New-Item -ItemType Directory -Path $embeddedUpdater | Out-Null
Copy-Item (Join-Path $updaterPublish "*") $embeddedUpdater -Recurse -Force
dotnet publish $serverProject -c $Configuration -r $RuntimeIdentifier --self-contained true -p:Version=$Version -p:PublishReadyToRun=true -p:PublishSingleFile=false -o $serverPublish

foreach ($required in @(
    (Join-Path $desktopPublish "Darmaltoon.exe"),
    (Join-Path $updaterPublish "BusinessOS.Pharmacy.Updater.exe"),
    (Join-Path $serverPublish "BusinessOS.Pharmacy.LocalServer.exe"))) {
    if (-not (Test-Path $required)) { throw "Required Windows publish artifact is missing: $required" }
}

$modes = @(
    @{ Mode = "Standalone"; Label = "Standalone"; File = "Standalone" },
    @{ Mode = "Server"; Label = "Main Pharmacy Server"; File = "MainServer" },
    @{ Mode = "Client"; Label = "Client Terminal"; File = "ClientTerminal" }
)
$releaseFiles = @()

foreach ($item in $modes) {
    $mode = $item.Mode
    $label = $item.Label
    $fileLabel = $item.File
    $modeRoot = Join-Path $workRoot $fileLabel
    $payloadRoot = Join-Path $modeRoot "payload"
    New-Item -ItemType Directory -Path $payloadRoot | Out-Null
    Copy-Item (Join-Path $desktopPublish "*") $payloadRoot -Recurse -Force
    Set-Content -Path (Join-Path $payloadRoot "deployment-default.txt") -Value $mode -Encoding ascii

    if ($mode -eq "Server") {
        $serverTarget = Join-Path $payloadRoot "Server"
        New-Item -ItemType Directory -Path $serverTarget | Out-Null
        Copy-Item (Join-Path $serverPublish "*") $serverTarget -Recurse -Force
        Copy-Item $serviceRegistrationScript (Join-Path $serverTarget "register-service.ps1") -Force
    }

    Set-ReleaseConfiguration $payloadRoot
    Get-ChildItem $payloadRoot -Filter *.exe -Recurse | ForEach-Object { Sign-File $_.FullName }

    $setupBase = "Darmaltoon-$fileLabel-Setup-$Version-$RuntimeIdentifier"
    & $iscc "/DMyAppVersion=$Version" "/DSourceDir=$payloadRoot" "/DDeploymentMode=$mode" "/DModeLabel=$label" "/DOutputDir=$outputRoot" "/DOutputBaseFilename=$setupBase" "/DAppIconFile=$appIcon" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed for $mode." }

    $setup = Join-Path $outputRoot "$setupBase.exe"
    if (-not (Test-Path $setup)) { throw "Installer did not produce $setup" }
    Sign-File $setup

    $portable = Join-Path $outputRoot "Darmaltoon-$fileLabel-$Version-$RuntimeIdentifier.zip"
    Compress-Archive -Path (Join-Path $payloadRoot "*") -DestinationPath $portable -CompressionLevel Optimal
    $releaseFiles += $setup, $portable
}

$checksums = foreach ($file in $releaseFiles) {
    "$((Get-FileHash $file -Algorithm SHA256).Hash)  $([IO.Path]::GetFileName($file))"
}
$checksums | Set-Content (Join-Path $outputRoot "SHA256SUMS.txt") -Encoding ascii

$manifest = [ordered]@{
    product = "Darmaltoon"
    publisher = "BusinessOS.af"
    version = $Version
    runtime = $RuntimeIdentifier
    self_contained = $true
    generated_at = [DateTimeOffset]::UtcNow.ToString("O")
    artifacts = $releaseFiles | ForEach-Object {
        $info = Get-Item $_
        [ordered]@{ file = $info.Name; bytes = $info.Length; sha256 = (Get-FileHash $_ -Algorithm SHA256).Hash }
    }
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $outputRoot "release-manifest.json") -Encoding utf8

# Render the real current WPF dashboard on Windows and package it as a CI artifact.
$dashboardScreenshot = Join-Path $outputRoot "dashboard-real.png"
$dashboardScreenshotZip = Join-Path $outputRoot "dashboard-real.zip"
dotnet run --project (Join-Path $repoRoot "tools/DashboardScreenshot/DashboardScreenshot.csproj") -c Release -- $dashboardScreenshot
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dashboardScreenshot)) {
    throw "Real dashboard screenshot capture failed."
}
Compress-Archive -Path $dashboardScreenshot -DestinationPath $dashboardScreenshotZip -CompressionLevel Optimal -Force

Get-ChildItem $outputRoot -File | Sort-Object Name | Format-Table Name, Length -AutoSize
