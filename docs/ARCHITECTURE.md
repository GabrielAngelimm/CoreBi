# Arquitetura

## Dependências e responsabilidades

```text
CoreBi/
  brand/                         Geometria autoral, SVGs, PNGs e ícone Windows
  installer/                     Script do Inno Setup
  src/
    CoreBi.Engine/               Regras, coleta, algoritmos e integrações Windows (sem WPF)
      Monitoring/                Inventory, Sensors, Graphics, Storage, Power, Network,
                                 Memory, Devices, Diagnostics e Health (cartões de saúde);
                                 InfoNode (árvore chave/valor) fica na raiz, usada por várias áreas
      Performance/               Benchmarks, Stress, Graphics (D3D), Shaders e WorkloadCoordinator
      Games/                     ETW (FrameMonitor), gargalo, OverlayBi/RTSS e catálogo de hardware
      Tools/                     Startup, Drivers e Apps
      Infrastructure/            Auxiliares sem regra de produto: JSON atômico, processos, ACL,
                                 WMI, COM, log local, nomes e bytes formatados
      Persistence/               Configurações e normalização
      Reports/                   Relatórios HTML/JSON/PDF a partir de leituras coletadas
    CoreBi.Desktop/              WPF: apresentação e composição
      Shell/                     Janela principal (MainWindow + partes Sidebar, Navigation e Palette),
                                 tema (ThemeManager, Theme.xaml), toast e Win32Window
      Controls/                  Blocos visuais reutilizáveis: InstrumentBase, ObservableObject,
                                 painéis de layout, conversores, HwIcon, propriedades anexadas (Ui),
                                 entrada de página (PageMotion) e esqueleto de carregamento (Skeleton)
      Views/<Área>/              14 páginas autocontidas com handlers e estado próprios
      Features/<Área>/           Apresentação por domínio (fichas, instrumentos, overlay, exportação)
      Services/                  FeatureContext, HardwareDeviceWatcher, permissões e links
      Assets/                    Marca e ícone
  tests/CoreBi.Tests/            Executor de regressões, integração, WPF e hardware
  tools/                         Build, testes, publicação, licenças e marca
  docs/                          Guias, contratos, segurança e validação
  licenses/                      Textos e avisos redistribuídos com o binário
```

`Desktop → Engine` consome apenas a API pública do Engine (`Engine` não referencia WPF nem expõe internos para o Desktop). `Tests` acessa membros internos para verificar regras e composição sem criar APIs públicas apenas para teste; por isso o que só o Engine usa é `internal`. Tipos ficam próximos de seus consumidores. Não há contêiner de DI, projeto por classe, ORM ou framework adicional sem necessidade.

### Regras de organização

- **Engine:** namespace = pasta (`CoreBi.Engine.Monitoring.Storage` ↔ `Monitoring/Storage/`). Uma regra que a interface precisa (limiares, pontuações, vereditos) mora no Engine e o Desktop só a exibe; duplicar a conta na view é bug esperando acontecer.
- **Desktop:** pastas organizam, mas tipos referenciados por XAML ficam no namespace `CoreBi.Desktop` (prefixo `local:`), porque mudar o namespace exigiria editar o XAML. Páginas ficam em `CoreBi.Desktop.Views`.
- **Um arquivo, uma responsabilidade.** Classes grandes se dividem em `partial` por assunto (`MainWindow.Sidebar.cs`, `SystemSheets.Memory.cs`, `BenchmarksView.Gpu.cs`), não por tamanho arbitrário.
- **Comentários explicam o porquê** (restrição de hardware, anticheat, compatibilidade com o Explorador). Não remova um comentário de decisão numa limpeza sem levar a decisão junto.

## Coleta e vida útil

`WindowsGpuUsage` coleta PDH em background a cada segundo, fora do dispatcher, e publica um snapshot imutável por LUID do adaptador DXGI. Soma a ocupação dos processos de cada motor físico e escolhe o motor mais ocupado, seguindo a definição do Gerenciador de Tarefas. Contador indisponível ou ainda aquecendo produz valor ausente, sem substituir silenciosamente pela carga do núcleo do driver. O encerramento cancela o worker e libera a consulta PDH. `MemoryInfo.ReadPhysicalUsage` mantém total, disponível, usado e percentual da mesma amostra de RAM física; não seleciona por nome os sensores de memória física/virtual da biblioteca.

A RAM usa `GlobalMemoryStatusEx`, independente de fabricante de CPU, GPU ou módulo de memória, no Windows x64 compatível com o CoreBi. O percentual usa a capacidade física utilizável informada pelo sistema, que pode ser menor que a instalada por reservas de hardware. A carga da GPU não depende de NVML e pode atender NVIDIA, AMD e Intel quando o driver WDDM 2.0+ expõe os contadores `GPU Engine`. Ainda requer testes físicos nas outras famílias; driver sem contadores, falha de consulta ou correspondência ambígua entre adaptadores mantém a leitura indisponível. A associação atual por fabricante/device ID exige um único adaptador DXGI correspondente, portanto duas placas idênticas não recebem uma porcentagem arbitrária.

Uma instância de `SensorMonitor` atende as páginas e o overlay. A abertura é idempotente; fechar durante a abertura libera o hardware ao concluir. A coleta protege operações de abrir/atualizar/fechar e isola falhas por dispositivo. O dispatcher atualiza a interface; consumidores de background recebem `SensorReading` imutáveis de `Snapshot()`, evitando enumerar a coleção WPF enquanto ela muda. Valores não finitos ficam indisponíveis.

`FrameMonitor` inicia ETW sob demanda para gargalo/overlay/sessão, com nome exclusivo por processo. A parada descarta a sessão e aguarda seu worker. A janela possui token de vida útil; callbacks assíncronos verificam o fechamento antes de alterar a apresentação. O coletor USB aplica debounce. Falhas de coleta não são convertidas em medidas válidas.

`WorkloadCoordinator` permite uma única carga pesada, incluindo preparação e limpeza. O lease é idempotente e pertence à operação que o recebeu. Stress continua ocupando o coordenador enquanto seus workers ainda estão parando. Falhas de worker e cancelamento são propagados; não se retorna uma medição parcial como teste concluído.

Shaders HLSL ficam em arquivos próprios incorporados ao assembly. A extração preservou os algoritmos e foi validada executando as cenas reais. A compilação dos shaders continua em tempo de execução por D3DCompiler/DXC.

## Páginas autocontidas e serviços

Todas as 14 páginas (`OverviewView`, `SystemView`, `SensorsView`, `GraphicsView`, `DisksView`, `PowerView`, `NetworkView`, `OverlayView`, `BottleneckView`, `BenchmarksView`, `StressView`, `StartupView`, `AppsView` e `DriversView`) possuem seus próprios handlers, modelos de visualização e ciclo de vida. Cada uma recebe um `FeatureContext` em `Initialize` e fala com o shell só por ele.

`FeatureContext` é o ponto único de composição: janela proprietária, `WorkloadCoordinator`, token de vida útil, configurações, coletores compartilhados (`SensorMonitor`, `FrameMonitor`), navegação, registro de eventos, notificações, isolamento de benchmarks e operações críticas. As dependências são propriedades `required`: esquecer uma ao montar o contexto é erro de compilação. Os callbacks têm delegados nomeados (`RecordEventHandler`, `NotifyHandler`, `BenchmarkIsolationHandler`) para que os parâmetros tenham nome. `HardwareDeviceWatcher` centraliza o `WM_DEVICECHANGE` (volumes removíveis, USB e troca de driver de vídeo), e `ReportExportService` encapsula a exportação de relatórios HTML/PDF/JSON.

O shell chama as páginas por um contrato de nomes fixos (nem toda página implementa todos):

| Método | Quando o shell chama |
|---|---|
| `Tick()` | A cada segundo, no timer de telemetria (a página decide se está visível) |
| `RefreshAsync()` | Ao abrir a página, depois do primeiro quadro dela (os painéis que esperam a leitura mostram o esqueleto) |
| `RefreshHardware()` | Quando placas de vídeo, processador ou discos terminam de ser lidos ou mudam |
| `RedrawTheme()` | Na troca de tema claro/escuro, para desenhos feitos em código |

A navegação usa `nameof(PageX)`, e o `Tag` de cada item da barra lateral é o nome do campo da página no XAML.

Chamadas assíncronas de atualização retornam `Task`, permitindo aguardar e validar o carregamento. Coletas em andamento deixam de atualizar a página após o fechamento. Tema pertence ao shell; benchmark pertence a `Views/Performance/BenchmarksView*.cs`; exportação pertence a `Features/Reports`.

As operações críticas bloqueiam o fechamento enquanto são gerenciadas pelo aplicativo, exibindo a mensagem da operação ativa. Um instalador oficial que já foi aberto continua em seu próprio processo. Serviços Windows permanecem no Engine:

- `AppInstaller`: catálogo, detecção local, instalação interativa e argumentos explícitos de winget.
- `DriverInventory`, `GpuDriverLookup`, `DriverBackup`, `DriverInstaller` e `WindowsUpdateService`: inventário, metadados oficiais, download/assinatura/execução, backup pnputil e escopo COM com resultados individuais.
- `StartupService` e `StartupManager`: backup durável antes de remover (com rollback em caso de falha), registro canônico protegido e restauração por ID. A interface não fornece caminhos arbitrários para restaurar.
- `ProcessRunner`: `ArgumentList`, leitura concorrente/buffer limitado de stdout e stderr, cancelamento da árvore de processo criada pelo CoreBi.
- `ProtectedWorkspace`: diretórios administrativos sem herança ampla, com proprietário e ACL verificados; recusa reparse points.

O download percorre somente HTTPS em domínios oficiais NVIDIA/AMD, valida cada redirecionamento e exige Authenticode confiável com publicador exato. O instalador é iniciado com um handle de leitura que impede substituição do arquivo entre validação e lançamento. Esses controles têm testes de política e ACL; a instalação real ainda exige ensaio separado.

## Apresentação

O OverlayBi possui dois caminhos: a janela WPF original e a publicação opcional no RTSS já em execução. `RtssOverlayClient`, no Engine, valida o ABI/limites, usa lock não bloqueante, possui somente seu slot e libera as seções mapeadas em cada operação. `OverlayTarget` identifica a geometria do processo em primeiro plano sem tentar adivinhar o modo exclusivo. O Desktop mede o painel canônico e projeta seus textos, cores, separadores, gráfico e raio do cartão em camadas RTSS, sem duplicar seu layout por tabulações nem usar capturas/imagens. A fonte/zoom do conteúdo é explícita, sem alterar perfis. Só oculta WPF após observar o consumo OSD; falhas e ausência do servidor mantêm o caminho anterior. Não há DLL de hook ou processo auxiliar incluído. Contrato, configuração e limitações em [OVERLAY.md](OVERLAY.md).

A interface segue o design system em [DESIGN.md](DESIGN.md) mantendo WPF direto sem framework MVVM externo: cada view gerencia seu próprio estado, usando `ObservableObject` para notificações de leitura ao vivo e `InstrumentBase` para gráficos e medidores customizados sensíveis ao tema.

O shell (`MainWindow.xaml.cs` e as partes `Sidebar`, `Navigation` e `Palette`) concentra apenas o que é transversal: janela sem moldura (`Win32Window`), navegação, tema (`ThemeManager`, `Theme.xaml`), paleta de comandos, telemetria da barra lateral e orquestração do ciclo de vida. Propriedades anexadas do design system ficam em `Controls/Ui.cs`. Cores de estados definidos em código usam `SetResourceReference`; desenhos em código (gráficos, anéis) usam `ThemeManager.Brush(chave)` e são refeitos na troca de tema pelo `RedrawTheme()` de cada página. `tools/check_resources.py` confere as chaves de recurso, que o compilador de XAML não valida.

O tique de 1 s lê os sensores em duas metades: `SensorMonitor.PollAsync()` faz a parte lenta (a biblioteca de hardware, dezenas de ms) fora da thread da janela, com trava própria do hardware, e `Apply()` passa os valores às linhas na thread da interface. `Pause`, o isolamento de benchmark (`WaitForPoll`) e o fechamento esperam a leitura em andamento.

Novas regras entram no Engine. Templates e tema continuam compartilhados em `Shell`; controles reutilizáveis entram em `Controls`.

## Persistência e erro

`AtomicJsonFile` serializa em temporário exclusivo no mesmo diretório, sincroniza e substitui o destino. Falha de serialização preserva o arquivo anterior. Configurações são normalizadas; arquivo inválido é preservado para diagnóstico.

Logs locais limitados conservam operação, tipo e stack da exceção; omitem mensagens que possam carregar credenciais. Erros de gravação são informados na interface. Logs não são enviados automaticamente.

Backups antigos contidos no JSON editável não são automaticamente convertidos em comandos elevados de restauração. Eles são preservados; uma futura importação precisaria revalidar dados e origem.

## Personalizar sem quebrar outras páginas

Remova página, navegação, handlers e acessos correspondentes; depois elimine apenas serviços/dependências sem consumidores. Sensores, inventário, configurações e coordenador são compartilhados. Vortice atende raster/DXR/stress; TraceEvent atende ETW; LibreHardwareMonitor atende diversas leituras. Atualize testes e documentação quando alterar algoritmo, unidade ou suporte. Nenhuma regra de produto deve ficar no adaptador de controles.
