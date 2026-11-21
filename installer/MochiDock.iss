; MochiDock beta installer (Inno Setup 6). Build with scripts\build_installer.bat, or:
;   ISCC.exe /DMyAppVersion=0.1.3-beta installer\MochiDock.iss
; The payload is the self-contained publish in ..\Publish-Installer (scripts\publish.ps1 -Installer).
; Optional Downloads tools: put ffmpeg.exe and/or yt-dlp.exe in installer\redist before compiling.

#ifndef MyAppVersion
  #define MyAppVersion "0.1.3-beta"
#endif
#define MyAppName "MochiDock"
#define MyAppExe "MochiDock.exe"
#define PublishDir "..\Publish-Installer"

#ifexist "redist\ffmpeg.exe"
  #define HaveFfmpeg
#endif
#ifexist "redist\yt-dlp.exe"
  #define HaveYtDlp
#endif

[Setup]
; Fixed AppId: never change it, upgrades and uninstall depend on it.
AppId={{6B1E3C52-8F0A-4D7B-9A2E-4C5D7E8F1A93}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=MochiDock
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SetupIconFile=..\MochiDock\icon.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
; What the user reads first; the licence list is installed as THIRD-PARTY-NOTICES.txt instead.
InfoBeforeFile=InfoBefore.txt
OutputDir=Output
OutputBaseFilename=MochiDock-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &Desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start {#MyAppName} at &logon (elevated scheduled task for the installing user)"; Flags: unchecked
; PawnIO is a separate proprietary (freeware) kernel driver. It is never bundled: this task asks winget to install the official package
; (winget checks its hash). Ticking it means the user accepts PawnIO's own licence (https://pawnio.eu).
Name: "pawnio"; Description: "Install the &PawnIO driver for a real CPU temperature (third-party kernel driver, installed with winget under its own licence; optional)"; Flags: unchecked; Check: not PawnIoInstalled
#if Defined(HaveFfmpeg) || Defined(HaveYtDlp)
Name: "tools"; Description: "Install Downloads tools (ffmpeg / yt-dlp) into the Tools folder"
#endif

[Files]
; GOWA session data lives in %LocalAppData%\MochiDock\GOWA and is never part of the payload.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
#ifdef HaveFfmpeg
Source: "redist\ffmpeg.exe"; DestDir: "{app}\Tools"; Flags: ignoreversion; Tasks: tools
#endif
#ifdef HaveYtDlp
Source: "redist\yt-dlp.exe"; DestDir: "{app}\Tools"; Flags: ignoreversion; Tasks: tools
#endif

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\icon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
; Runs elevated as the installing user, which is who the logon task belongs to. Does not start the app.
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-startup.ps1"" -ExecutablePath ""{app}\{#MyAppExe}"""; Flags: runhidden waituntilterminated runascurrentuser; Tasks: startup; StatusMsg: "Registering the logon task..."
; Unchecked by default; only starts if the user ticks it on the finish page.
Filename: "{app}\{#MyAppExe}"; Description: "Launch {#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall shellexec skipifsilent unchecked
; In-app update (MochiDock runs this setup with /VERYSILENT ... /RELAUNCH): start the new version again when the silent install is done.
Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Flags: nowait; Check: IsRelaunch

[Code]
// True when MochiDock started this setup for a one-click update (/RELAUNCH): it is silent, keeps the earlier task choices
// (UsePreviousTasks), never runs winget for PawnIO, and starts MochiDock again at the end.
function IsRelaunch: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then
      Result := True;
end;

procedure StopRunningApp(const AppDir: String);
var
  ResultCode: Integer;
  Script: String;
begin
  { Close gracefully (10 s), then kill; only the copy running from AppDir. }
  Script :=
    '$p=''' + AddBackslash(AppDir) + '{#MyAppExe}''; ' +
    'Get-Process MochiDock -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $p } | ForEach-Object { ' +
    '[void]$_.CloseMainWindow(); if (-not $_.WaitForExit(10000)) { $_.Kill(); [void]$_.WaitForExit(10000) } }; ' +
    { The GOWA engine is a child process; a killed or crashed app leaves it running and locks GOWA\windows-amd64.exe. }
    '$g=''' + AddBackslash(AppDir) + 'GOWA\windows-amd64.exe''; ' +
    'Get-Process windows-amd64 -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $g } | ForEach-Object { $_.Kill(); [void]$_.WaitForExit(10000) }';
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -Command "' + Script + '"', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Installs the official PawnIO package with winget. Never fails the setup: on any problem the user is told where to get it. }
// PawnIO registers an uninstall entry and a kernel service; either means it is already installed, so the setup neither offers
// the task nor runs winget again (winget then answers "already installed", which used to look like a failure).
function PawnIoInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO') or
            RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

procedure InstallPawnIo;
var
  Cmd: String;
  Code: Integer;
  Done: Boolean;
begin
  if PawnIoInstalled then
    Exit;
  Cmd := ExpandConstant('{sys}\cmd.exe');
  Done := False;
  WizardForm.StatusLabel.Caption := 'Installing the PawnIO driver...';
  if Exec(Cmd, '/c winget --version', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0) then
  begin
    if Exec(Cmd, '/c winget install --id namazso.PawnIO --exact --source winget --silent --accept-package-agreements --accept-source-agreements',
      '', SW_HIDE, ewWaitUntilTerminated, Code) then
      { 0 = installed, 3010 = installed, restart needed; any other code still counts when PawnIO is now registered. }
      Done := (Code = 0) or (Code = 3010);
  end;
  if not Done then
    Done := PawnIoInstalled;
  if (not Done) and (not WizardSilent) then
    MsgBox('PawnIO could not be installed automatically (winget is missing or reported an error).' + #13#10#13#10 +
           'MochiDock works without it; the CPU temperature is then an estimate. To install it later, download it from https://pawnio.eu.',
           mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('pawnio') and not IsRelaunch then
    InstallPawnIo;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Upgrade over a running copy: stop it before files are replaced. }
  StopRunningApp(ExpandConstant('{app}'));
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp(ExpandConstant('{app}'));
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Script: String;
  HadMailPasswords: Boolean;
begin
  if CurUninstallStep = usUninstall then
  begin
    Script := ExpandConstant('{app}\uninstall-startup.ps1');
    if FileExists(Script) then
      Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
        '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '"', '',
        SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    { Saved passwords and secrets always go, even when the settings are kept (and on a silent uninstall): mail app passwords,
      the WhatsApp server login and the calendar feed secret. They only open for this Windows user, but nothing should be left
      behind that can still sign in. A reinstall asks for them again. The settings question below is unchanged. }
    HadMailPasswords := FileExists(ExpandConstant('{localappdata}\MochiDock\mail-imap.dat'));
    DeleteFile(ExpandConstant('{localappdata}\MochiDock\mail-imap.dat'));
    DeleteFile(ExpandConstant('{localappdata}\MochiDock\gowa-remote.dat'));
    DeleteFile(ExpandConstant('{localappdata}\MochiDock\gcal.dat'));
    if HadMailPasswords and not UninstallSilent then
      MsgBox('Saved mail app passwords were deleted from this PC.' + #13#10#13#10 +
             'If you added a Gmail, Yahoo, iCloud or other mail account, the app password itself still works until you revoke it ' +
             'in that account''s security settings (for Gmail: myaccount.google.com/apppasswords).',
             mbInformation, MB_OK);
    if not UninstallSilent then
      if MsgBox('Also delete your MochiDock settings and data?' + #13#10#13#10 +
                ExpandConstant('{userappdata}\MochiDock') + #13#10 +
                ExpandConstant('{localappdata}\MochiDock') + #13#10#13#10 +
                'This includes tasks, caches and the linked WhatsApp session. It cannot be undone.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(ExpandConstant('{userappdata}\MochiDock'), True, True, True);
        DelTree(ExpandConstant('{localappdata}\MochiDock'), True, True, True);
      end;
  end;
end;
