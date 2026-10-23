# Ferramentas e implementação

| Ferramenta | O que faz e como obtém os dados | Limite prático |
| --- | --- | --- |
| Sistema | WMI/CIM: Windows, CPU, placa-mãe/BIOS, módulos de RAM, dispositivos e unidades; organiza árvores de informações | Firmware pode omitir campos. WMI não substitui leitura completa de SPD como CPU-Z |
| Sensores | LibreHardwareMonitor lê CPU, GPU, placa-mãe, armazenamento e rede; um coletor compartilhado mantém valor atual/mínimo/máximo | Suporte depende de driver, acesso administrativo e hardware. CPU interna depende de PawnIO |
| Placa de vídeo | WMI, DXGI, NVML NVIDIA, sensores e tabela de especificações | Campos de tabela são referência do modelo, não medições elétricas. Não tem toda a cobertura do GPU-Z |
| Disco | Inventário/volumes, capacidade, sensores SMART e confiabilidade de armazenamento do Windows | Uma avaliação resumida não garante ausência de defeitos. USB/controladores podem ocultar SMART |
| Energia | WMI de bateria/capacidades/ciclos, plano de energia e potência dos sensores | Desktop não tem bateria. Não mede consumo da tomada nem capacidade/qualidade da fonte |
| Rede | ICMP ao gateway/internet, DNS, jitter/perda, tráfego HTTP de download/upload em Cloudflare e latência sob carga | Depende do servidor e rota. HTTP funcional com ICMP sem resposta é latência inconclusiva, não offline; DNS pode usar cache |
| FPS estimado | Tabelas de referência, índices CPU/GPU e fatores de resolução/preset/upscaling; API HowManyFPS opcional com cache | Modelo aproximado; hardware desconhecido fica sem referência. API com cache limitado/validado, mas contrato/autenticação ainda precisam de chave real |
| Gargalo | Combina carga, maior thread, temperaturas, RAM/VRAM e FPS em regras e sessão de amostragem | Heurística contextual; não representa um percentual universal de gargalo. Dados ausentes produzem estado desconhecido |
| Overlay | Janela transparente, métricas compartilhadas, RAM física via API Windows, carga da GPU via contadores WDDM/PDH, atalho global e eventos ETW Present de DXGI/D3D9 | Conta apresentações, não reconstrói frames exibidos como o pipeline completo de PresentMon. Carga da GPU depende dos contadores do driver; tela cheia exclusiva não é suportada por esta janela |
| Benchmark CPU | Path tracing em blocos, cena fixa, amostras por pixel, execução single/multi e pontuação pela taxa de amostras | Índice próprio; não equivale ao benchmark do CPU-Z/Cinebench |
| Benchmark GPU | Cena rasterizada D3D11 com iluminação/pós-processamento, contagem de frames e tempos | Resultado daquela cena e preset, não FPS de jogos comerciais |
| Ray tracing | D3D12/DXR, aceleração por hardware, aquecimento e múltiplas passagens, mediana/variação | Exige GPU/driver compatíveis. Repetibilidade não equivale à calibração externa |
| Simulação CPU | 30 mil entidades, grade espacial, física/IA, trabalho serial/paralelo e frame times | Carga sintética representativa de uma engine, não jogo real |
| RAM | Pointer chasing determinístico para latência; cópia/leitura de buffers grandes para banda | JIT, caches, TLB e configuração do sistema influenciam. Não é o mesmo método do AIDA64 |
| Carregamento | Pacote grande e muitos arquivos pequenos reais, com leitura sem cache de arquivos quando aplicável | Modelo sintético de carregamento, sem compressão/shaders de um jogo real |
| Disco benchmark | I/O sequencial e aleatório de 4 KiB, buffers alinhados, arquivo temporário e leitura sem cache do Windows; Q8 usa workers separados | MiB/s e IOPS reais, mas parâmetros/fila/tempo diferem de CrystalDiskMark. Q8 significa concorrência de workers síncronos |
| Stress | CPU em todos os threads; RAM com padrões e verificação; GPU com cena de carga e monitoramento de temperaturas quando disponíveis | Teste curto prova execução/parada. Estabilidade térmica e precisão da proteção requerem ensaios prolongados |
| Inicialização | Registro Run/StartupApproved e pastas de atalhos; ativar/desativar, backup/remoção e restauração | Backup durável protegido antes de remover; restauração por ID recusa conflitos. Falhas testadas com backend isolado; alterações reais ainda pendentes |
| Drivers | Inventário WMI, consulta de versão de GPU, download com verificação de assinatura, Windows Update e backup pnputil | Download exige HTTPS oficial e publicador exato/Authenticode. Consulta NVIDIA e busca WU testadas; instalação real e fluxo AMD pendentes |
| Instalar apps | Catálogo curado, inventário do Windows e execução do winget por identificador | Argumentos separados e ID validado. Versão/inventário testados; instalação real depende de fonte, rede e instaladores de terceiros |
| Relatórios e histórico | Exporta informações coletadas e resultados; mantém configurações/históricos locais próprios | Os dados refletem a disponibilidade no momento da coleta; a exportação pode incluir identificadores do computador |

## Relação com utilitários conhecidos

Os princípios de leitura de sensores e SMART são semelhantes aos de HWMonitor/CrystalDiskInfo: bibliotecas, drivers e APIs acessam registros/contadores expostos pelo hardware. O CoreBi reutiliza LibreHardwareMonitor, não o código desses programas. CPU-Z/GPU-Z têm coletores e bases próprias; campos iguais não provam cobertura ou precisão equivalentes.

Os benchmarks executam trabalho real e usam temporização. Seus algoritmos, cenas, tamanhos e filas são próprios. Números de CPU-Z, AIDA64 ou CrystalDiskMark só podem ser comparados quando o método e as unidades forem compatíveis. Não há alegação de equivalência numérica.
