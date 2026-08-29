; Cafe Launcher — Inno Setup installer script.
; Replaces the retired NSIS script (installer/Cafe.Launcher.Avalonia.nsi).
; Build through scripts/Build-Distribution.ps1. Requires Inno Setup 6.3+ (ISCC).
;
; Required ISCC /D defines (enforced by the preprocessor below):
;   APP_VERSION       e.g. 1.0.1-beta.1  — the <VersionPrefix> from the main .csproj
;   APP_FILE_VERSION  e.g. 1.0.1.0       — the <FileVersion> from the main .csproj
;   PUBLISH_GLOB      absolute glob of the win-x64 publish output, e.g. C:\...\artifacts\publish\*

#ifndef APP_VERSION
  #error "APP_VERSION is required."
#endif
#ifndef APP_FILE_VERSION
  #error "APP_FILE_VERSION is required."
#endif
#ifndef PUBLISH_GLOB
  #error "PUBLISH_GLOB is required."
#endif

; The launcher's Windows single-instance mutex (Program.cs: MutexName).
#define APP_MUTEX "Local\Cafe_Launcher_SI"
#define EXECUTABLE_NAME "Cafe.Launcher.Avalonia.exe"
; Uninstall registration key written by this installer (AppId + "_is1").
#define INNO_UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\{803fa671-62db-49fd-b99b-85635f5118ba}_is1"
; Uninstall registration key of the retired NSIS installer, used for the one-time
; upgrade bridge in [Code]. The Inno installer registers under {AppId}_is1 instead.
#define LEGACY_NSIS_UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Cafe.Launcher.Avalonia"

[Setup]
; Stable product identity. NEVER change AppId: it locates the installed product
; for upgrades (same AppId = prior version is silently uninstalled first) and
; names the Windows uninstall entry (HKLM\...\Uninstall\{AppId}_is1).
AppId={{803fa671-62db-49fd-b99b-85635f5118ba}
AppName=Cafe Launcher
AppVersion={#APP_VERSION}
AppVerName=Cafe Launcher {#APP_VERSION}
AppPublisher=BlueArchive Cafe
; Resolved by ResolveDefaultDir: the previous installation's directory when a
; registry record exists (Inno upgrade or trusted legacy NSIS bridge), otherwise
; the machine-wide default.
DefaultDirName={code:ResolveDefaultDir}
; Upgrading into the existing directory is the normal path (the default
; directory above is the detected previous install), so the "folder already
; exists — are you sure?" prompt would fire on every upgrade. Skip it.
DirExistsWarning=no
DefaultGroupName=Cafe Launcher
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=Cafe Launcher
UninstallDisplayIcon={app}\{#EXECUTABLE_NAME}
SetupIconFile=..\src\Cafe.Launcher.Avalonia\Assets\app-icon.ico
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=Cafe.Launcher.Avalonia_setup
VersionInfoVersion={#APP_FILE_VERSION}
VersionInfoProductVersion={#APP_FILE_VERSION}
VersionInfoProductTextVersion={#APP_VERSION}
VersionInfoDescription=Cafe Launcher Setup
VersionInfoProductName=Cafe Launcher
VersionInfoCompany=BlueArchive Cafe
VersionInfoCopyright=BlueArchive Cafe
; Contract: never terminate the running launcher. The user must close it first;
; AppMutex shows the localized "close it now, then click OK/Retry" prompt.
CloseApplications=no
RestartApplications=no
AppMutex={#APP_MUTEX}
SetupLogging=yes
; The launcher writes its per-user data to {localappdata}\Cafe Launcher while the
; install itself is machine-wide (PrivilegesRequired=admin). The uninstaller
; touches that per-user path only after explicit user consent (see
; InitializeUninstall), which is intended; silence Inno's UsedUserAreasWarning.
UsedUserAreasWarning=no

[Languages]
; Per-language custom messages (DeleteDataQuestion, InvalidInstallLocation,
; PreviousUninstallFailed) live in installer/lang/CustomMessages.*.isl:
; language-independent message text in the script would apply globally (the
; last entry wins for every language), so localized messages are supplied
; through each language's translation files instead.
; ChineseSimplified.isl is vendored in installer/lang/ because official Inno
; Setup ships Chinese translations only in 7.x. The release workflow uses 7.x,
; while the vendored file keeps local compilation compatible with 6.3+.
Name: "english"; MessagesFile: "compiler:Default.isl, lang\CustomMessages.en.isl"
Name: "chinesesimplified"; MessagesFile: "lang\ChineseSimplified.isl, lang\CustomMessages.zh.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl, lang\CustomMessages.ja.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; ignoreversion: always overwrite installed files on upgrade; the publish output
; is the single source of truth. No [UninstallDelete] entry above {app} level:
; the uninstaller only removes files it installed (plus the marker below), so the
; sibling game directory next to the install folder can never be touched.
Source: "{#PUBLISH_GLOB}"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Cafe Launcher\Cafe Launcher"; Filename: "{app}\{#EXECUTABLE_NAME}"; WorkingDir: "{app}"
Name: "{autoprograms}\Cafe Launcher\Uninstall Cafe Launcher"; Filename: "{uninstallexe}"; WorkingDir: "{app}"
Name: "{autodesktop}\Cafe Launcher"; Filename: "{app}\{#EXECUTABLE_NAME}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#EXECUTABLE_NAME}"; Description: "{cm:LaunchProgram,Cafe Launcher}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Installation ownership marker (written at install time; see CurStepChanged).
Type: files; Name: "{app}\.cafe-launcher-install"
; User data is preserved unless the user explicitly opts in, and never removed
; during a silent uninstall (e.g. upgrades).
Type: filesandordirs; Name: "{localappdata}\Cafe Launcher"; Check: ShouldDeleteUserData

[Code]
var
  DeleteApplicationData: Boolean;
  InitialInstallDir: String;

{ Reads InstallLocation from an uninstall registry key when present and non-empty. }
function TryReadInstallLocation(const Root: Integer; const Key: String): Boolean;
var
  Location: String;
begin
  Result := False;
  if RegQueryStringValue(Root, Key, 'InstallLocation', Location) and (Location <> '') then
  begin
    InitialInstallDir := RemoveBackslashUnlessRoot(Location);
    Result := True;
  end;
end;

{ Locate an existing installation so the directory page defaults to it.
  Inno-to-Inno upgrades are normally covered by UsePreviousAppDir, which reads
  the current-hive uninstall key; the explicit lookups also recover the path
  when that default lookup misses (e.g. the record lives in another registry
  view). The legacy NSIS registration is adopted only by the validated upgrade
  bridge below, never here, so a stale record can never seed the default. }
procedure ResolveInitialInstallDir;
begin
  if TryReadInstallLocation(HKLM64, '{#INNO_UNINSTALL_KEY}') then
    Exit;
  if TryReadInstallLocation(HKLM32, '{#INNO_UNINSTALL_KEY}') then
    Exit;
  if TryReadInstallLocation(HKCU, '{#INNO_UNINSTALL_KEY}') then
    Exit;
  TryReadInstallLocation(HKCU32, '{#INNO_UNINSTALL_KEY}');
end;

{ DefaultDirName callback: reuse the detected installation, else the default. }
function ResolveDefaultDir(const Param: String): String;
begin
  if InitialInstallDir <> '' then
    Result := InitialInstallDir
  else
    Result := ExpandConstant('{autopf}\Cafe Launcher');
end;

{ Called by [UninstallDelete] while Setup records the uninstall entry and again
  during uninstall. Do not call uninstall-only support functions here: Setup
  evaluates this check before an uninstaller exists. InitializeUninstall keeps
  this False for silent uninstalls and only sets it after explicit user consent. }
function ShouldDeleteUserData: Boolean;
begin
  Result := DeleteApplicationData;
end;

{ Returns True while the legacy registration is consistent: a missing or empty
  InstallLocation is tolerated, but a present value must match the uninstaller's
  directory case-insensitively after dropping trailing separators. }
function LegacyInstallLocationMatches(const InstallDir: String): Boolean;
var
  InstallLocation: String;
  Location: String;
  Expected: String;
begin
  Result := True;
  if not RegQueryStringValue(HKLM, '{#LEGACY_NSIS_UNINSTALL_KEY}', 'InstallLocation', InstallLocation) then
    Exit;
  if InstallLocation = '' then
    Exit;

  Location := InstallLocation;
  Expected := InstallDir;
  { Keep drive roots (e.g. C:\) as-is. }
  while (Length(Location) > 3) and (Location[Length(Location)] = '\') do
    Delete(Location, Length(Location), 1);
  while (Length(Expected) > 3) and (Expected[Length(Expected)] = '\') do
    Delete(Expected, Length(Expected), 1);

  Result := CompareText(Location, Expected) = 0;
end;

{ One-time upgrade bridge support: reads the legacy NSIS uninstaller path and
  validates the registration. Returns the install directory when the record is
  trusted (uninstaller named Uninstall.exe, existing, and living in the
  directory recorded as InstallLocation); anything else is treated as stale and
  returns ''. This function never modifies or executes anything. }
function TryGetValidatedLegacyInstall(var UninstallerPath: String): String;
var
  UninstallString: String;
  InstallDir: String;
  QuotePos: Integer;
  SpacePos: Integer;
begin
  Result := '';
  UninstallerPath := '';
  if not RegQueryStringValue(HKLM, '{#LEGACY_NSIS_UNINSTALL_KEY}', 'UninstallString', UninstallString) then
    Exit;
  if UninstallString = '' then
    Exit;

  { Extract the uninstaller executable path (quote- and space-aware). }
  UninstallerPath := UninstallString;
  if Copy(UninstallString, 1, 1) = '"' then
  begin
    QuotePos := Pos('"', Copy(UninstallString, 2, Length(UninstallString) - 1));
    if QuotePos = 0 then
      Exit;
    UninstallerPath := Copy(UninstallString, 2, QuotePos - 1);
  end
  else
  begin
    SpacePos := Pos(' ', UninstallString);
    if SpacePos > 0 then
      UninstallerPath := Copy(UninstallString, 1, SpacePos - 1);
  end;

  InstallDir := ExtractFileDir(UninstallerPath);
  if (CompareText(ExtractFileName(UninstallerPath), 'Uninstall.exe') <> 0)
      or (not FileExists(UninstallerPath))
      or (not LegacyInstallLocationMatches(InstallDir)) then
  begin
    UninstallerPath := '';
    Exit;
  end;

  Result := RemoveBackslashUnlessRoot(InstallDir);
end;

{ One-time upgrade bridge, run from PrepareToInstall — i.e. only after the user
  completed the wizard and chose to install — so cancelling setup can never
  remove the old version. Stale or tampered registrations are removed without
  execution (the validation above), so they can never run an arbitrary path. }
function RemoveLegacyInstallation(): Boolean;
var
  InstallDir: String;
  UninstallerPath: String;
  ResultCode: Integer;
begin
  Result := True;
  InstallDir := TryGetValidatedLegacyInstall(UninstallerPath);
  if InstallDir = '' then
  begin
    { Absent or stale registration — clean up the key, never execute it. }
    if RegKeyExists(HKLM, '{#LEGACY_NSIS_UNINSTALL_KEY}') then
      RegDeleteKeyIncludingSubkeys(HKLM, '{#LEGACY_NSIS_UNINSTALL_KEY}');
    Exit;
  end;

  { Match the retired NSIS upgrade path exactly: _?= must be UNQUOTED, because
    the legacy uninstaller treats the rest of its command line as the path and
    any quotes would be compared against the registry InstallLocation. /S keeps
    it silent, and _?= makes the legacy uninstaller remove itself afterwards. }
  if not Exec(UninstallerPath, Format('/S _?=%s', [InstallDir]), InstallDir,
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := False;
    Exit;
  end;
  if ResultCode <> 0 then
  begin
    Result := False;
    Exit;
  end;
  if FileExists(UninstallerPath) then
    DeleteFile(UninstallerPath);
  RemoveDir(InstallDir);
end;

function InitializeSetup: Boolean;
var
  LegacyUninstallerPath: String;
begin
  Result := True;
  ResolveInitialInstallDir;
  { Only read the legacy registration here (as the directory-page default when
    no Inno Setup record exists); the actual uninstall happens in
    PrepareToInstall, after the user chose to install. }
  if InitialInstallDir = '' then
    InitialInstallDir := TryGetValidatedLegacyInstall(LegacyUninstallerPath);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not RemoveLegacyInstallation() then
    Result := ExpandConstant('{cm:PreviousUninstallFailed}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { Ownership marker consumed by the uninstaller to validate the install location. }
  if CurStep = ssPostInstall then
    SaveStringToFile(
      ExpandConstant('{app}\.cafe-launcher-install'),
      ExpandConstant('{#EXECUTABLE_NAME}') + #13#10,
      False);
end;

function InitializeUninstall: Boolean;
var
  DataPath: String;
  QuestionText: String;
begin
  Result := True;
  if not FileExists(ExpandConstant('{app}\.cafe-launcher-install')) then
  begin
    MsgBox(ExpandConstant('{cm:InvalidInstallLocation}'), mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if not FileExists(ExpandConstant('{app}\{#EXECUTABLE_NAME}')) then
  begin
    MsgBox(ExpandConstant('{cm:InvalidInstallLocation}'), mbError, MB_OK);
    Result := False;
    Exit;
  end;

  DeleteApplicationData := False;
  if not UninstallSilent then
  begin
    DataPath := ExpandConstant('{localappdata}\Cafe Launcher');
    QuestionText := FmtMessage(ExpandConstant('{cm:DeleteDataQuestion}'), [DataPath]);
    DeleteApplicationData := MsgBox(QuestionText, mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

