; Inno Setup Script for Monitor Brightness Controller
; Compiled with Inno Setup 6.x via: ISCC.exe /DMyAppVersion=1.5.0 MonitorBrightnessControllerSetup.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "Monitor Brightness Controller"
#define MyAppExeName "MonitorBrightnessController.exe"
#define MyAppPublisher "Monitor Brightness Controller"

[Setup]
AppId={{B7E4F2A1-9C3D-4A8E-B6F5-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\MonitorBrightnessController
OutputDir=builds\v{#MyAppVersion}
OutputBaseFilename=MonitorBrightnessControllerSetup-{#MyAppVersion}
UninstallDisplayName={#MyAppName}
DefaultGroupName={#MyAppName}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=MonitorBrightnessController\Assets\app.ico
WizardStyle=modern
PrivilegesRequired=lowest
UsePreviousAppDir=yes
UsePreviousTasks=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe

[Files]
Source: "MonitorBrightnessController\bin\Release\net8.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "startmenu"; Description: "Create a Start Menu shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a Desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startwithwindows"; Description: "Start with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueName: "MonitorBrightnessController"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" --silent"; Flags: uninsdeletevalue; Tasks: startwithwindows
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueName: "MonitorBrightnessController"; ValueType: binary; ValueData: "02 00 00 00 00 00 00 00 00 00 00 00"; Flags: uninsdeletevalue; Tasks: startwithwindows

[UninstallDelete]
Type: files; Name: "{app}\{#MyAppExeName}"

[Code]
const
  WM_CLOSE = $0010;

function FindWindowByTitle(lpClassName: String; lpWindowName: String): HWND;
  external 'FindWindowW@user32.dll stdcall';
function PostMessage(hWnd: HWND; Msg: UINT; wParam: Longint; lParam: Longint): BOOL;
  external 'PostMessageW@user32.dll stdcall';

function IsAppRunning: Boolean;
var
  Hwnd: HWND;
begin
  Hwnd := FindWindowByTitle('', 'Monitor Brightness Controller');
  Result := (Hwnd <> 0);
end;

function CloseApplications: Boolean;
var
  Hwnd: HWND;
  WaitCount: Integer;
begin
  Result := True;
  
  // Try to find and close the running application
  Hwnd := FindWindowByTitle('', 'Monitor Brightness Controller');
  if Hwnd <> 0 then
  begin
    PostMessage(Hwnd, WM_CLOSE, 0, 0);
    
    // Wait up to 5 seconds for the process to exit
    WaitCount := 0;
    while WaitCount < 10 do
    begin
      Sleep(500);
      WaitCount := WaitCount + 1;
      Hwnd := FindWindowByTitle('', 'Monitor Brightness Controller');
      if Hwnd = 0 then
        Exit;
    end;
    
    // Process still running after 5 seconds - prompt user
    if MsgBox('Monitor Brightness Controller is still running. Please close it manually and click OK to continue, or click Cancel to abort.', 
              mbConfirmation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
    end;
  end;
end;

function GetPreviousInstallDir: String;
var
  PrevDir: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1',
    'InstallLocation', PrevDir) then
  begin
    Result := PrevDir;
  end
  else if RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1',
    'InstallLocation', PrevDir) then
  begin
    Result := PrevDir;
  end;
end;

function IsUpgradeInstall: Boolean;
begin
  Result := GetPreviousInstallDir <> '';
end;

function ShouldPreCheckStartWithWindows: Boolean;
var
  RegValue: String;
begin
  Result := False;
  if RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
    'MonitorBrightnessController', RegValue) then
  begin
    Result := True;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := CloseApplications;
end;

procedure InitializeWizard;
var
  PrevDir: String;
begin
  // Pre-fill installation directory from previous install
  PrevDir := GetPreviousInstallDir;
  if PrevDir <> '' then
  begin
    WizardForm.DirEdit.Text := PrevDir;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Settings.json at %LOCALAPPDATA%\MonitorBrightnessController is preserved
  // automatically since the installer does not touch that directory.
end;

function InitializeUninstall: Boolean;
begin
  Result := CloseApplications;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RegValue: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Remove Auto_Start_Registry_Entry if it exists
    if RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
      'MonitorBrightnessController', RegValue) then
    begin
      RegDeleteValue(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
        'MonitorBrightnessController');
    end;
  end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'InstallDir', WizardDirValue);
end;
