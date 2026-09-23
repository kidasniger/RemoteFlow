#define MyAppName "RemoteFlow"
#ifndef MyAppVersion
#define MyAppVersion "1.0.1"
#endif
#define MyAppPublisher "RemoteFlow"
#define MyAppExeName "RemoteFlow.exe"
[Setup]
AppId={{E5EE58D8-A99C-4B3A-9E68-8CB6F1A3A8DF}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\\RemoteFlow
DefaultGroupName=RemoteFlow
OutputBaseFilename=RemoteFlow-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\\{#MyAppExeName}
[Files]
Source: "..\\publish\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autodesktop}\\RemoteFlow"; Filename: "{app}\\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\\RemoteFlow"; Filename: "{app}\\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\\Désinstaller RemoteFlow"; Filename: "{uninstallexe}"
[Run]
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=\"RemoteFlow\" dir=in action=allow enable=yes program=\"{app}\\{#MyAppExeName}\" protocol=TCP localport=8443 remoteip=localsubnet profile=any"; Flags: runhidden waituntilterminated
Filename: "{app}\\{#MyAppExeName}"; Description: "Lancer RemoteFlow"; Flags: nowait postinstall skipifsilent
[UninstallRun]
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=\"RemoteFlow\""; Flags: runhidden waituntilterminated
