; CoreBi - instalador para Windows x64 (Inno Setup 6).
; Compilado por tools\Installer.ps1 a partir de artifacts\publish\win-x64.

#ifndef AppVersion
  #define AppVersion "0.2.3"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{8E4A1C3B-6F2D-4B7A-9D15-3C0E7A92F4B8}
AppName=CoreBi
AppVersion={#AppVersion}
AppVerName=CoreBi {#AppVersion}
AppPublisher=CoreBi contributors
AppPublisherURL=https://github.com/GabrielAngelimm/CoreBi
AppSupportURL=https://github.com/GabrielAngelimm/CoreBi/issues
DefaultDirName={autopf}\CoreBi
DefaultGroupName=CoreBi
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=CoreBi-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\CoreBi.Desktop\Assets\corebi.ico
UninstallDisplayIcon={app}\CoreBi.exe
VersionInfoVersion={#AppVersion}.0
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Types]
Name: "full"; Description: "Instalação completa"
Name: "compact"; Description: "Somente o aplicativo"
Name: "custom"; Description: "Personalizada"; Flags: iscustom

[Components]
Name: "app"; Description: "CoreBi"; Types: full compact custom; Flags: fixed
Name: "pawnio"; Description: "Driver de sensores PawnIO (recomendado)"; Types: full
Name: "rtss"; Description: "RivaTuner Statistics Server (RTSS) para o overlay em tela cheia (recomendado)"; Types: full

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CoreBi"; Filename: "{app}\CoreBi.exe"; WorkingDir: "{app}"; Components: app
Name: "{group}\Desinstalar CoreBi"; Filename: "{uninstallexe}"; Components: app
Name: "{autodesktop}\CoreBi"; Filename: "{app}\CoreBi.exe"; WorkingDir: "{app}"; Components: app; Tasks: desktopicon

[Run]
Filename: "{app}\CoreBi.exe"; Description: "Abrir o CoreBi"; WorkingDir: "{app}"; Components: app; Flags: nowait postinstall skipifsilent

[Code]
const
  UninstallRoot = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\';
  DescPawnIO = 'Driver assinado que permite ler a temperatura, o consumo e o clock internos da CPU. Sem ele o CoreBi funciona, mas esses sensores ficam indisponíveis. Baixado do repositório oficial do winget (não vem dentro deste instalador).';
  DescRtss = 'Serviço de OSD (o mesmo do MSI Afterburner) que permite exibir o OverlayBi dentro de jogos em tela cheia exclusiva. Pode ser desmarcado: sem ele o overlay continua funcionando em jogos em janela e sem bordas. Baixado pelo winget (Guru3D.RTSS); a licença do RTSS não permite redistribuí-lo no instalador.';

var
  DescLabel: TNewStaticText;

function ChaveInstalada(const Nome: String): Boolean;
begin
  Result := RegKeyExists(HKLM64, UninstallRoot + Nome) or RegKeyExists(HKLM32, UninstallRoot + Nome);
end;

procedure InstalarPeloWinget(const Id, Nome, Consequencia: String);
var
  ResultCode: Integer;
begin
  { Mesmo caminho que o app usa na página de Sensores; 3010 significa que o Windows pede reinício. }
  if not Exec(ExpandConstant('{cmd}'),
    '/C winget install --id ' + Id + ' --exact --source winget --accept-package-agreements --silent --accept-source-agreements',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or ((ResultCode <> 0) and (ResultCode <> 3010)) then
    MsgBox('Não foi possível instalar ' + Nome + ' agora. O CoreBi funciona normalmente, mas ' + Consequencia + ' Você pode instalar depois pelo winget (' + Id + ') ou pelo site oficial.', mbInformation, MB_OK);
end;

procedure MostrarDescricao(Sender: TObject);
var
  I: Integer;
begin
  I := WizardForm.ComponentsList.ItemIndex;
  if I < 0 then
    DescLabel.Caption := 'Selecione um componente para ver o que ele faz.'
  else if Pos('PawnIO', WizardForm.ComponentsList.ItemCaption[I]) > 0 then
    DescLabel.Caption := DescPawnIO
  else if Pos('RTSS', WizardForm.ComponentsList.ItemCaption[I]) > 0 then
    DescLabel.Caption := DescRtss
  else
    DescLabel.Caption := 'O aplicativo CoreBi. Sempre instalado.';
end;

procedure InitializeWizard;
begin
  WizardForm.ComponentsList.Height := WizardForm.ComponentsList.Height - ScaleY(64);
  DescLabel := TNewStaticText.Create(WizardForm);
  DescLabel.Parent := WizardForm.SelectComponentsPage;
  DescLabel.Left := WizardForm.ComponentsList.Left;
  DescLabel.Top := WizardForm.ComponentsList.Top + WizardForm.ComponentsList.Height + ScaleY(8);
  DescLabel.Width := WizardForm.ComponentsList.Width;
  DescLabel.Height := ScaleY(56);
  DescLabel.AutoSize := False;
  DescLabel.WordWrap := True;
  WizardForm.ComponentsList.OnClick := @MostrarDescricao;
  WizardForm.ComponentsList.OnClickCheck := @MostrarDescricao;
  MostrarDescricao(nil);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then Exit;
  if WizardIsComponentSelected('pawnio') and not ChaveInstalada('PawnIO') then
    InstalarPeloWinget('namazso.PawnIO', 'o driver PawnIO', 'os sensores internos da CPU ficam indisponíveis.');
  if WizardIsComponentSelected('rtss') and not ChaveInstalada('RTSS') then
    InstalarPeloWinget('Guru3D.RTSS', 'o RTSS', 'o overlay não aparece em jogos em tela cheia exclusiva.');
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    { Os backups em ProgramData restauram entradas de inicialização removidas pelo app; por isso o padrão é manter. }
    if MsgBox('Remover também as configurações, o histórico e os backups de inicialização do CoreBi?' + #13#10#13#10 +
      'Os backups em ProgramData são necessários para restaurar entradas de inicialização removidas pelo app.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\CoreBi'), True, True, True);
      DelTree(ExpandConstant('{commonappdata}\CoreBi'), True, True, True);
    end;
  end;
end;
