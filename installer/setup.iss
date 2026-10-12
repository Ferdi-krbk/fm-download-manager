#define MyAppName "FM Download Manager"
#define MyAppVersion "3.3.0"
#define MyAppPublisher "FM Download Manager Authors"
#define MyAppURL "https://github.com/mstcvk/mc-download-manager"
#define MyAppExeName "FMDownloadManager.exe"

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
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
InfoAfterFile=extension_guide.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.OpenExtFolder=Open browser extension folder (for Chrome / Edge)
turkish.OpenExtFolder=Tarayıcı eklentisi klasörünü aç (Chrome / Edge için)
english.OpenExtGuide=Open visual Extension Setup Guide (HTML)
turkish.OpenExtGuide=Resimli Eklenti Kurulum Rehberini Aç (HTML)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start FM Download Manager with Windows"; GroupDescription: "Windows Integration:"

[Files]
; Main published binaries (includes Tools and extension)
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; AppUserModelID: "FM.DownloadManager.3.0"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; AppUserModelID: "FM.DownloadManager.3.0"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--background"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startupicon; AppUserModelID: "FM.DownloadManager.3.0"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "explorer.exe"; Parameters: """{app}\extension"""; Description: "{cm:OpenExtFolder}"; Flags: postinstall skipifsilent
Filename: "{app}\EKLENTI_KURULUMU.html"; Description: "{cm:OpenExtGuide}"; Flags: postinstall shellexec skipifsilent

[UninstallDelete]
; Clean up runtime artifacts while preserving user downloads
Type: filesandordirs; Name: "{localappdata}\MC Download Manager\bridge.token"
Type: files; Name: "{localappdata}\MC Download Manager\downloads.json.tmp"

[Code]
procedure SHChangeNotify(wEventId: Integer; uFlags: Cardinal; dwItem1: Cardinal; dwItem2: Cardinal);
external 'SHChangeNotify@shell32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    try
      // Flush Windows Explorer icon cache so new burgundy icon appears immediately
      SHChangeNotify(134217728, 0, 0, 0);
    except
    end;
  end;
end;
