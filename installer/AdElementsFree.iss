#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppName "Ad Elements Free"
#define AppId "AdElementsFree.NoC486"
#define AppMutex "Local\AdElementsFree"
#ifdef TestBuild
  #define AppName "Ad Elements Free Installer Test"
  #define AppId "AdElementsFree.NoC486.InstallerTest"
  #define AppMutex "Local\AdElementsFree.InstallerTest"
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NoC486
AppPublisherURL=https://github.com/NoC486/Ad-Elements-Free
AppSupportURL=https://github.com/NoC486/Ad-Elements-Free/issues
AppUpdatesURL=https://github.com/NoC486/Ad-Elements-Free/releases
DefaultDirName={code:GetDefaultInstallDir}
DisableDirPage=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex={#AppMutex}
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\AdElementsFree.exe
SetupIconFile=..\src\AdElementsFree\Assets\App.ico
InfoBeforeFile=INSTALL.txt
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir=..\artifacts\release
OutputBaseFilename=Ad-Elements-Free-{#AppVersion}-win-x64-setup

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "INSTALL.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\AdElementsFree.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\AdElementsFree.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AdElementsFree.exe"; Description: "Launch Ad Elements Free (system tray)"; Flags: nowait postinstall skipifsilent

[Code]
function GetInstallDriveType(RootPath: String): Cardinal;
  external 'GetDriveTypeW@kernel32.dll stdcall';

function GetDefaultInstallDir(Param: String): String;
var
  Index: Integer;
  RootPath: String;
begin
  { Prefer D:, then other fixed non-C drives. Exclude removable/network drives. }
  for Index := Ord('D') to Ord('Z') do
  begin
    RootPath := Chr(Index) + ':\';
    if GetInstallDriveType(RootPath) = 3 then
    begin
      Result := RootPath + 'Program Files\{#AppName}';
      Exit;
    end;
  end;
  { Computers with no secondary fixed drive still get a usable default. }
  Result := ExpandConstant('{localappdata}\Programs\{#AppName}');
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not UninstallSilent then
    Result := MsgBox('Before uninstalling, turn OFF all providers in Ad Elements Free to restore the shortcuts it changed, then exit the app from the system tray.' + #13#10 + #13#10 +
      'Settings, logs and shortcut recovery records in your Local AppData folder will be preserved. Continue uninstalling?', mbConfirmation, MB_YESNO) = IDYES;
end;
