#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must be defined
#endif
#ifndef DeploymentMode
  #define DeploymentMode "Standalone"
#endif
#ifndef ModeLabel
  #define ModeLabel DeploymentMode
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef OutputBaseFilename
  #define OutputBaseFilename "Darmaltoon-Setup"
#endif
#ifndef AppIconFile
  #define AppIconFile ""
#endif
#ifndef DisableInstallerLicenseGate
  #if DeploymentMode != "Client"
    #ifndef ActivationBootstrapFile
      #error ActivationBootstrapFile must be defined when the installer license gate is enabled
    #endif
  #endif
#endif

#define MyAppName "Darmaltoon"
#define MyPublisher "BusinessOS.af"
#define MyAppExeName "Darmaltoon.exe"
#define MyServiceName "BusinessOS Pharmacy Local Server"
#define MyServiceExe "{app}\Server\BusinessOS.Pharmacy.LocalServer.exe"

[Setup]
AppId={{5D780C77-B117-4DA1-974A-66E92FF44890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion} - {#ModeLabel}
AppPublisher={#MyPublisher}
AppPublisherURL=https://businessos.af
AppSupportURL=https://businessos.af
DefaultDirName={autopf64}\BusinessOS\Darmaltoon
DefaultGroupName=BusinessOS
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableReadyPage=no
DisableFinishedPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
SetupLogging=yes
UninstallDisplayName=Darmaltoon - {#ModeLabel}
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
ChangesAssociations=no
ChangesEnvironment=no
MinVersion=10.0.17763
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyPublisher}
VersionInfoDescription=Darmaltoon Pharmacy Management Setup
VersionInfoProductName=Darmaltoon
VersionInfoProductVersion={#MyAppVersion}
AppComments=Premium pharmacy management by BusinessOS.af
AppContact=BusinessOS Support
AppCopyright=Copyright (c) BusinessOS.af
#if AppIconFile != ""
SetupIconFile={#AppIconFile}
#endif

[Dirs]
Name: "{commonappdata}\BusinessOS\Pharmacy"; Permissions: users-modify; Flags: uninsneveruninstall

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifndef DisableInstallerLicenseGate
#if DeploymentMode != "Client"
Source: "{#ActivationBootstrapFile}"; Flags: dontcopy
#endif
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts"; Flags: unchecked

[Icons]
Name: "{autoprograms}\BusinessOS\Darmaltoon"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Darmaltoon"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\BusinessOS\Darmaltoon"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\BusinessOS\Darmaltoon"; ValueType: string; ValueName: "DeploymentPackage"; ValueData: "{#DeploymentMode}"; Flags: uninsdeletevalue

[Run]
Filename: "{sys}\icacls.exe"; Parameters: """{commonappdata}\BusinessOS\Pharmacy"" /inheritance:e /grant *S-1-5-32-545:(OI)(CI)M /T /C"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Darmaltoon"; Flags: nowait postinstall skipifsilent

#if DeploymentMode == "Server"
[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Server\register-service.ps1"" -ExecutablePath ""{#MyServiceExe}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Darmaltoon Local Server API (Private LAN)"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Darmaltoon Local Server API (Private LAN)"" dir=in action=allow protocol=TCP localport=5280 profile=private remoteip=localsubnet"; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Darmaltoon Local Server Discovery (Private LAN)"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Darmaltoon Local Server Discovery (Private LAN)"" dir=in action=allow protocol=UDP localport=5281 profile=private remoteip=localsubnet"; Flags: runhidden waituntilterminated
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ""$p=Join-Path $env:ProgramData 'BusinessOS\Pharmacy\Config\network.json'; if(Test-Path $p){{ try{{ $c=Get-Content $p -Raw|ConvertFrom-Json; if($c.mode -eq 1){{ & sc.exe start '{#MyServiceName}' | Out-Null }} }}catch{{}} }}"""; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#MyServiceName}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#MyServiceName}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Darmaltoon Local Server API (Private LAN)"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Darmaltoon Local Server Discovery (Private LAN)"""; Flags: runhidden waituntilterminated
#endif

[Code]
var
  ServiceWasRunning: Boolean;
#ifndef DisableInstallerLicenseGate
#if DeploymentMode != "Client"
  LicensePage: TInputQueryWizardPage;
  ExistingActivationVerified: Boolean;
  LicenseActivationSucceeded: Boolean;
#endif
#endif

#ifndef DisableInstallerLicenseGate
#if DeploymentMode != "Client"
function RunActivationBootstrap(Arguments: String; var StatusText: String): Boolean;
var
  BootstrapPath: String;
  ResultPath: String;
  ResultCode: Integer;
  ResultLines: TArrayOfString;
begin
  Result := False;
  StatusText := '';
  BootstrapPath := ExpandConstant('{tmp}\Darmaltoon.ActivationBootstrap.exe');
  ResultPath := ExpandConstant('{tmp}\darmaltoon-activation-result.txt');

  if not FileExists(BootstrapPath) then
    ExtractTemporaryFile('Darmaltoon.ActivationBootstrap.exe');

  DeleteFile(ResultPath);

  if not Exec(
    BootstrapPath,
    Arguments + ' --result-file "' + ResultPath + '"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    StatusText := 'Darmaltoon could not start the secure license verifier.';
    Exit;
  end;

  if FileExists(ResultPath) and
     LoadStringsFromFile(ResultPath, ResultLines) and
     (GetArrayLength(ResultLines) > 0) then
    StatusText := ResultLines[0];

  DeleteFile(ResultPath);

  if (ResultCode = 0) then
  begin
    Result := True;
    Exit;
  end;

  if StatusText = '' then
    StatusText := 'The Windows activation could not be verified. Contact Darmaltoon support if this key was already used.';
end;

function VerifyExistingActivation(): Boolean;
var
  StatusText: String;
begin
  Result := RunActivationBootstrap('verify', StatusText);
end;

function ActivateInstallerLicense(LicenseKey: String; var StatusText: String): Boolean;
var
  LicensePath: String;
begin
  LicensePath := ExpandConstant('{tmp}\darmaltoon-one-time-license.key');
  DeleteFile(LicensePath);

  if not SaveStringToFile(LicensePath, Trim(LicenseKey), False) then
  begin
    StatusText := 'Darmaltoon Setup could not prepare the license key for secure verification.';
    Result := False;
    Exit;
  end;

  Result := RunActivationBootstrap(
    'activate --license-file "' + LicensePath + '"',
    StatusText);

  DeleteFile(LicensePath);
end;
#endif
#endif

procedure InitializeWizard;
begin
  WizardForm.Caption := 'Darmaltoon Setup';
  WizardForm.WelcomeLabel1.Caption := 'Welcome to Darmaltoon';
  WizardForm.WelcomeLabel2.Caption :=
    'Install Darmaltoon {#MyAppVersion} — {#ModeLabel}' + #13#10 + #13#10 +
    'Secure, local-first pharmacy management by BusinessOS.af.' + #13#10 +
    'Choose your shortcut preference on the next steps, then Setup will handle the rest.';

#ifndef DisableInstallerLicenseGate
#if DeploymentMode != "Client"
  ExistingActivationVerified := VerifyExistingActivation();
  LicenseActivationSucceeded := ExistingActivationVerified;

  LicensePage := CreateInputQueryPage(
    wpWelcome,
    'Activate Darmaltoon',
    'Enter the one-time Windows activation key',
    'This key is consumed on the first successful Windows activation and becomes bound to this PC. ' +
    'If Windows is reinstalled, activation data is lost, or the PC is replaced, contact Darmaltoon support for reassignment.');

  LicensePage.Add('One-time activation key:', False);
#endif
#endif
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
#ifndef DisableInstallerLicenseGate
#if DeploymentMode != "Client"
  if (PageID = LicensePage.ID) and LicenseActivationSucceeded then
    Result := True;
#endif
#endif
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  StatusText: String;
begin
  Result := True;

#ifndef DisableInstallerLicenseGate
#if DeploymentMode != "Client"
  if CurPageID = LicensePage.ID then
  begin
    if Trim(LicensePage.Values[0]) = '' then
    begin
      MsgBox(
        'Enter the one-time Windows activation key provided by Darmaltoon support.',
        mbError,
        MB_OK);
      Result := False;
      Exit;
    end;

    WizardForm.NextButton.Enabled := False;
    try
      if not ActivateInstallerLicense(LicensePage.Values[0], StatusText) then
      begin
        MsgBox(
          StatusText + #13#10 + #13#10 +
          'If this key was already used or this PC was reset, contact Darmaltoon support for a license reassignment.',
          mbError,
          MB_OK);
        Result := False;
        Exit;
      end;

      LicensePage.Values[0] := '';
      LicenseActivationSucceeded := True;
      MsgBox(
        StatusText + #13#10 + #13#10 +
        'The license is now bound to this Windows installation. Setup can continue.',
        mbInformation,
        MB_OK);
    finally
      WizardForm.NextButton.Enabled := True;
    end;
  end;
#endif
#endif
end;

function ServiceIsRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
#if DeploymentMode == "Server"
  if Exec(ExpandConstant('{sys}\sc.exe'), 'query "{#MyServiceName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := ResultCode = 0;
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
#ifndef DisableInstallerLicenseGate
#if DeploymentMode != "Client"
  if not LicenseActivationSucceeded then
  begin
    Result :=
      'Darmaltoon must be activated online before installation. ' +
      'Enter a valid one-time Windows activation key, or contact Darmaltoon support.';
    Exit;
  end;
#endif
#endif
  ServiceWasRunning := ServiceIsRunning();
#if DeploymentMode == "Server"
  if ServiceWasRunning then begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop "{#MyServiceName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1500);
  end;
#endif
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
#if DeploymentMode == "Server"
  if (CurStep = ssPostInstall) and ServiceWasRunning then
    Exec(ExpandConstant('{sys}\sc.exe'), 'start "{#MyServiceName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
#endif
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { ProgramData\BusinessOS\Pharmacy is intentionally never deleted here. }
end;
