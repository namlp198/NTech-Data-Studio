; Inno Setup script for NTech Data Studio.
; Build with scripts\package.ps1 (it passes /DMyAppVersion, /DSourceDir and /O).
; Manual: iscc /DMyAppVersion=0.1.0 /DSourceDir=..\publish\NTechDataStudio installer\NTech-DataStudio.iss

#define MyAppName "NTech Data Studio"
#define MyAppPublisher "NTech"
#define MyAppExeName "NTech.DataStudio.Desktop.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

; Folder holding the published app (output of scripts\build.ps1).
#ifndef SourceDir
  #define SourceDir "..\publish\NTechDataStudio"
#endif

[Setup]
; Stable, unique application id. Never change it between versions: it is how
; a new installer recognizes and upgrades an existing installation.
AppId={{6F2B8C14-3D7A-4E59-A1C6-9B0E5D4F7A32}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\NTech Data Studio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=NTech-DataStudio-Setup-{#MyAppVersion}
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

#ifexist "..\src\NTech.DataStudio.Desktop\Assets\AppIcon.ico"
SetupIconFile=..\src\NTech.DataStudio.Desktop\Assets\AppIcon.ico
#endif

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Projects live in the user's own folders, never under {app}, so an uninstall
; or an upgrade never touches user data.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
