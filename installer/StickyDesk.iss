#ifndef AppVersion
  #error AppVersion must be supplied by scripts/installer.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by scripts/installer.ps1
#endif
#ifndef ProjectRoot
  #error ProjectRoot must be supplied by scripts/installer.ps1
#endif
#ifndef InstallerOutput
  #error InstallerOutput must be supplied by scripts/installer.ps1
#endif

[Setup]
AppId={{1F17D3F9-1F43-40CA-94E5-7A9D8C613426}
AppName=StickyDesk
AppVersion={#AppVersion}
AppPublisher=StickyDesk
AppComments=Markdown-заметки на рабочем столе Windows
DefaultDirName={localappdata}\Programs\StickyDesk
DefaultGroupName=StickyDesk
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern dynamic windows11
DisableWelcomePage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir={#InstallerOutput}
OutputBaseFilename=StickyDesk-Setup-{#AppVersion}-x64
SetupIconFile={#ProjectRoot}\src\StickyDesk.App\Assets\StickyDesk.ico
UninstallDisplayIcon={app}\StickyDesk.exe
UninstallDisplayName=StickyDesk
VersionInfoVersion={#AppVersion}
VersionInfoDescription=StickyDesk Installer
Compression=lzma2/max
SolidCompression=yes
AppMutex=Local\StickyDesk-Running
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
russian.WelcomeLabel2=Markdown-заметки прямо на рабочем столе.%n%nПрограмма будет установлена только для текущего пользователя. Ваши заметки и настройки сохраняются отдельно от программы.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\StickyDesk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\StickyDesk"; Filename: "{app}\StickyDesk.exe"; WorkingDir: "{app}"; Check: not WizardNoIcons
Name: "{autodesktop}\StickyDesk"; Filename: "{app}\StickyDesk.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\StickyDesk.exe"; Description: "{cm:LaunchProgram,StickyDesk}"; Flags: postinstall nowait skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
  InstalledExecutable: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    InstalledExecutable := '"' + ExpandConstant('{app}\StickyDesk.exe') + '"';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'StickyDesk', StartupCommand) then
    begin
      if Pos(Lowercase(InstalledExecutable), Lowercase(StartupCommand)) = 1 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StickyDesk');
    end;
  end;
end;
