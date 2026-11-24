; NeuralRig Studio v3.0.0 Windows Installer Script (Inno Setup 6)

#ifndef AppVersion
  #define AppVersion "3.0.1"
#endif

[Setup]
AppId={{8D4F62C3-2D90-4A9A-9D48-9B4D5F3D3001}
AppName=NeuralRig Studio
AppVersion={#AppVersion}
AppPublisher=Ray Kim (NeuralRig Audio)
AppPublisherURL=https://github.com/srain0626/NeuralRigStudio
AppSupportURL=https://github.com/srain0626/NeuralRigStudio/issues
DefaultDirName={localappdata}\Programs\NeuralRigStudio
DefaultGroupName=NeuralRig Studio
PrivilegesRequired=lowest
OutputDir=..\dist-release
OutputBaseFilename=NeuralRigStudio-v{#AppVersion}-Setup-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\obj\publish-app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NeuralRig Studio"; Filename: "{app}\NeuralRigStudio.exe"
Name: "{group}\Uninstall NeuralRig Studio"; Filename: "{uninstallexe}"
Name: "{autodesktop}\NeuralRig Studio"; Filename: "{app}\NeuralRigStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\NeuralRigStudio.exe"; Description: "{cm:LaunchProgram,NeuralRig Studio}"; Flags: nowait postinstall skipifsilent
