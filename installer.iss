#define AppVersion "1.0.0"

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
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Iniciar Traduce al entrar en Windows / Start with Windows"; Flags: checkedonce
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio / Desktop shortcut"; Flags: unchecked

[Files]
Source: "dist\Traduce.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\Traduce.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Traduce"; Filename: "{app}\Traduce.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Traduce"; Filename: "{app}\Traduce.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\Traduce"; Filename: "{app}\Traduce.exe"; Parameters: "--background"; WorkingDir: "{app}"; Tasks: startup

[InstallDelete]
; Remove only this app's old shortcut if startup was deselected on an upgrade.
Type: files; Name: "{userstartup}\Traduce.lnk"; Check: not WizardIsTaskSelected('startup')

[Run]
Filename: "{app}\Traduce.exe"; Parameters: "--background"; Description: "Abrir Traduce / Launch Traduce"; Flags: nowait postinstall skipifsilent

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
    Result := 'Cierra Traduce desde el icono junto al reloj y vuelve a intentarlo. / Close Traduce from the system tray and try again.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := CloseTraduce();
  if not Result then
    MsgBox('Cierra Traduce desde el icono junto al reloj. / Close Traduce from the system tray.', mbInformation, MB_OK);
end;

function InitializeSetup(): Boolean;
var
  NetRelease: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', NetRelease) and (NetRelease >= 528040);
  if not Result then
    MsgBox('Traduce necesita .NET Framework 4.8. Instálalo desde Windows Update o https://dotnet.microsoft.com/download/dotnet-framework/net48', mbInformation, MB_OK);
end;
