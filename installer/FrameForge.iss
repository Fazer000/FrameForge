; Установщик Frame Forge для Windows x64.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId=FrameForge.Desktop
AppName=Frame Forge
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Frame Forge
DefaultGroupName=Frame Forge
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\FrameForge.exe
SetupIconFile=..\src\FrameForge\Assets\FrameForge.ico
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=FrameForge-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Frame Forge"; Filename: "{app}\FrameForge.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Frame Forge"; Filename: "{app}\FrameForge.exe"; WorkingDir: "{app}"; Tasks: desktopicon
