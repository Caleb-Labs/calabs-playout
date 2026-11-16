; Instalador de CaLabs Playout. Orden de las pantallas: terminos y condiciones (hay que aceptar) -> carpeta de instalacion -> opciones -> instalar.
#define NombreApp "CaLabs Playout"
#define Version "2.0.0"

[Setup]
AppId={{8F3A6B2E-4C1D-4E7A-9B5F-2D6C1A7E9F10}
AppName={#NombreApp}
AppVersion={#Version}
AppVerName={#NombreApp} {#Version}
AppPublisher=CaLabs
DefaultDirName=C:\CaLabs Playout
DefaultGroupName=CaLabs
DisableProgramGroupPage=yes
LicenseFile=terminos.txt
OutputDir=salida
OutputBaseFilename=CaLabs-Playout-{#Version}-Setup
SetupIconFile=icono.ico
UninstallDisplayIcon={app}\bin\CaLabs.Parrilla.exe
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "autoinicio"; Description: "Iniciar el playout automáticamente con Windows"; GroupDescription: "Opciones:"
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Opciones:"; Flags: unchecked

[Dirs]
Name: "{app}\datos"; Permissions: users-modify
Name: "{app}\motor"; Permissions: users-modify

[Files]
Source: "staging\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "staging\ui\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "staging\motor\*"; DestDir: "{app}\motor"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "staging\config.json"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "terminos.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\CaLabs Playout"; Filename: "{app}\bin\CaLabs.Parrilla.exe"; WorkingDir: "{app}\bin"
Name: "{autoprograms}\Desinstalar CaLabs Playout"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CaLabs Playout"; Filename: "{app}\bin\CaLabs.Parrilla.exe"; WorkingDir: "{app}\bin"; Tasks: desktopicon
Name: "{commonstartup}\CaLabs Playout (arranque)"; Filename: "{app}\bin\CaLabs.Supervisor.exe"; WorkingDir: "{app}\bin"; Tasks: autoinicio

[Run]
Filename: "{app}\bin\CaLabs.Supervisor.exe"; WorkingDir: "{app}\bin"; Flags: nowait runasoriginaluser skipifsilent
Filename: "{app}\bin\CaLabs.Parrilla.exe"; WorkingDir: "{app}\bin"; Description: "Abrir CaLabs Playout ahora"; Flags: nowait postinstall runasoriginaluser skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM CaLabs.Supervisor.exe"; Flags: runhidden; RunOnceId: "cerrar1"
Filename: "taskkill.exe"; Parameters: "/F /IM CaLabs.Parrilla.exe"; Flags: runhidden; RunOnceId: "cerrar2"
Filename: "taskkill.exe"; Parameters: "/F /IM CaLabs.Programador.exe"; Flags: runhidden; RunOnceId: "cerrar3"
Filename: "taskkill.exe"; Parameters: "/F /IM casparcg.exe"; Flags: runhidden; RunOnceId: "cerrar4"

[UninstallDelete]
; el motor deja archivos propios al ejecutarse; los datos del usuario (carpeta datos y config.json) se conservan
Type: filesandordirs; Name: "{app}\motor"
Type: filesandordirs; Name: "{app}\bin"
Type: filesandordirs; Name: "{app}\ui"

[Code]
procedure Cerrar(const Exe: string);
var Codigo: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ' + Exe, '', SW_HIDE, ewWaitUntilTerminated, Codigo);
end;

// Si ya hay una version abierta (una actualizacion), se cierra antes de copiar los archivos.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Cerrar('CaLabs.Supervisor.exe'); Cerrar('CaLabs.Parrilla.exe'); Cerrar('CaLabs.Programador.exe'); Cerrar('casparcg.exe');
  Result := '';
end;

function HayWebView2: Boolean;
var v: String;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v)
         or RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v)
         or RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v);
  if Result then Result := (v <> '') and (v <> '0.0.0.0');
end;

function HayNDI: Boolean;
begin
  Result := (GetEnv('NDILIB_REDIST_FOLDER') <> '') or DirExists(ExpandConstant('{commonpf64}\NDI'));
end;

// Antes de instalar: avisos de lo que falta en este PC (no impiden instalar).
function NextButtonClick(CurPageID: Integer): Boolean;
var Aviso: String;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    Aviso := '';
    if not HayWebView2 then
      Aviso := Aviso + '• Falta Microsoft Edge WebView2 Runtime: la ventana de control lo necesita (Windows 11 ya lo trae). Descárgalo de microsoft.com/edge/webview2 antes de abrir el programa.' + #13#10#13#10;
    if not HayNDI then
      Aviso := Aviso + '• No se encontró NDI: la salida NDI no funcionará hasta instalar NDI Tools (ndi.video). El resto del programa funciona igual.' + #13#10#13#10;
    if Aviso <> '' then
      MsgBox('Antes de continuar, ten en cuenta:' + #13#10#13#10 + Aviso + 'Puedes seguir con la instalación.', mbInformation, MB_OK);
  end;
end;
