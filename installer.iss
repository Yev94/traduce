#define AppVersion "1.1.0"

[Setup]
AppId={{C6EA322A-171B-48A9-B728-D9275F110E2F}
AppName=Traduce
AppVersion={#AppVersion}
AppPublisher=Traduce contributors
AppPublisherURL=https://github.com/Yev94/traduce
AppSupportURL=https://github.com/Yev94/traduce/issues
AppUpdatesURL=https://github.com/Yev94/traduce/releases/latest
DefaultDirName={localappdata}\Programs\Traduce
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
OutputDir=release
OutputBaseFilename=Traduce-Setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\Traduce.exe
SetupIconFile=assets\traduce.ico
ShowLanguageDialog=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:DesktopTask}"; Flags: unchecked

[CustomMessages]
spanish.StartupTask=Iniciar Traduce al entrar en Windows
english.StartupTask=Start Traduce with Windows
spanish.DesktopTask=Crear acceso directo en el escritorio
english.DesktopTask=Create a desktop shortcut
spanish.LaunchApp=Abrir Traduce
english.LaunchApp=Launch Traduce
spanish.CloseApp=Cierra Traduce desde el icono junto al reloj y vuelve a intentarlo.
english.CloseApp=Close Traduce from the system tray and try again.
spanish.NeedFramework=Traduce necesita .NET Framework 4.8. Instálalo desde Windows Update o https://dotnet.microsoft.com/download/dotnet-framework/net48
english.NeedFramework=Traduce requires .NET Framework 4.8. Install it from Windows Update or https://dotnet.microsoft.com/download/dotnet-framework/net48

[Files]
Source: "dist\Traduce.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\Traduce.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.es.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\logo.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "docs\window-es.png"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "docs\window-en.png"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "docs\settings-es.png"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "docs\settings-en.png"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Traduce"; Filename: "{app}\Traduce.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Traduce"; Filename: "{app}\Traduce.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\Traduce"; Filename: "{app}\Traduce.exe"; Parameters: "--background"; WorkingDir: "{app}"; Tasks: startup

[InstallDelete]
; Remove only this app's old shortcut if startup was deselected on an upgrade.
Type: files; Name: "{userstartup}\Traduce.lnk"; Check: not WizardIsTaskSelected('startup')

[Run]
Filename: "{app}\Traduce.exe"; Parameters: "--background"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
function CloseTraduce(): Boolean;
var
  ExitCode, Attempt: Integer;
  AppExe: String;
begin
  Result := True;
  AppExe := ExpandConstant('{app}\Traduce.exe');
  if FileExists(AppExe) then
    Exec(AppExe, '--quit', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode);
  for Attempt := 1 to 60 do
  begin
    if not CheckForMutexes('Local\Traduce.Desktop,Local\Yev.Traduce.Desktop') then exit;
    Sleep(100);
  end;
  Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CloseTraduce() then
    Result := CustomMessage('CloseApp');
end;

function InitializeUninstall(): Boolean;
begin
  Result := CloseTraduce();
  if not Result then
    MsgBox(CustomMessage('CloseApp'), mbInformation, MB_OK);
end;

function InitializeSetup(): Boolean;
var
  NetRelease: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', NetRelease) and (NetRelease >= 528040);
  if not Result then
    MsgBox(CustomMessage('NeedFramework'), mbInformation, MB_OK);
end;
