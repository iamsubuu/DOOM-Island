; DOOM Island — Inno Setup 6 Installer Script
#define AppName "DOOM Island"
#define AppVersion "1.0.0"
#define AppExeName "NOVAIsland.App.exe"

[Setup]
AppId={{7B63F9E4-8A4C-4F1D-9B72-2C9E4F5A8D11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=DOOM Island
DefaultDirName={localappdata}\Programs\DOOMIsland
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=DOOMIsland-Setup-x64-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\artifacts\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
