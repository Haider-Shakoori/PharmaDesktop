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

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BusinessOS\Darmaltoon"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Darmaltoon"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Registry]
Root: HKLM; Subkey: "Software\BusinessOS\Darmaltoon"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\BusinessOS\Darmaltoon"; ValueType: string; ValueName: "DeploymentPackage"; ValueData: "{#DeploymentMode}"; Flags: uninsdeletevalue

#if DeploymentMode == "Server"
[Run]
Filename: "{sys}\sc.exe"; Parameters: "create ""{#MyServiceName}"" binPath= """"{#MyServiceExe}"""" start= auto DisplayName= ""Darmaltoon Main Pharmacy Server"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "config ""{#MyServiceName}"" binPath= """"{#MyServiceExe}"""" start= auto DisplayName= ""Darmaltoon Main Pharmacy Server"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description ""{#MyServiceName}"" ""Darmaltoon authoritative local pharmacy LAN server."""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure ""{#MyServiceName}"" reset= 86400 actions= restart/5000/restart/15000/restart/30000"; Flags: runhidden waituntilterminated
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
