; Inno Setup script for NTech Trainer (Desktop app, CLI and worker image sources).
; Build with scripts\trainer\package.ps1 (it passes /DMyAppVersion, /DSourceDir and /O).
; Manual: iscc /DMyAppVersion=0.1.0 /DSourceDir=..\..\publish\NTechTrainer-win-x64 trainer\installer\NTech-Trainer.iss

#define MyAppName "NTech Trainer"
#define MyAppPublisher "NTech"
#define MyAppExeName "NTechTrainer.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

; Folder holding the published app (output of scripts\trainer\build.ps1).
#ifndef SourceDir
  #define SourceDir "..\..\publish\NTechTrainer-win-x64"
#endif

[Setup]
; Stable, unique application id. Never change it between versions: it is how
; a new installer recognizes and upgrades an existing installation. It differs
; from Data Studio's, so the two tools install and uninstall independently.
AppId={{374AE65E-1993-4389-BA78-DDA65F15E499}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\NTech Trainer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\..\dist
OutputBaseFilename=NTech-Trainer-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
; Installs for all users and asks for admin rights by default. A command-line
; /CURRENTUSER installs without admin rights, for machines where that is not possible.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
; Offer to close a running copy instead of failing on locked files.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Workspaces, runs and settings live in the user's own folders and %APPDATA%,
; never under {app}, so an uninstall or an upgrade never touches them.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[InstallDelete]
; The worker image is built from {app}\image: a worker file dropped in a new
; version must not linger there after an upgrade.
Type: filesandordirs; Name: "{app}\image"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
