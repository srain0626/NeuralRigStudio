; NeuralRig Studio v3.2.0 Windows Installer Script (Inno Setup 6)

#ifndef AppVersion
  #define AppVersion "3.2.0"
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
Name: "installvst3"; Description: "Install VST3 Plugin ({localappdata}\Programs\Common\VST3)"; GroupDescription: "DAW Plugins:"
Name: "installclap"; Description: "Install CLAP Plugin ({localappdata}\Programs\Common\CLAP)"; GroupDescription: "DAW Plugins:"

[Files]
Source: "..\obj\publish-app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\obj\publish-app\Plugins\VST3\*"; DestDir: "{localappdata}\Programs\Common\VST3"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: installvst3
Source: "..\obj\publish-app\Plugins\CLAP\*"; DestDir: "{localappdata}\Programs\Common\CLAP"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: installclap

[Icons]
Name: "{group}\NeuralRig Studio"; Filename: "{app}\NeuralRigStudio.exe"
Name: "{group}\Uninstall NeuralRig Studio"; Filename: "{uninstallexe}"
Name: "{autodesktop}\NeuralRig Studio"; Filename: "{app}\NeuralRigStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\NeuralRigStudio.exe"; Description: "{cm:LaunchProgram,NeuralRig Studio}"; Flags: nowait postinstall skipifsilent
