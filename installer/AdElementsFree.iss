#ifndef AppVersion
  #define AppVersion "0.3.0"
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
UsePreviousTasks=no
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
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
OutputDir=..\artifacts\release\{#AppVersion}
OutputBaseFilename=Ad-Elements-Free-{#AppVersion}-win-x64-setup

[Tasks]
Name: "launchnow"; Description: "立即运行 Ad Elements Free（安装完成后）"; GroupDescription: "安装选项："
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "安装选项："; Flags: unchecked

[Files]
Source: "..\artifacts\publish\{#AppVersion}\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "INSTALL.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\AdElementsFree.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\AdElementsFree.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AdElementsFree.exe"; Parameters: "--show-window"; Flags: nowait skipifsilent runasoriginaluser; Tasks: launchnow

#ifdef TestBuild
[UninstallDelete]
Type: files; Name: "{app}\Rules\KOOK\style.css.bak"
#endif

[Code]
function GetInstallDriveType(RootPath: String): Cardinal;
  external 'GetDriveTypeW@kernel32.dll stdcall';

function GetDefaultInstallDir(Param: String): String;
var
  Index: Integer;
  RootPath: String;
begin
  if not IsAdminInstallMode then
  begin
    Result := ExpandConstant('{localappdata}\Programs\{#AppName}');
    Exit;
  end;
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
  Result := ExpandConstant('{autopf}\{#AppName}');
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not UninstallSilent then
    Result := MsgBox('Before uninstalling, disable startup in Settings and turn OFF all providers in Ad Elements Free to restore the shortcuts it changed, then exit the app from the system tray.' + #13#10 + #13#10 +
      'Settings, logs and shortcut recovery records in your Local AppData folder will be preserved. Continue uninstalling?', mbConfirmation, MB_YESNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
#ifndef TestBuild
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AdElementsFree', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\AdElementsFree.exe') + '" --startup') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AdElementsFree');
#endif
end;
