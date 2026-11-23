; CoreBi - instalador para Windows x64 (Inno Setup 6).
; Compilado por tools\Installer.ps1 a partir de artifacts\publish\win-x64.

#ifndef AppVersion
  #define AppVersion "1.0.0"
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
AppPublisher=Gabriel Angelim
AppCopyright=Copyright (c) 2026 Gabriel Angelim. Licença MIT.
AppPublisherURL=https://github.com/GabrielAngelimm/CoreBi
AppSupportURL=https://github.com/GabrielAngelimm/CoreBi/issues
AppUpdatesURL=https://github.com/GabrielAngelimm/CoreBi/releases
DefaultDirName={autopf}\CoreBi
DefaultGroupName=CoreBi
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=CoreBi-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\CoreBi.Desktop\Assets\corebi.ico
UninstallDisplayIcon={app}\CoreBi.exe
UninstallDisplayName=CoreBi
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=CoreBi
VersionInfoDescription=Instalador do CoreBi
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
SetupLogging=yes
; Visual: segue o tema claro ou escuro do Windows, com a barra de título no mesmo tom. O painel das boas-vindas e da
; conclusão é a barra lateral do app (fundo, símbolo e a cor de cada peça); o símbolo do canto troca com o tema.
; As imagens saem de tools/generate_installer_art.py, uma por escala de tela, e o Setup escolhe a mais próxima.
WizardStyle=modern dynamic windows11 includetitlebar
DisableWelcomePage=no
WizardImageFile=images\painel-*.png
WizardImageBackColor=#0A0D0F
; No modo escuro o Inno usa uma imagem própria se esta não for repetida: o painel já é escuro e serve aos dois
WizardImageFileDynamicDark=images\painel-*.png
WizardImageBackColorDynamicDark=#0A0D0F
WizardSmallImageFile=images\simbolo-claro-*.png
WizardSmallImageFileDynamicDark=images\simbolo-escuro-*.png

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
; A tradução padrão usa "pra" e títulos com cada palavra em maiúscula; aqui o texto segue o do app
WelcomeLabel1=Bem-vindo ao CoreBi {#AppVersion}
WelcomeLabel2=Monitoramento, diagnóstico e testes de hardware para Windows, num app só e sem conta.%n%nEste assistente instala o CoreBi no seu computador.
ClickNext=Clique em Avançar para continuar ou em Cancelar para sair.
WizardSelectDir=Pasta de instalação
SelectDirDesc=Onde o [name] deve ser instalado?
SelectDirLabel3=O [name] será instalado nesta pasta.
SelectDirBrowseLabel=Para continuar, clique em Avançar. Para escolher outra pasta, clique em Procurar.
DiskSpaceGBLabel=São necessários pelo menos [gb] GB livres em disco.
DiskSpaceMBLabel=São necessários pelo menos [mb] MB livres em disco.
DirExistsTitle=A pasta já existe
DirExists=A pasta:%n%n%1%n%njá existe. Instalar nela mesmo assim?
WizardSelectComponents=Componentes
SelectComponentsDesc=O que deve ser instalado?
SelectComponentsLabel2=Marque o que você quer instalar e desmarque o que não quer. Clique em Avançar para continuar.
ComponentsDiskSpaceGBLabel=A seleção atual precisa de pelo menos [gb] GB em disco.
ComponentsDiskSpaceMBLabel=A seleção atual precisa de pelo menos [mb] MB em disco.
WizardSelectTasks=Atalhos
SelectTasksDesc=Quais atalhos devem ser criados?
SelectTasksLabel2=Escolha os atalhos do [name] e clique em Avançar.
WizardReady=Pronto para instalar
ReadyLabel1=Tudo pronto para instalar o [name] no seu computador.
ReadyLabel2a=Clique em Instalar para continuar ou em Voltar para revisar alguma opção.
ReadyLabel2b=Clique em Instalar para continuar.
WizardPreparing=Preparando a instalação
WizardInstalling=Instalando
InstallingLabel=Aguarde enquanto o [name] é instalado no seu computador.
ClickFinish=Clique em Concluir para sair do instalador.
ExitSetupTitle=Sair do instalador
UninstallStatusLabel=Aguarde enquanto o %1 é removido do seu computador.
UninstalledAll=O %1 foi removido do seu computador.
FinishedHeadingLabel=O CoreBi está pronto
FinishedLabel=O CoreBi foi instalado e está no menu Iniciar. Ele abre como administrador, que é o que permite ler os sensores e cuidar do sistema.
FinishedLabelNoIcons=O CoreBi foi instalado. Ele abre como administrador, que é o que permite ler os sensores e cuidar do sistema.

[Types]
Name: "full"; Description: "Instalação completa"
Name: "compact"; Description: "Somente o aplicativo"
Name: "custom"; Description: "Personalizada"; Flags: iscustom

[Components]
Name: "app"; Description: "CoreBi"; Types: full compact custom; Flags: fixed
Name: "pawnio"; Description: "Driver de sensores PawnIO (recomendado)"; Types: full
Name: "rtss"; Description: "RivaTuner Statistics Server (RTSS) para o overlay em tela cheia (recomendado)"; Types: full

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: unchecked

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
  DescPawnIO = 'Driver assinado que permite ler a temperatura, o consumo e o clock internos da CPU. Sem ele o CoreBi funciona, mas esses sensores ficam indisponíveis. Baixado do repositório oficial do winget (não vem dentro deste instalador); mantê-lo marcado aceita a licença do PawnIO.';
  DescRtss = 'Serviço de OSD (o mesmo do MSI Afterburner) que permite exibir o OverlayBi dentro de jogos em tela cheia exclusiva. Pode ser desmarcado: sem ele o overlay continua funcionando em jogos em janela e sem bordas. Baixado pelo winget (Guru3D.RTSS): a licença do RTSS não permite incluí-lo aqui, e mantê-lo marcado aceita essa licença.';

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
    if not WizardSilent then
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
  { A lista tem três itens: a sobra vai para a descrição, que com a licença chega a cinco linhas }
  WizardForm.ComponentsList.Height := WizardForm.ComponentsList.Height - ScaleY(80);
  DescLabel := TNewStaticText.Create(WizardForm);
  DescLabel.Parent := WizardForm.SelectComponentsPage;
  DescLabel.Left := WizardForm.ComponentsList.Left;
  DescLabel.Top := WizardForm.ComponentsList.Top + WizardForm.ComponentsList.Height + ScaleY(8);
  DescLabel.Width := WizardForm.ComponentsList.Width;
  DescLabel.Height := ScaleY(72);
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
