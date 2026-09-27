#ifndef AppVersion
  #error AppVersion is required; use scripts/release.ps1.
#endif
#ifndef PublishDir
  #error PublishDir is required.
#endif
#ifndef ReleaseDir
  #error ReleaseDir is required.
#endif
#ifndef OutputName
  #define OutputName "Jot-Setup-" + AppVersion + "-win-x64"
#endif

[Setup]
#ifdef ValidationBuild
AppId={{5730A854-61C8-43C2-9834-25C4C46CC086}
AppName=Jot Installer Validation
CreateUninstallRegKey=no
AppMutex=Local\Jot-installer-validation-only
#else
AppId={{A760EE33-36A8-462C-89AB-9AF8E92A041B}
AppName=Jot
AppMutex=Local\Jot-personal-notes-v2
#endif
AppVersion={#AppVersion}
AppPublisher=Jot
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Jot
DefaultGroupName=Jot
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#ReleaseDir}
OutputBaseFilename={#OutputName}
SetupIconFile={#PublishDir}\assets\jot.ico
UninstallDisplayIcon={app}\Jot.exe
WizardStyle=modern
Compression=lzma2/fast
LZMANumBlockThreads=2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
AllowNoIcons=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
Uninstallable=yes
ChangesAssociations=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "tests\*,*.pdb"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\Jot"; Filename: "{app}\Jot.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Jot"; Filename: "{app}\Jot.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Jot.exe"; Description: "Launch Jot"; Flags: nowait postinstall skipifsilent unchecked

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  WindowsVersion: TWindowsVersion;
begin
  if CurStep = ssPostInstall then
  begin
    GetWindowsVersionEx(WindowsVersion);
    if (WindowsVersion.Major = 10) and (WindowsVersion.Build < 22000) then
    begin
      { Microsoft's Fixed WebView2 runtime requires these read/execute ACLs on
        Windows 10. Apply only to shipped runtime binaries, never user notes. }
      if not Exec(ExpandConstant('{sys}\icacls.exe'),
        ExpandConstant('"{app}\WebView2Runtime" /grant "*S-1-15-2-2:(OI)(CI)(RX)" "*S-1-15-2-1:(OI)(CI)(RX)" /T /Q'),
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        RaiseException('Could not prepare the bundled runtime. Run Jot Setup again.');
      if ResultCode <> 0 then
        RaiseException('Could not set the bundled runtime permissions. Run Jot Setup again.');
    end;
  end;
end;

{ There is deliberately no deletion of %LOCALAPPDATA%\Jot. Uninstall removes
  installed files and shortcuts only; notes, backups, trash and logs survive. }
