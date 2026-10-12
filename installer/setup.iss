#define MyAppName "FM Download Manager"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "FM Download Manager Authors"
#define MyAppURL "https://github.com/mstcvk/mc-download-manager"
#define MyAppExeName "MCDownloadManager.exe"

[Setup]
; AppId uniquely identifies this application. Do not use the same AppId in other installers.
AppId={{D37D0B92-F9F1-4F2A-A618-971FE2F6F304}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install: no administrative privileges required
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=FMDownloadManager-Setup-{#MyAppVersion}
SetupIconFile=..\src\McDm.App\Resources\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start MC Download Manager with Windows"; GroupDescription: "Windows Integration:"

[Files]
; Main published single-file binaries
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Tools folder (yt-dlp and ffmpeg if downloaded)
Source: "..\Tools\*"; DestDir: "{app}\Tools"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Chrome/Edge/Brave extension folder
Source: "..\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--background"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up runtime artifacts while preserving user downloads
Type: filesandordirs; Name: "{localappdata}\MC Download Manager\bridge.token"
Type: files; Name: "{localappdata}\MC Download Manager\downloads.json.tmp"
