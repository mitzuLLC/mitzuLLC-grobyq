; grobyq v4.0 installer (Inno Setup). CI invokes:  iscc /DSrcDir=<publish|protected> build\grobyq.iss
; Produces dist\grobyq-setup.exe — installs to Program Files\grobyq, shortcuts, uninstaller.

#ifndef SrcDir
  #define SrcDir "publish"
#endif
#ifndef OutDir
  #define OutDir "dist"
#endif
#define AppVer "4.0.5"

[Setup]
AppName=grobyq
AppVersion={#AppVer}
AppPublisher=mitzu LLC
DefaultDirName={autopf}\grobyq
DefaultGroupName=grobyq
UninstallDisplayIcon={app}\grobyq.exe
OutputDir={#OutDir}
OutputBaseFilename=grobyq-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
; a running grobyq (tray) is detected: setup / uninstall ask to exit it first (it restores every change on exit)
AppMutex=Global\grobyq-mitzu

[Files]
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\grobyq"; Filename: "{app}\grobyq.exe"
Name: "{autodesktop}\grobyq"; Filename: "{app}\grobyq.exe"

[Run]
Filename: "{app}\grobyq.exe"; Description: "Launch grobyq"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; the "Start with Windows" logon task points at this folder: it goes with the app
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""grobyq-mitzu"" /F"; Flags: runhidden; RunOnceId: "DelLogonTask"
