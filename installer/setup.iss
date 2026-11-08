#define MyAppName "Ray's Loader"
#define MyAppVersion "1.4.1"
#define MyAppPublisher "Rayware Inc."
#define MyAppExeName "RaysLoader.exe"

[Setup]
AppId={{6F7C01E3-94DE-4E20-AF46-990D181828C4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\RaysLoader
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=RaysLoader-Setup-{#MyAppVersion}
SetupIconFile=..\src\RaysLoader\Assets\RaysLoader.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
; Rendered by assets\brand\render.ps1; Setup picks the size that best fits the display DPI.
WizardImageFile=images\WizardImage-100.png,images\WizardImage-150.png,images\WizardImage-200.png
WizardSmallImageFile=images\WizardSmallImage-100.png,images\WizardSmallImage-150.png,images\WizardSmallImage-200.png
PrivilegesRequired=admin
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  RemoveManagedAppData: Boolean;
  RemoveLegacyAppData: Boolean;
  CleanupFailures: String;

function IsExpectedOwnedDirectory(const Candidate, Parent, ChildName: String): Boolean;
var
  Expected: String;
begin
  Expected := AddBackslash(RemoveBackslashUnlessRoot(Parent)) + ChildName;
  Result := CompareText(RemoveBackslashUnlessRoot(Candidate), RemoveBackslashUnlessRoot(Expected)) = 0;
end;

procedure DeleteOwnedDirectory(const Candidate, Parent, ChildName: String);
begin
  if not IsExpectedOwnedDirectory(Candidate, Parent, ChildName) then begin
    CleanupFailures := CleanupFailures + Candidate + ': path validation failed.' + #13#10;
    Log('Refused to delete unexpected app-data path: ' + Candidate);
    Exit;
  end;

  if not DirExists(Candidate) then
    Exit;

  { DelTree removes reparse-point directories without traversing their targets. }
  if DelTree(Candidate, True, True, True) then
    Log('Removed Ray''s Loader-owned app data: ' + Candidate)
  else begin
    CleanupFailures := CleanupFailures + Candidate + ': Windows could not remove every file.' + #13#10;
    Log('Could not completely remove Ray''s Loader-owned app data: ' + Candidate);
  end;
end;

function InitializeUninstall: Boolean;
var
  ManagedDirectory: String;
  LegacyLocalDirectory: String;
  LegacyRoamingDirectory: String;
begin
  Result := True;
  RemoveManagedAppData := False;
  RemoveLegacyAppData := False;
  CleanupFailures := '';

  { Silent enterprise uninstalls preserve user data unless an interactive user opts in. }
  if UninstallSilent then
    Exit;

  ManagedDirectory := ExpandConstant('{localappdata}\RaysLoader');
  RemoveManagedAppData := SuppressibleMsgBox(
    'Also remove Ray''s Loader settings, managed tools, updater files, and logs?' + #13#10 + #13#10 +
    'Folder: ' + ManagedDirectory + #13#10 + #13#10 +
    'Downloaded media, external tools, and cookie files will not be removed.',
    mbConfirmation, MB_YESNO, IDNO) = IDYES;

  if not RemoveManagedAppData then
    Exit;

  LegacyLocalDirectory := ExpandConstant('{localappdata}\YtDlpGui');
  LegacyRoamingDirectory := ExpandConstant('{userappdata}\YtDlpGui');
  if DirExists(LegacyLocalDirectory) or DirExists(LegacyRoamingDirectory) then
    RemoveLegacyAppData := SuppressibleMsgBox(
      'Legacy YtDlpGui app data was found. Remove it too?' + #13#10 + #13#10 +
      LegacyLocalDirectory + #13#10 + LegacyRoamingDirectory,
      mbConfirmation, MB_YESNO, IDNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  if RemoveManagedAppData then
    DeleteOwnedDirectory(
      ExpandConstant('{localappdata}\RaysLoader'),
      ExpandConstant('{localappdata}'),
      'RaysLoader');

  if RemoveLegacyAppData then begin
    DeleteOwnedDirectory(
      ExpandConstant('{localappdata}\YtDlpGui'),
      ExpandConstant('{localappdata}'),
      'YtDlpGui');
    DeleteOwnedDirectory(
      ExpandConstant('{userappdata}\YtDlpGui'),
      ExpandConstant('{userappdata}'),
      'YtDlpGui');
  end;

  if CleanupFailures <> '' then
    MsgBox(
      'Ray''s Loader was uninstalled, but some app data could not be removed:' + #13#10 + #13#10 +
      CleanupFailures + #13#10 + 'Close programs using these files and delete the listed folders manually.',
      mbError, MB_OK);
end;
