; Branded per-user installer. Protocol identifiers and the DPAPI profile remain compatible.
#ifndef AppVersion
  #error AppVersion must be supplied from AgenteFS80H.csproj (for example /DAppVersion=1.3.0).
#endif
#define AppName "Central Biométrica"
#define AppExe "AgenteFS80H.exe"

[Setup]
AppId=FutureplaceAgenteFS80H
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Future Place Coworking
AppPublisherURL=https://central.futureplacecoworking.com.br
AppSupportURL=https://central.futureplacecoworking.com.br
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription=Instalação da Central Biométrica
DefaultDirName={localappdata}\Programs\Futureplace\AgenteFS80H
DefaultGroupName=Futureplace
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\AgenteFS80H\Assets\Reader.ico
WizardSmallImageFile=..\artifacts\setup-brand.bmp
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
DisableFinishedPage=no
UsePreviousAppDir=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=no
RestartApplications=no
SetupLogging=yes
OutputDir=..\artifacts\setup
OutputBaseFilename=Central-Biometrica-Setup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "..\artifacts\bundle\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\bundle\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\bundle\LEIA-ME.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\bundle\build-info.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Futureplace\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Comment: "Leitura de digitais e autorização pela Central"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Comment: "Abrir a Central Biométrica"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FutureplaceAgenteFS80H"; ValueData: """{app}\{#AppExe}"" --background"; Flags: uninsdeletevalue; Check: ShouldRegisterStartup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir a Central Biométrica"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Flags: nowait; Check: RestartAfterSilentUpdate

[Code]
var
  StartupChoiceInitialized, RegisterStartup: Boolean;
  LegacyTargetExecutable: String;
  LegacyCandidateWindow: HWND;
  LegacyCandidateProcessId: LongWord;
  LegacyHasVisibleForms: Boolean;

function NativeEnumWindows(Callback: NativeInt; Parameter: NativeInt): Boolean;
  external 'EnumWindows@user32.dll stdcall';
function NativeGetWindowThreadProcessId(Window: HWND; var ProcessId: LongWord): LongWord;
  external 'GetWindowThreadProcessId@user32.dll stdcall';
function NativeGetClassName(Window: HWND; Text: String; Maximum: Integer): Integer;
  external 'GetClassNameW@user32.dll stdcall';
function NativeGetWindowText(Window: HWND; Text: String; Maximum: Integer): Integer;
  external 'GetWindowTextW@user32.dll stdcall';
function NativeGetDlgItemText(Window: HWND; ItemId: Integer; Text: String; Maximum: Integer): Integer;
  external 'GetDlgItemTextW@user32.dll stdcall';
function NativeGetDlgItem(Window: HWND; ItemId: Integer): HWND;
  external 'GetDlgItem@user32.dll stdcall';
function NativeIsWindowVisible(Window: HWND): Boolean;
  external 'IsWindowVisible@user32.dll stdcall';
function NativeIsWindowEnabled(Window: HWND): Boolean;
  external 'IsWindowEnabled@user32.dll stdcall';
function NativePostMessage(Window: HWND; Message: Cardinal; WParam, LParam: NativeInt): Boolean;
  external 'PostMessageW@user32.dll stdcall';

procedure InitializeStartupChoice;
var
  ExistingCommand, ExpectedCommand: String;
begin
  if StartupChoiceInitialized then Exit;
  StartupChoiceInitialized := True;
  RegisterStartup := not FileExists(ExpandConstant('{app}\{#AppExe}'));
  if not RegisterStartup then
  begin
    ExpectedCommand := '"' + ExpandConstant('{app}\{#AppExe}') + '" --background';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'FutureplaceAgenteFS80H', ExistingCommand) then
      RegisterStartup := CompareText(ExistingCommand, ExpectedCommand) = 0;
  end;
end;

function ShouldRegisterStartup: Boolean;
begin
  InitializeStartupChoice;
  Result := RegisterStartup;
end;

function WmiQuote(Value: String): String;
begin
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '''', '\''', True);
  Result := Value;
end;

function RunningInstalledApplication(const Executable: String): Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Locator := CreateOleObject('WbemScripting.SWbemLocator');
  Service := Locator.ConnectServer('', 'root\CIMV2');
  Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''{#AppExe}'' AND ExecutablePath = ''' + WmiQuote(Executable) + '''');
  Result := Processes.Count > 0;
end;

function KnownLegacyVersion(const Executable: String): Boolean;
var
  VersionMS, VersionLS: Cardinal;
begin
  Result := GetVersionNumbers(Executable, VersionMS, VersionLS);
  if Result then
    Result := (VersionLS = 0) and
      ((VersionMS = $00010000) or (VersionMS = $00010001) or (VersionMS = $00010002));
end;

function WindowClass(Window: HWND): String;
var
  Length: Integer;
begin
  Result := StringOfChar(#0, 256);
  Length := NativeGetClassName(Window, Result, 256);
  SetLength(Result, Length);
end;

function WindowCaption(Window: HWND): String;
var
  Length: Integer;
begin
  Result := StringOfChar(#0, 512);
  Length := NativeGetWindowText(Window, Result, 512);
  SetLength(Result, Length);
end;

function IsInstalledProcess(ProcessId: LongWord): Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Locator := CreateOleObject('WbemScripting.SWbemLocator');
  Service := Locator.ConnectServer('', 'root\CIMV2');
  Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE ProcessId = ' + IntToStr(ProcessId) + ' AND ExecutablePath = ''' + WmiQuote(LegacyTargetExecutable) + '''');
  Result := Processes.Count = 1;
end;

function FindLegacyShutdownDialog(Window: HWND; Parameter: NativeInt): Integer;
var
  Text: String;
  Length: Integer;
  ProcessId: LongWord;
  OkayButton: HWND;
begin
  Result := 1;
  if not NativeIsWindowVisible(Window) then Exit;
  if WindowClass(Window) <> '#32770' then Exit;
  if WindowCaption(Window) <> 'Agente FS80H da Central' then Exit;
  Text := StringOfChar(#0, 1024);
  Length := NativeGetDlgItemText(Window, $FFFF, Text, 1024);
  SetLength(Text, Length);
  if Text <> 'Não foi possível iniciar o agente. Se os dados locais estiverem protegidos por outro usuário do Windows ou danificados, reinstale escolhendo explicitamente remover os dados e faça um novo pareamento. Nenhum dado foi apagado automaticamente.' then Exit;
  OkayButton := NativeGetDlgItem(Window, 1);
  if (OkayButton = 0) or (WindowClass(OkayButton) <> 'Button') or not NativeIsWindowEnabled(OkayButton) then Exit;
  NativeGetWindowThreadProcessId(Window, ProcessId);
  if not IsInstalledProcess(ProcessId) then Exit;
  LegacyCandidateWindow := Window;
  LegacyCandidateProcessId := ProcessId;
  Result := 0;
end;

function FindLegacyOperationalWindow(Window: HWND; Parameter: NativeInt): Integer;
var
  ProcessId: LongWord;
begin
  Result := 1;
  NativeGetWindowThreadProcessId(Window, ProcessId);
  if (ProcessId = LegacyCandidateProcessId) and NativeIsWindowVisible(Window) and
    (Pos('WindowsForms', WindowClass(Window)) = 1) then
  begin
    LegacyHasVisibleForms := True;
    Result := 0;
  end;
end;

procedure AcknowledgeKnownLegacyShutdownError(const Executable: String);
var
  OkayButton: HWND;
begin
  { Versions 1.0--1.2 close their reader successfully, but a second Dispose then
    leaves precisely this outer-catch message box. Acknowledge only that known
    error of our installed process; never dismiss arbitrary dialogs or kill it. }
  if not KnownLegacyVersion(Executable) then Exit;
  LegacyTargetExecutable := Executable;
  LegacyCandidateWindow := 0;
  LegacyCandidateProcessId := 0;
  NativeEnumWindows(CreateCallback(@FindLegacyShutdownDialog), 0);
  if LegacyCandidateWindow = 0 then Exit;
  LegacyHasVisibleForms := False;
  NativeEnumWindows(CreateCallback(@FindLegacyOperationalWindow), 0);
  if LegacyHasVisibleForms then Exit;
  OkayButton := NativeGetDlgItem(LegacyCandidateWindow, 1);
  if NativePostMessage(LegacyCandidateWindow, $0111, 1, NativeInt(OkayButton)) then
    Log('Acknowledged the known legacy shutdown error for installed process ' + IntToStr(LegacyCandidateProcessId));
end;

function StopInstalledApplication: String;
var
  Executable: String;
  ExitCode, Attempt: Integer;
begin
  Result := '';
  Executable := ExpandConstant('{app}\{#AppExe}');
  if not FileExists(Executable) then Exit;
  try
    if not RunningInstalledApplication(Executable) then Exit;
    Log('Requesting application shutdown as ' + GetUserNameString + ': ' + Executable + ' --shutdown');
    if not Exec(Executable, '--shutdown', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      Result := 'Não foi possível solicitar o encerramento da Central Biométrica. Encerre o aplicativo pela bandeja do Windows e tente novamente.';
      Exit;
    end;
    Log('Shutdown helper completed with exit code ' + IntToStr(ExitCode));
    for Attempt := 1 to 100 do
    begin
      if not RunningInstalledApplication(Executable) then Exit;
      AcknowledgeKnownLegacyShutdownError(Executable);
      Sleep(200);
    end;
    Result := 'A Central Biométrica ainda está concluindo uma operação. Encerre o aplicativo pela bandeja e tente novamente. Nenhum processo foi encerrado à força.';
  except
    Result := 'Não foi possível confirmar o encerramento da Central Biométrica. Encerre o aplicativo pela bandeja do Windows e tente novamente.';
    Log('Could not verify graceful application shutdown: ' + GetExceptionMessage);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  InitializeStartupChoice;
  Result := StopInstalledApplication;
end;

function InitializeUninstall: Boolean;
var
  Failure: String;
begin
  Failure := StopInstalledApplication;
  Result := Failure = '';
  if not Result then
  begin
    Log(Failure);
    if not UninstallSilent then MsgBox(Failure, mbError, MB_OK);
  end;
end;

function RestartAfterSilentUpdate: Boolean;
var
  I: Integer;
begin
  Result := False;
  if not WizardSilent then Exit;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/AUTORESTART') = 0 then
      Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    { Only migrate legacy registration after all application files installed successfully.
      The protected user data and the externally selected vendor DLL are never touched. }
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\FutureplaceAgenteFS80H');
    DeleteFile(ExpandConstant('{userprograms}\Futureplace\Agente FS80H.lnk'));
    DeleteFile(ExpandConstant('{app}\Uninstall.ps1'));
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  { Before the user presses Install, cancelling needs no extra confirmation. }
  if CurPageID <> wpInstalling then Confirm := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    { The app can enable startup after an install that deliberately did not write this value.
      Remove our value independently of the conditional [Registry] uninstall log. }
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'FutureplaceAgenteFS80H');
end;
