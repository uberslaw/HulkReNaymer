#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif

#define MyAppName "HulkReNaymer"
#define MyAppPublisher "HulkReNaymer"
#define MyAppURL "https://github.com/uberslaw/HulkReNaymer"
#define MyAppExeName "HulkReNaymer.exe"

[Setup]
AppId={{E4B8C2A1-9D7F-4E3A-8B15-7C6A2F91D043}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=HulkReNaymer-Setup
SetupIconFile=..\src\HulkReNaymer.App\Assets\hulk.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "sendto"; Description: "Add HulkReNaymer to the Explorer Send To menu"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "contextmenu"; Description: "Add Explorer context menu (Rename with HulkReNaymer)"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Windows\SendTo\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: sendto

[Registry]
; Optional Explorer verb (files + folders). Uninstall removes the keys. No Run/startup persistence.
Root: HKCR; Subkey: "*\shell\HulkReNaymer"; ValueType: string; ValueName: ""; ValueData: "Rename with HulkReNaymer"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCR; Subkey: "*\shell\HulkReNaymer"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"; Tasks: contextmenu
Root: HKCR; Subkey: "*\shell\HulkReNaymer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCR; Subkey: "Directory\shell\HulkReNaymer"; ValueType: string; ValueName: ""; ValueData: "Rename with HulkReNaymer"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCR; Subkey: "Directory\shell\HulkReNaymer"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"; Tasks: contextmenu
Root: HKCR; Subkey: "Directory\shell\HulkReNaymer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCR; Subkey: "Directory\Background\shell\HulkReNaymer"; ValueType: string; ValueName: ""; ValueData: "Rename with HulkReNaymer"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCR; Subkey: "Directory\Background\shell\HulkReNaymer"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"; Tasks: contextmenu
Root: HKCR; Subkey: "Directory\Background\shell\HulkReNaymer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%V"""; Tasks: contextmenu

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
