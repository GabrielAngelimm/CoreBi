# Mudanças

## Em desenvolvimento

- Navegação instantânea: ao abrir Sensores, Discos, Sistema, Energia, Inicialização ou Instalar apps pela primeira vez, a página troca no mesmo instante e mostra um esqueleto de carregamento enquanto monta e lê os dados em segundo plano. A leitura NVMe de Discos saiu da thread da janela.

## 0.2.1 — 2026-10-24

- Página do overlay: ícones dos grupos em cor neutra com glifos próprios (placa de vídeo, memória, processador), e a faixa "Detecção de FPS" enxuta, sem a lista de compatibilidade, só como funciona e o estado ("Aguardando o jogo").

- O limitador de desempenho NVIDIA (`gpu_perfcap`) saiu do OverlayBi; seleções salvas com ele são migradas sem perder os demais itens.
- O instalador ganhou explicação por componente e o componente RTSS, instalado em silêncio pelo winget junto do PawnIO (ambos marcados; a instalação "Somente o aplicativo" os dispensa). Nada de terceiros é incluído no setup.

- OverlayBi integra opcionalmente com RTSS aberto para apresentar o OSD dentro de jogos em tela cheia, inclusive exclusiva quando o jogo/driver/anticheat permitem. Usa memória compartilhada a 2 Hz, slot próprio, publicação não bloqueante e retorno ao painel WPF em falhas; sem novos pacotes, injeção própria ou alteração de perfis. Projeta as medidas do painel atual, com fonte/zoom próprios e cartão arredondado, preservando dados, cores, seleção e controles. Guia e limites em `docs/OVERLAY.md`.

- OverlayBi atualiza os textos e o gráfico sem recriar os elementos a cada amostra; os campos reservam espaço para a troca de dígitos e a janela só reaplica a posição quando necessário. Cores, fontes, tamanhos de texto, ordem e controles mantidos.
- RAM do overlay usa a mesma amostra física do Windows para GB e percentual: sensores de memória física e virtual do LibreHardwareMonitor têm o mesmo nome e podiam selecionar a memória virtual. Carga da GPU passa a usar os contadores de motores do Windows (motor mais ocupado, processos somados por motor e adaptador), compartilhada com a Visão geral, barra lateral e análise de gargalo. Temperaturas, clocks e demais sensores preservados.
- Janela maximizada respeita a barra de tarefas: antes ocupava a tela inteira e o rodapé de todas as páginas ficava escondido.
- O CoreBi abre sempre maximizado (com a barra de tarefas visível); Restaurar volta à janela de 1360 × 860 centralizada.
- Selo "ao vivo" da barra de título removido; o tema virou uma chave animada ("Modo escuro") acima da telemetria na barra lateral.
- Central de monitoramento na barra lateral com CPU, GPU, RAM e disco (no lugar do estado do teste de carga), oculta na Visão geral; marca maior no topo e a versão, só o número, na barra de título ao lado do botão de minimizar.
- Seleção da navegação e das listas sem o traço lateral: fundo elevado e ícone em verde-água, com transição curta.
- Visão geral cabe numa tela com a janela maximizada. Cartões de uso para processador e placa de vídeo e mostrador de anéis para RAM, VRAM e disco com uso e espaço livre, sem campos vazios. Setas levam a Sistema (processador, memória), Placa de vídeo e Discos; o gráfico principal mostra a leitura exata sob o mouse e pausa enquanto o mouse está sobre ele. Saúde do PC com selo de resumo e altura conforme o conteúdo; a atividade recente mostra quantos eventos couberem.
- Overlay cabe numa tela com a janela maximizada. O interruptor e o atalho ficam no cabeçalho; o monitor redesenhado mostra cantos tracejados clicáveis e uma prévia viva do overlay (a mesma caixa do jogo, com os itens marcados e os sensores ao vivo; o FPS é de exemplo quando não há jogo na frente). "O que mostrar" fica ao lado, com uma linha por componente (FPS, GPU, VRAM, CPU, RAM, Outros), itens em pílulas com visto, contagem e a predefinição atual marcada. A detecção de FPS desce para uma faixa com estado colorido e selos de compatibilidade. Opacidade ajustável (30% a 100%, 100% = totalmente opaco), três tamanhos maiores, traços verticais entre os dados de cada linha e dois novos presets: Gargalo e Térmico. A página passa a se chamar OverlayBi e abre a seção Jogos; "Padrão" virou "Essencial" (primeiro preset, também o padrão para quem nunca escolheu itens), com dicas específicas em cada preset. Nova opção Nome do hardware (ligada no Essencial): as linhas mostram o modelo, como CPU Ryzen 7 5700X, GPU RTX 4060 Ti e RAM 32 GB.
- Discos reformulada numa tela só com a janela maximizada. À esquerda, um rack com uma baia compacta por disco (nome, características e veredito em LED). À direita, o disco escolhido: placa com modelo, veredito, especificação e espaço por unidade; vida útil num arco em gomos com as faixas de atenção e crítica, desgaste, blocos de reserva e resistência estimada; temperatura ao vivo numa régua térmica com faixas, máxima registrada e histórico de 5 minutos; integridade (S.M.A.R.T., saúde do Windows e erros) e um odômetro com horas ligado, ciclos, total gravado e lido. SSDs NVMe passam a informar os blocos de reserva, o aviso crítico, os erros de mídia, os desligamentos inesperados, as horas ligado e os ciclos lidos direto do relatório de saúde do próprio disco (o Windows nem sempre repassa), também no relatório.
- Rede redesenhada e numa tela só com a janela maximizada. O botão do teste ganhou destaque num console ao lado do título, com a barra de progresso, que acompanha o trabalho real (pings enviados e segundos medidos de cada fase), as três etapas marcadas e a porcentagem. O diagnóstico virou um instrumento: download e upload em dois arcos espelhados com números grandes, latência no centro, leituras parciais durante o teste e jitter, perda, latência sob carga e DNS logo abaixo, sem textos de apoio. O caminho PC → roteador → internet mostra onde está o atraso, um traço compara os pings em repouso e durante o download, e os dados do adaptador aparecem antes mesmo do teste. O download deixou de ser pulado quando o servidor da Cloudflare recusa conexões logo após um teste anterior: o motor espera e tenta de novo e, se ainda assim falhar, diz o motivo no diagnóstico em vez de mostrar só um travessão. O relatório exportado continua com os mesmos campos.

## 0.2.0 — 2026-10-23

- Instalador `CoreBi-0.2.0-win-x64-setup.exe` (Inno Setup): instala em `Program Files`, cria atalhos e desinstalador e traz o driver PawnIO como componente marcado, instalado pelo winget. A desinstalação pergunta antes de apagar configurações e backups.
- Reformulação completa da interface (direção "Bancada"): novo design system com paleta de dois canais (verde-água para CPU e leitura ao vivo, cobre para GPU), cores de veredito reservadas a estados, Bahnschrift com algarismos tabulares para leituras, medidores segmentados, tema claro e escuro revisados e textos em caixa de frase.
- Shell novo: marca vetorial, navegação com ícones, telemetria permanente na barra lateral, pílula de estado dos sensores, cantos arredondados do Windows 11 e paleta de comandos (Ctrl + K) com busca sem acento.
- As 15 páginas redesenhadas: Visão geral com osciloscópio de dois canais, Benchmark com ladrilhos por teste, Teste de carga com cartões de componente e limites com unidade, Overlay com seletor de posição na prévia do monitor, selos de veredito coloridos em Discos, Energia, Rede e FPS estimado.
- Botões de iniciar e parar trocam cor, glifo e texto juntos; estados de alerta acompanham a troca de tema.
- Toast e overlay de jogo em grafite com números de largura fixa.
- `tools/check_resources.py` valida as chaves de recurso de XAML e C#.

- TraceEvent 4.0.0, System.Management e EventLog 10.0.13; locks completos e licenças de dependências sincronizados.

- Documentação concentrada em docs, com somente README na raiz; licenças centralizadas em licenses e referências de apresentação à base removidas.

- Disco, Energia e Rede com estado/handlers próprios, tarefas aguardáveis e proteção contra atualizações após fechamento.
- Tema, benchmark de disco e exportação separados nos diretórios correspondentes; remoção de encaminhamentos e método sem uso no shell.
- Executor WPF isolado da instância única do produto; cobertura de exclusão entre cargas e vida útil das páginas.
- Resumo de saúde de disco distingue falta de dados de ausência de problemas detectados.
- README exclusivamente técnico; contrato da interface movido para docs e documentação organizada por responsabilidade.
- Exclusões de artefatos ancoradas na raiz para não ocultar fontes em Features/Reports.

## 0.1.1 — 2026-10-23

- Isolamento de handlers/estado das três ferramentas administrativas em suas páginas, com contexto mínimo de vida útil.
- Inicialização com backup durável antes da remoção e restauração por registro canônico protegido; conflitos/falhas preservam a recuperação.
- Download de driver com domínio/redirecionamento validado, publicador exato, Authenticode e proteção contra substituição do arquivo.
- Windows Update com identidades por revisão, liberação de objetos COM e resultado individual/possibilidade de reboot.
- Processos com argumentos separados, saída limitada e cancelamento da árvore criada pelo aplicativo.
- Abertura/fechamento de sensores idempotentes, snapshots imutáveis, isolamento de dispositivo e ETW sob demanda.
- Benchmarks/stress com validação, cancelamento/limpeza e propagação de falhas de workers; HLSL separado em recursos incorporados.
- Configurações atômicas/normalizadas, diagnóstico local limitado, tratamento de falhas de gravação e cache de API validado/limitado.
- Hardware sem referência não produz FPS inventado; rede distingue ICMP bloqueado de ausência de conexão.
- 24 regressões lógicas/WPF e modos separados para hardware, integração e cargas reais; CI Windows, dependências/ações fixadas, varredura de segredos e documentação revisada.

## 0.1.0 — 2026-10-23

Primeira base independente CoreBi: marca autoral, divisão Engine/Desktop, 15 páginas mantidas, remoção do escopo excluído e correções iniciais de dados/concorrência. Créditos MIT e licenças de terceiros preservados.
