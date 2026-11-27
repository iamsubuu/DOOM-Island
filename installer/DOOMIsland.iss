; DOOM Island — Inno Setup 6 Installer Script
#define AppName "DOOM Island"
#ifndef AppVersion
#define AppVersion "2.4.5"
#endif
#define AppExeName "DOOMIsland.exe"

[Setup]
AppId={{7B63F9E4-8A4C-4F1D-9B72-2C9E4F5A8D11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ArcSubash
AppPublisherURL=https://github.com/ArcSubash
AppSupportURL=mailto:lynchkiller0337@gmail.com
AppUpdatesURL=https://github.com/iamsubuu
AppContact=lynchkiller0337@gmail.com
VersionInfoCompany=ArcSubash
VersionInfoCopyright=Copyright (C) ArcSubash
DefaultDirName={localappdata}\Programs\DOOMIsland
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=DOOMIsland-Setup-x64-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\NOVAIsland.App\Assets\DOOMIsland.ico
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
