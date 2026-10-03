#ifndef AppVersion
  #define AppVersion "0.3.0-beta.3"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\artifacts\release"
#endif

[Setup]
AppId={{6A14A46B-B09F-423F-8FA1-5B342EE65625}
AppName=Ai UsageNest
AppVersion={#AppVersion}
AppPublisher=Mikrofab
AppPublisherURL=https://github.com/ozkurkuran/ai-usage-viewer
AppSupportURL=https://github.com/ozkurkuran/ai-usage-viewer/issues
DefaultDirName={localappdata}\Programs\Ai UsageNest
DefaultGroupName=Ai UsageNest
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
LicenseFile=..\LICENSE
OutputDir={#ReleaseDir}
OutputBaseFilename=AIUsageViewer-{#AppVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\AIUsageViewer.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Shortcuts from releases published as "AI Usage Viewer".
Type: files; Name: "{autoprograms}\AI Usage Viewer.lnk"
Type: files; Name: "{autodesktop}\AI Usage Viewer.lnk"

[Icons]
Name: "{autoprograms}\Ai UsageNest"; Filename: "{app}\AIUsageViewer.exe"
Name: "{autodesktop}\Ai UsageNest"; Filename: "{app}\AIUsageViewer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AIUsageViewer.exe"; Description: "{cm:LaunchProgram,Ai UsageNest}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command: String;
begin
  // Remove only this installation's startup entry. User data is retained.
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AiUsageViewer', Command) then
      if Pos('"' + ExpandConstant('{app}\AIUsageViewer.exe') + '"', Command) = 1 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AiUsageViewer');
end;
