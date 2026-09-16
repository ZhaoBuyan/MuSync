; ============================================================
;  MuSync 安装器脚本（Inno Setup 6.5+）
;
;  构建：ISCC.exe /DAppVersion=0.4.0 MuSync.iss
;  产物：..\dist-setup\MuSync-Setup.exe
;
;  设计要点：
;  - 单用户安装到 %LocalAppData%\Programs\MuSync，全程不需要管理员、不弹 UAC；
;  - 安装器版程序依据编译期 MuSyncEdition=setup 标记识别自己，
;    更新时静默运行新版安装器（/VERYSILENT），安装完成自动重启新版；
;  - 配置与日志在 %LocalAppData%\MuSync，与绿色版共用，升级/重装均不受影响；
;  - AppId 固定不可更改：升级识别依赖它。
; ============================================================

#define AppName "MuSync"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{B7A3D9E1-5C42-4F88-A6D0-1E93C4B25F71}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ZhaoBuyan
AppCopyright=Copyright (c) 2026 ZhaoBuyan
VersionInfoVersion={#AppVersion}.0
UsePreviousTasks=yes
DefaultDirName={localappdata}\Programs\MuSync
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist-setup
OutputBaseFilename=MuSync-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no
SetupIconFile=..\Resources\icon.ico
UninstallDisplayIcon={app}\MuSync.exe

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\setup-payload\*"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\MuSync"; Filename: "{app}\MuSync.exe"
Name: "{group}\{cm:UninstallProgram,MuSync}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MuSync"; Filename: "{app}\MuSync.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MuSync.exe"; Description: "{cm:LaunchProgram,MuSync}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\MuSync.exe"; Flags: nowait; Check: IsSilentUpgrade

[Code]
// 是否在升级：安装开始时求值一次缓存（Run 阶段卸载信息已写入，不能在那时判断）
var
  WasUpgradeAtStart: Boolean;

function InitializeSetup(): Boolean;
begin
  WasUpgradeAtStart := RegKeyExists(HKCU,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B7A3D9E1-5C42-4F88-A6D0-1E93C4B25F71}_is1');
  Result := True;
end;

// 静默升级完成后自动重启新版；首次静默安装不启动
function IsSilentUpgrade: Boolean;
begin
  Result := WizardSilent and WasUpgradeAtStart;
end;
