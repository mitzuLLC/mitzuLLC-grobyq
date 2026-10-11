; grobyq v4.0 installer (Inno Setup). CI invokes:  iscc /DSrcDir=<publish|protected> build\grobyq.iss
; Produces dist\grobyq-setup.exe — installs to Program Files\grobyq, shortcuts, uninstaller.

#ifndef SrcDir
  #define SrcDir "publish"
#endif
#define AppVer "4.0.0"

[Setup]
AppName=grobyq
AppVersion={#AppVer}
AppPublisher=mitzu LLC
DefaultDirName={autopf}\grobyq
DefaultGroupName=grobyq
UninstallDisplayIcon={app}\grobyq.exe
OutputDir=dist
OutputBaseFilename=grobyq-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
SourceDir=.

[Files]
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\grobyq"; Filename: "{app}\grobyq.exe"
Name: "{autodesktop}\grobyq"; Filename: "{app}\grobyq.exe"

[Run]
Filename: "{app}\grobyq.exe"; Description: "Launch grobyq"; Flags: nowait postinstall skipifsilent
