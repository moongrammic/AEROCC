; Script generated for AEROCC - Control Center for Gigabyte Aero 16 (1VH)
; Inno Setup 6.x

#define MyAppName "AEROCC"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "moongrammic"
#define MyAppURL "https://github.com/moongrammic/AEROCC"
#define MyAppExeName "AEROCC.App.exe"
#define MyServiceExeName "AEROCC.Service.exe"
#define MyCliExeName "AEROCC.Cli.exe"

[Setup]
AppId={{5D0B1B3C-918B-4A59-BF26-64A198D0B7E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=AEROCC_Setup_v{#MyAppVersion}
SetupIconFile=..\src\AEROCC.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startservice"; Description: "Установить и запустить фоновую службу (AEROCCService)"; GroupDescription: "Служба и автозапуск:"; Flags: checkablealone
Name: "autostartapp"; Description: "Автозапуск Control Center при входе в Windows (в трей)"; GroupDescription: "Служба и автозапуск:"; Flags: checkablealone

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} CLI (Диагностика)"; Filename: "{app}\{#MyCliExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Install background service if selected
Filename: "{app}\{#MyServiceExeName}"; Parameters: "--install"; StatusMsg: "Установка и запуск службы AEROCCService..."; Tasks: startservice; Flags: runhidden waituntilterminated
; Register autostart task if selected (uses AEROCC.App to write explicit XML with battery & unlimited execution settings)
Filename: "{app}\{#MyAppExeName}"; Parameters: "--enable-autostart"; Tasks: autostartapp; Flags: runhidden waituntilterminated runascurrentuser
; Launch application after setup completes (runascurrentuser prevents CreateProcess Error 740 on elevated manifest)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Stop and remove service before files are deleted
Filename: "{app}\{#MyServiceExeName}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated
; Remove autostart task from Task Scheduler
Filename: "schtasks.exe"; Parameters: "/delete /tn ""AEROCC"" /f"; Flags: runhidden waituntilterminated
; Terminate running GUI or CLI instances if still open
Filename: "taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden waituntilterminated
Filename: "taskkill.exe"; Parameters: "/f /im {#MyCliExeName}"; Flags: runhidden waituntilterminated
