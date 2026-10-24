# Arquitetura

## Dependências e responsabilidades

```text
CoreBi/
  brand/                         Geometria autoral, SVGs, PNGs e ícone Windows
  src/
    CoreBi.Engine/               Regras, coleta, algoritmos e integrações Windows
      Monitoring/                Inventory, Sensors, Graphics, Storage, Power,
                                 Network, Memory, Devices e Diagnostics
      Performance/               Benchmarks, Stress, Graphics, Shaders e coordenador
      Games/                     ETW, métricas, gargalo, catálogo e API opcional
      Tools/                     Startup, Drivers e Apps
      Infrastructure/            JSON atômico, processos, ACL e diagnóstico local
      Persistence/               Configurações, normalização e proteção DPAPI
      Reports/                   Relatórios a partir de leituras coletadas
    CoreBi.Desktop/              WPF: apresentação e composição
      Shell/                     Janela, navegação, tema, recursos e adaptador
      Views/                     15 páginas; 6 já possuem handlers/estado próprios
      Features/                  Apresentação por domínio e exportação de relatórios
      Services/                  Contexto mínimo de vida útil, permissões e links
      Assets/                    Marca e ícone
  tests/CoreBi.Tests/             Executor de regressões, integração, WPF e hardware
  tools/                         Build, testes, publicação, licenças e marca
  docs/                          Guias, contratos, segurança e validação
  licenses/                      Textos e avisos redistribuídos com o binário
```

`Desktop → Engine`; `Engine` não referencia WPF. `Tests` acessa membros internos para verificar regras e composição sem expor APIs públicas só para teste. Tipos ficam próximos de seus consumidores. Não há contêiner de DI, projeto por classe, ORM ou framework adicional sem necessidade.

## Coleta e vida útil

`WindowsGpuUsage` coleta PDH em background a cada segundo, fora do dispatcher, e publica um snapshot imutável por LUID do adaptador DXGI. Soma a ocupação dos processos de cada motor físico e escolhe o motor mais ocupado, seguindo a definição do Gerenciador de Tarefas. Contador indisponível ou ainda aquecendo produz valor ausente, sem substituir silenciosamente pela carga do núcleo do driver. O encerramento cancela o worker e libera a consulta PDH. `MemoryInfo.ReadPhysicalUsage` mantém total, disponível, usado e percentual da mesma amostra de RAM física; não seleciona por nome os sensores de memória física/virtual da biblioteca.

A RAM usa `GlobalMemoryStatusEx`, independente de fabricante de CPU, GPU ou módulo de memória, no Windows x64 compatível com o CoreBi. O percentual usa a capacidade física utilizável informada pelo sistema, que pode ser menor que a instalada por reservas de hardware. A carga da GPU não depende de NVML e pode atender NVIDIA, AMD e Intel quando o driver WDDM 2.0+ expõe os contadores `GPU Engine`. Ainda requer testes físicos nas outras famílias; driver sem contadores, falha de consulta ou correspondência ambígua entre adaptadores mantém a leitura indisponível. A associação atual por fabricante/device ID exige um único adaptador DXGI correspondente, portanto duas placas idênticas não recebem uma porcentagem arbitrária.

Uma instância de `SensorMonitor` atende as páginas e o overlay. A abertura é idempotente; fechar durante a abertura libera o hardware ao concluir. A coleta protege operações de abrir/atualizar/fechar e isola falhas por dispositivo. O dispatcher atualiza a interface; consumidores de background recebem `SensorReading` imutáveis de `Snapshot()`, evitando enumerar a coleção WPF enquanto ela muda. Valores não finitos ficam indisponíveis.

`FrameMonitor` inicia ETW sob demanda para gargalo/overlay/sessão, com nome exclusivo por processo. A parada descarta a sessão e aguarda seu worker. A janela possui token de vida útil; callbacks assíncronos verificam o fechamento antes de alterar a apresentação. O coletor USB aplica debounce. Falhas de coleta não são convertidas em medidas válidas.

`WorkloadCoordinator` permite uma única carga pesada, incluindo preparação e limpeza. O lease é idempotente e pertence à operação que o recebeu. Stress continua ocupando o coordenador enquanto seus workers ainda estão parando. Falhas de worker e cancelamento são propagados; não se retorna uma medição parcial como teste concluído.

Shaders HLSL ficam em arquivos próprios incorporados ao assembly. A extração preservou os algoritmos e foi validada executando as cenas reais. A compilação dos shaders continua em tempo de execução por D3DCompiler/DXC.

## Páginas com responsabilidade própria

`AppsView`, `DriversView`, `StartupView`, `DisksView`, `PowerView` e `NetworkView` possuem seus próprios handlers e estado de apresentação. `FeatureContext` compartilha apenas janela proprietária, coordenador, token de vida útil e operações críticas. Disco e Energia recebem o mesmo coletor de sensores; não abrem coletores adicionais. Rede mantém seu último resultado para a exportação e usa o coordenador comum para impedir cargas concorrentes.

Chamadas assíncronas de atualização retornam `Task`, permitindo aguardar e validar o carregamento. Coletas em andamento deixam de atualizar a página após o fechamento. Tema pertence ao shell; benchmark de disco pertence a `Features/Performance/DiskBenchmark`; exportação pertence a `Features/Reports`. Os arquivos de Rede/Disco não concentram mais esses fluxos sem relação.

As operações críticas bloqueiam o fechamento enquanto são gerenciadas pelo aplicativo. Um instalador oficial que já foi aberto continua em seu próprio processo. Serviços Windows permanecem no Engine.

- `AppInstaller`: catálogo, detecção local e argumentos explícitos de winget.
- `DriverManager`: inventário e metadados oficiais; `DriverInstaller`: download/assinatura/execução; `WindowsUpdateService`: escopo COM, identidades e resultados individuais.
- `StartupService`: backup durável antes de remover, registro canônico protegido e restauração por ID. `StartupManager`: acesso ao Registro/atalhos e verificação de conflitos. A interface não fornece caminhos arbitrários para restaurar.
- `ProcessRunner`: `ArgumentList`, leitura concorrente/buffer limitado de stdout e stderr, cancelamento da árvore de processo criada pelo CoreBi.
- `ProtectedWorkspace`: diretórios administrativos sem herança ampla, com proprietário e ACL verificados; recusa reparse points.

O download percorre somente HTTPS em domínios oficiais NVIDIA/AMD, valida cada redirecionamento e exige Authenticode confiável com publicador exato. O instalador é iniciado com um handle de leitura que impede substituição do arquivo entre validação e lançamento. Esses controles têm testes de política e ACL; a instalação real ainda exige ensaio separado.

## Apresentação

O OverlayBi possui dois caminhos: a janela WPF original e a publicação opcional no RTSS já em execução. `RtssOverlayClient`, no Engine, valida o ABI/limites, usa lock não bloqueante, possui somente seu slot e libera as seções mapeadas em cada operação. `OverlayTarget` identifica a geometria do processo em primeiro plano sem tentar adivinhar o modo exclusivo. O Desktop mede o painel canônico e projeta seus textos, cores, separadores, gráfico e raio do cartão em camadas RTSS, sem duplicar seu layout por tabulações nem usar capturas/imagens. A fonte/zoom do conteúdo é explícita, sem alterar perfis. Só oculta WPF após observar o consumo OSD; falhas e ausência do servidor mantêm o caminho anterior. Não há DLL de hook ou processo auxiliar incluído. Contrato, configuração e limitações em [OVERLAY.md](OVERLAY.md).

A interface foi redesenhada por completo (design system em [DESIGN.md](DESIGN.md)), mas a camada de apresentação **não foi convertida para MVVM**: a reformulação manteve os nomes de elementos, os handlers e o contrato dos testes, para trocar a aparência sem arriscar os fluxos. As demais ferramentas ainda usam arquivos parciais de `MainWindow`, páginas que encaminham eventos e acessos tipados em `MainWindow.ViewAccess.cs`. As seis páginas listadas acima já dispensam essa ponte.

O shell concentra o que é transversal: tema (`ThemeManager`, `Theme.xaml`), propriedades anexadas do design system (`Ui.cs`), paleta de comandos (`MainWindow.Palette.cs`), telemetria e chave de tema da barra lateral. Cores de estados definidos em código usam `SetResourceReference`; desenhos em código (gráficos, anéis) usam `MainWindow.Palette(chave)` e são refeitos em `RedrawThemedVisuals` na troca de tema. `tools/check_resources.py` confere as chaves de recurso, que o compilador de XAML não valida.

Novas regras entram no Engine. Ao evoluir uma página, mova seu estado/handlers para a própria página ou um presenter enxuto e elimine os acessos correspondentes da ponte. Um viewmodel deve resolver uma necessidade concreta, sem introduzir outro framework só para uniformidade. Templates e tema continuam compartilhados em `Shell`.

## Persistência e erro

`AtomicJsonFile` serializa em temporário exclusivo no mesmo diretório, sincroniza e substitui o destino. Falha de serialização preserva o arquivo anterior. Configurações são normalizadas; arquivo inválido é preservado para diagnóstico. Cache da API tem prazo, tamanho e quantidade limitados, valida valores e usa hash dos parâmetros/credencial na chave.

Logs locais limitados conservam operação, tipo e stack da exceção; omitem mensagens que possam carregar credenciais. Erros de gravação são informados na interface. Logs não são enviados automaticamente.

Backups antigos contidos no JSON editável não são automaticamente convertidos em comandos elevados de restauração. Eles são preservados; uma futura importação precisaria revalidar dados e origem.

## Personalizar sem quebrar outras páginas

Remova página, navegação, handlers e acessos correspondentes; depois elimine apenas serviços/dependências sem consumidores. Sensores, inventário, configurações e coordenador são compartilhados. Vortice atende raster/DXR/stress; TraceEvent atende ETW; LibreHardwareMonitor atende diversas leituras. Atualize testes e documentação quando alterar algoritmo, unidade ou suporte. Nenhuma regra de produto deve ficar no adaptador de controles.
