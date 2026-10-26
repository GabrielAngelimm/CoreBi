---
version: beta
name: CoreBi
description: 'Bancada: a interface do CoreBi como um instrumento de medição, com dois canais de leitura, mostradores segmentados e grafite.'
colors:
  bg: "#0D141A"
  sidebar: "#0A1015"
  surface: "#121B23"
  raised: "#18232D"
  line: "#1F2B35"
  line-strong: "#2D3D4A"
  foreground: "#E6EEF1"
  muted: "#93A4B0"
  faint: "#61717D"
  accent: "#27C9BC"
  copper: "#D99067"
  steel: "#7F93A2"
  ok: "#5FC385"
  warn: "#E8B550"
  danger: "#EF6B5E"
  light-bg: "#F2F5F6"
  light-foreground: "#122029"
  light-accent: "#087E78"
  light-copper: "#A35F37"
typography:
  text:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
  display:
    fontFamily: "Segoe UI Variable Display, Segoe UI"
  readout:
    fontFamily: "Bahnschrift, Segoe UI"
    fontStretch: "SemiCondensed"
    numerals: "tabular"
  mono:
    fontFamily: "Cascadia Mono, Consolas"
  icons:
    fontFamily: "Segoe Fluent Icons, Segoe MDL2 Assets"
rounded:
  control: "8px"
  inset: "9px"
  card: "12px"
  palette: "14px"
spacing:
  page-gutter: "36px"
  card-gap: "16px"
  card-padding: "22px 20px"
  title-height: "52px"
  sidebar-width: "244px"
components:
  primary-button:
    backgroundColor: "{colors.accent}"
    textColor: "#04211E"
  secondary-button:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.foreground}"
  stop-button:
    backgroundColor: "{colors.danger} 15%"
    textColor: "{colors.danger}"
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.foreground}"
  navigation:
    backgroundColor: "{colors.sidebar}"
    textColor: "{colors.muted}"
  toast:
    backgroundColor: "{colors.surface}"
  overlay:
    backgroundColor: "#D90D141A"
---

# CoreBi Design System: Bancada

## Ideia central

O CoreBi mede hardware. Em vez de parecer um painel administrativo genérico, a interface se comporta como um **instrumento de bancada**: tela escura de grafite, leituras grandes em fonte de mostrador, retícula de osciloscópio e dois canais de sinal com cores fixas. Quem usa um multímetro ou um osciloscópio reconhece a linguagem; quem não usa percebe só que os números são fáceis de ler.

Três regras resumem o sistema:

1. **Cor é canal, não decoração.** Verde-água é o canal A (processador e leitura ao vivo), cobre é o canal B (placa de vídeo). As cores aparecem nos gráficos, nos medidores, nas legendas, nos anéis e na barra lateral sempre com o mesmo significado.
2. **Verde, âmbar e vermelho só para veredito.** Saudável, atenção e crítico (disco, bateria, rede, FPS jogável, corte por temperatura). Nunca para enfeitar.
3. **Grandeza medida é segmentada; tarefa é contínua.** Carga, temperatura e FPS aparecem em medidores segmentados (como um VU). O progresso de um teste usa uma barra contínua fina.

Conteúdo em português brasileiro, em caixa de frase. Nomes de hardware, APIs e unidades mantêm a grafia técnica.

## Cores

A fonte canônica é `src/CoreBi.Desktop/Shell/ThemeManager.cs` (pares escuro/claro) com os nomes usados em `Theme.xaml`. Os valores do cabeçalho deste arquivo são o tema escuro.

| Papel | Chaves | Uso |
| --- | --- | --- |
| Superfícies | `Bg`, `Sidebar`, `Surface`, `Raised`, `Well` | Fundo da janela, barra lateral, painel, controle elevado, palco escuro de imagens renderizadas |
| Interação | `Hover`, `Selected`, `Line`, `LineStrong`, `Scrim` | Realce, seleção, fios de divisão, contorno de controles, fundo da paleta de comandos |
| Texto | `Fg`, `Muted`, `Faint` | Texto principal, secundário, notas e eixos |
| Canal A | `Accent`, `AccentHover`, `AccentSoft`, `AccentFg` | Processador, leitura ao vivo, ação principal, seleção |
| Canal B | `Copper`, `CopperSoft` | Placa de vídeo nos gráficos, medidores, anéis e legendas |
| Neutro técnico | `Steel` | Terceiro canal (disco nos anéis), ícones de categoria |
| Veredito | `Ok`, `Warn`, `Danger` e as variantes `Soft` | Selos, faixas laterais de cartões, avisos, corte automático |

O tema claro ("papel") troca todos os pincéis em tempo de execução. O que é desenhado em código (gráficos, anéis) é refeito na troca de tema; estados de controles definidos em código usam `SetResourceReference`, que acompanha o tema sozinho. Toast e overlay têm paleta fixa de grafite porque aparecem por cima de qualquer coisa, inclusive jogos.

## Tipografia

| Papel | Família | Onde |
| --- | --- | --- |
| Texto | Segoe UI Variable Text | Corpo, rótulos, controles (13 px) |
| Título | Segoe UI Variable Display | Título da página (28), destaques de resultado (21–30) |
| Mostrador | Bahnschrift SemiCondensed, algarismos tabulares | Leituras ao vivo (`ReadoutText` 22, `ReadoutLarge` 36, `ReadoutHero` 52), eixos de gráfico, assinatura CoreBi |
| Código | Cascadia Mono | Somente registro do teste de carga e linhas de comando da Inicialização |

Números em texto comum usam `Typography.NumeralAlignment="Tabular"` (estilo `Num`) para não "dançar" quando o valor muda. Rótulos são curtos e em caixa de frase; não há rótulos em caixa alta. Botões dizem exatamente o que acontece ("Iniciar teste", "Exportar relatório", "Ler novamente").

## Layout

- Janela de 1360 × 860 (mínimo 1120 × 680), sem moldura nativa, cantos arredondados do Windows 11 via DWM. Maximizada, ocupa exatamente o espaço de trabalho do monitor (`WM_GETMINMAXINFO`), sem passar por baixo da barra de tarefas.
- Barra lateral de 244 px: marca, navegação agrupada (Monitoramento, Jogos, Desempenho, Ferramentas) e, no rodapé, a chave de tema ("Modo escuro"), a central de monitoramento (CPU, GPU, RAM e disco com medidor segmentado, oculta na Visão geral, que mostra o mesmo em tamanho grande). O número da versão fica discreto na barra de título, ao lado do botão de minimizar.
- Barra de título de 52 px: atalho da paleta de comandos à esquerda; número da versão e controles da janela à direita. O estado da coleta aparece na própria página de Sensores.
- Página: margem de 36 px, cabeçalho com título e subtítulo (o subtítulo carrega o estado vivo da página quando existe) e a ação principal à direita. Painéis com 16 px entre si.
- Visão geral cabe numa tela com a janela maximizada: o gráfico e a atividade recente ocupam a sobra de altura; em janelas menores a página rola e os componentes passam a duas colunas. Processador e placa de vídeo têm cartão de uso (leitura, medidor e grandezas); RAM, VRAM e disco do sistema dividem um mostrador de anéis concêntricos com quanto está em uso e o que sobra, sem campos que não se aplicam a eles. As setas e as linhas dos anéis levam à seção de cada item, e o gráfico mostra, sob o mouse, a hora e o valor exato de cada canal; enquanto o mouse está sobre ele o gráfico pausa e retoma ao sair.
- Overlay cabe numa tela com a janela maximizada: interruptor e atalho no cabeçalho; o monitor (desenho fixo escalado por `Viewbox` dentro de `FillBox`) ocupa a sobra de altura e tem um canto tracejado clicável em cada quarto da tela; o canto escolhido mostra a prévia, que é a mesma `OverlayPanel` do jogo com os itens marcados. "O que mostrar" fica ao lado, uma linha por componente distribuída por `SpreadPanel`; a detecção de FPS é uma faixa no rodapé, que só mostra texto quando há o que dizer (overlay desligado não tem aviso). Os controles de posição, opacidade e tamanho formam uma faixa de 48 px com três blocos separados por fios; nada nela muda de lugar ao trocar de opção. Não há selo sobre a prévia. Os subtítulos dos dois cartões explicam o que fazer, e nomes de outras seções entre aspas (“O que mostrar”). A VRAM só tem uso em % e GB usados (a temperatura não tinha sensor confiável). Em janelas estreitas os dois painéis ficam um embaixo do outro e a página rola.
- Discos cabe numa tela com a janela maximizada: rack de 300 px à esquerda (uma baia compacta por disco, selecionável, só com nome, características e LED de veredito) e o disco escolhido à direita, com a placa de identificação (modelo, veredito, especificação e o espaço usado, total e livre), três instrumentos lado a lado (vida útil em arco de gomos, temperatura em régua térmica com histórico, integridade em linhas com LED) e o odômetro no rodapé. Os instrumentos são desenhados em `Features/Monitoring/Disks/DiskInstruments.cs` com pincéis por referência de recurso, então acompanham o tema sem redesenho manual. Em janelas estreitas a integridade desce para o lado do odômetro e a página rola.
- Rede cabe numa tela com a janela maximizada, com o título alinhado ao topo como nas outras páginas: o console do teste (botão de 46 px, etapa atual, barra contínua com as estações Latência, Download e Upload nas frações do motor e porcentagem) fica ao lado do título e desce para baixo dele em janelas estreitas; a barra só avança quando o motor informa trabalho feito e cada passo desliza 420 ms com desaceleração. O diagnóstico é um instrumento e ocupa a sobra de altura: download (verde-água) e upload (cobre) em dois arcos segmentados espelhados, em escala logarítmica de 1 Mbps a 1 Gbps e sem marcas numéricas, com a latência no centro e as leituras parciais acendendo durante o teste; jitter, perda, latência sob carga e DNS numa régua de quatro células e os achados no rodapé do painel. Sem textos de apoio: só estado, números e achados (o que o teste envia e baixa fica na dica do botão). Com menos peso, o caminho PC → roteador → internet (trechos na cor do veredito de cada um), o traço dos pings a 1.1.1.1 em repouso e durante o download (o bufferbloat aparece como o salto entre os blocos) e os dados do adaptador, lidos ao abrir a página.
- Energia cabe numa tela com a janela maximizada, em quatro painéis: em cima, a alimentação (fonte ou bateria desenhada com gomos e a saúde em capacidade de fábrica × atual, planos instalados, em que escolher um mostra a prévia do que muda e só Aplicar troca de fato, e só o essencial da regulagem: faixa e preferência do processador, tela e suspensão (o turbo só aparece se alguém o mudou; a prévia da troca tem título curto, "Prévia da troca", e o aviso de máximo abaixo de 100 % é a cor âmbar do valor, para o cartão nunca mudar de altura ao escolher um plano)) e o consumo ao vivo de processador e placa de vídeo (placa com a leitura grande, a divisão entre processador e placa de vídeo em um medidor segmentado, o histórico de 5 min empilhado e consumo médio, maior consumo e energia gasta na sessão numa régua, com a duração da sessão no cabeçalho), na mesma altura; embaixo, as tensões (trilhos ATX numa régua de ±10 % com a tolerância de ±5 % acesa; componentes numa escala fixa) e o uso do computador nos últimos 7 dias, reconstruído do registro de eventos (faixa de totais com vãos iguais e fios entre eles, uma linha por dia com o total, hoje no topo), que se estica até o fim da página. Detalhes ficam nas dicas, não em legendas. Em janelas estreitas os painéis descem um por linha. Os instrumentos ficam em `Features/Monitoring/Power/EnergyInstruments.cs`.
- Sistema é uma ficha técnica num painel só, sem ilustrações: hierarquia, grupos e tipografia. O topo é uma faixa um tom abaixo do painel com seis botões iguais (3 × 2 em janelas estreitas), cada um com fundo e contorno próprios, ícone em ladrilho de 38 px, o nome da categoria e uma seta discreta à direita, para ler como botão; o mouse clareia o fundo e o escolhido ganha contorno e ícone verde-água e perde a seta. Embaixo, a placa com sobretítulo (fabricante), o nome da peça e as quatro leituras que a resumem, separadas por fios com o mesmo espaço dos dois lados (sem largura mínima). Depois, na largura toda, os grupos de linhas chave/valor (título em 14 seminegrito e uma frase que explica o assunto) que se distribuem em 3, 2 ou 1 coluna (`ColumnsPanel`) e rolam sozinhos. Tudo é texto: sem LEDs, selos ou etiquetas; a explicação, quando há, vem embaixo do valor em itálico de 12 px, um tom abaixo e com 8 px de ar, para a hierarquia chave → valor → explicação ficar clara (estilo `Explain`, o mesmo na frase dos grupos). A placa é o resumo; os grupos são a ficha completa, então um dado de identificação (nome completo, modelo, versão) pode estar na placa e de novo no grupo dele. O que não se repete é um dado entre dois grupos (como o espaço livre do disco). A página mostra o hardware, não o uso ao vivo. Campo que o Windows não preenche não aparece (nada de linha com travessão), e a página não cita outros programas. Clicar numa linha copia o valor. Armazenamento mostra primeiro um disco por linha com as unidades em proporção ao tamanho, em gomos, e depois a ficha de cada disco nos grupos. O atalho para as leituras ao vivo (Sensores, Placa de vídeo, Discos) fica no rodapé do card, à esquerda: botão discreto com contorno, ícone de pulso e seta. As fichas são montadas em `Features/Monitoring/Inventory/SystemSheets.cs`. Os tons seguem os cabeçalhos de Sensores: a faixa dos botões em `Raised`, a placa em `Selected` (fio `LineStrong`) e os grupos no fundo do painel; o botão escolhido usa o mesmo `Selected` da placa, para ler como a aba dela. O rodapé do processador, da memória RAM e da placa-mãe abre Sensores já filtrado no componente; o da placa de vídeo abre Placa de vídeo e o do armazenamento, Discos. No canto superior direito, "Exportar relatório" é o único botão preenchido da página (fundo verde-água, ladrilho de ícone translúcido, 13,5 seminegrito, cantos de 10 px): o mouse clareia, o clique afunda 1 px e, enquanto gera, ele diz "Gerando relatório…" e fica esmaecido.
- Sensores é um monitor de hardware que rola: índice fixo à esquerda (300 px) que funciona como filtro real. "Visão geral" mostra todos os componentes; escolher um mostra só ele (o filtro de tipo e a busca valem dentro da escolha, e os tipos e contagens são os do componente). O índice vai até a base da página. Cada item tem um bloco de ícone de 34 px, centralizado (cinza, e verde-água suave no selecionado), nome inteiro em até duas linhas e o tipo abaixo, sem leituras; o selecionado ganha fundo suave e o ícone aceso em verde-água, sem filete. Ordem: processador, placa de vídeo, armazenamento, placa-mãe, cada módulo de RAM, rede, memória física e memória virtual (a biblioteca as mede num bloco só; viram dois componentes, com dica). Os filtros por tipo ficam numa faixa em cima, na largura toda, em pílulas com a quantidade ("Todos os tipos" separado por um fio); abaixo, o índice (300 px) e o cartão de sensores ocupam a mesma altura, começando e terminando na mesma linha. O cartão é um só, com um bloco por componente: o cabeçalho tem duas faixas, cada uma num tom, e os sensores ficam no fundo do cartão (três níveis): a principal (80 px, fundo `Selected`, fio `LineStrong`) com o nome em 22 seminegrito (sem ícone nem cor por canal), embaixo o tipo (13, tom secundário) e a quantidade de sensores (13, cor de texto) e a seta que recolhe (some quando há um componente só); e a das colunas (32 px, fundo `Raised`) com Atual, Mín., Média e Máx. alinhados à direita com os números de cada coluna de grupos. No cabeçalho fixo, o fundo das duas faixas cobre a largura toda e o conteúdo fica na largura útil; a rolagem tem modelo próprio em que a barra começa abaixo do cabeçalho (112 px), então nada dela fica escondido. Acima do cartão de sensores, alinhados com ele, os filtros por tipo ficam num grupo de fundo `Raised` e cantos redondos, com o rótulo "Filtrar por tipo de sensor" (ícone de funil, 12,5 seminegrito, tom secundário) antes das pílulas; o índice e o cartão continuam começando na mesma linha. Os atalhos de Sistema abrem Sensores já filtrado: "PageSensors:cpu", ":ram" (primeiro módulo) e ":board" (`SensorBoard.Find`, `SensorsView.ShowComponent`). Os grupos ficam em duas colunas de propósito: numa coluna só, a 1920 px, o nome ficaria a mais de 1000 px dos números e a página do processador dobraria de altura. O cabeçalho do componente à vista fica fixo no topo da rolagem (`UpdateSticky`): mostra o último cujo cabeçalho já chegou ao topo e, quando o do próximo se aproxima, sobe junto com ele sem sobrepor; a troca é no instante em que os dois são idênticos e estão na mesma posição, então não há salto. Leitura nenhuma no cabeçalho, para cada valor aparecer uma vez só. O corpo distribui os grupos de tipo em 2 colunas (`ColumnsPanel`, 1 em janelas estreitas), cada um com título em 16 seminegrito (fonte de títulos), filete verde-água de 3 px e quantidade, e os sensores recuados 12 px embaixo. A linha tem 32 px, fio em cima, nome claro (o técnico fica na dica), a leitura em mostrador 16 com a unidade pequena (âmbar ou vermelho quando a temperatura passa do limite) e as estatísticas sem unidade, com casas decimais fixas por grandeza para alinhar pela vírgula. Mínimo, média e máximo são contados pelo CoreBi desde a abertura ou desde "Zerar estatísticas". Só placas de rede físicas e conectadas são lidas (`PhysicalNetwork`). O rodapé com a nota das estatísticas fica fixo, numa linha, sob o índice e os cartões. Os cartões só existem com a página à vista. A montagem fica em `Features/Monitoring/Sensors/SensorBoard.cs` e os nomes em `SensorNaming` (motor).
- O relatório exportado (HTML para ler e imprimir, PDF ou JSON) repete as seis fichas do Sistema na mesma ordem e com os mesmos dados (`Features/Reports/SystemReport.cs` → `ReportBuilder`): capa com o nome do computador e uma linha de resumo, sumário numerado, cada ficha com placa, leituras e grupos em duas colunas, e depois a parte de diagnóstico (discos, energia, rede, sensores, benchmarks). O logo (versão escura no papel claro, clara no tema escuro) abre a capa e assina o rodapé; o resumo de cada peça fica num painel tingido, com as leituras numa fila separada por fios, e os títulos de seção têm um traço verde-água sobre o fio. Na tela, o HTML abre no tema do aplicativo e uma barra flutuante alterna claro e escuro e imprime; a impressão é sempre a folha A4 clara. O PDF sai do Microsoft Edge em modo headless (`Features/Reports/PdfPrinter.cs`), com perfil temporário próprio e limite de 60 s. Na impressão, cada grupo fica inteiro numa página, títulos não ficam sozinhos no fim da folha, o diagnóstico começa em página nova e cada folha (menos a capa) traz no topo "CoreBi · Relatório do sistema" e o computador, e no rodapé a data e "Página X de Y". Números de série e o nome do usuário ficam fora.
- Telas de teste (Benchmark, Teste de carga) usam a mesma composição: palco ou leitura principal à esquerda e uma coluna de 320–330 px com configuração, resultado e histórico.

## Profundidade e forma

Sem sombras nos painéis: a hierarquia vem do tom (fundo → painel → elevado) e de fios de 1 px. A única sombra do app é a da paleta de comandos, que flutua acima de tudo (e a do toast, fora da janela).

Raios: 8 em controles, 9 em poços internos, 12 em painéis, 14 na paleta. Selos e teclas usam 5. A marca segue a malha 256 × 256 de `brand/geometry.json`, desenhada em vetor na barra lateral.

## Componentes

Todos em `Shell/Theme.xaml`; templates de dados compartilhados em `Shell/FeatureTemplates.xaml`. As propriedades anexadas de `Shell/Ui.cs` evitam estilos duplicados:

- `Ui.Icon`: glifo Segoe Fluent em botões, itens de navegação, ladrilhos de teste, cartões de componente e campos.
- `Ui.Caption`: legenda de ladrilhos e cartões de seleção.
- `Ui.Placeholder` e `Ui.Suffix`: texto de espera e unidade (°C, min) dentro do campo. O texto de espera some quando o campo recebe o foco, para o cursor não piscar sobre a primeira letra dele; a busca de Sensores também solta o foco com um clique em qualquer lugar fora dela.
- `Ui.SetRunning(botão, rodando, …)`: o botão de iniciar vira botão de parar (cor, glifo e texto juntos).

| Componente | Estilo | Quando usar |
| --- | --- | --- |
| Ação principal | `PrimaryButton` | Uma por tela |
| Ação secundária | `SecondaryButton`, `GhostButton` | Ler novamente, abrir página, predefinições |
| Interromper | `StopButton` | Teste em andamento |
| Remover em lista | `DangerGhostButton` | Inicialização |
| Opções exclusivas | `SegmentGroup` + `Segment` | Resolução, duração, preset (poucas opções fixas); o item marcado reserva a largura do negrito, então marcar não desloca os vizinhos |
| Filtro gerado | `Chip` | Categorias de sensores, GPUs, unidades de disco |
| Escolha de teste | `TestTile` | Benchmark |
| Componente a carregar | `ToggleCard` | Teste de carga |
| Ligar e desligar | `Switch` | Overlay, programas da inicialização, Driver Studio |
| Tema | `ThemeToggle` | Linha da barra lateral; ligada = modo escuro |
| Seleção múltipla | `CheckBox` padrão | Apps |
| Opção em pílula | `OptionChip` | Itens do overlay (visto quando marcado, "+" quando não) |
| Veredito | `CardTemplate` | Discos, Energia, Rede (faixa lateral + selo colorido) |
| Medidor | `Meter`, máscara `SegmentMask` | Gargalo, histórico de benchmarks |

Paleta de comandos (Ctrl + K): busca sem acento entre as páginas (lidas da própria navegação) e ações como alternar tema, exportar relatório, mostrar o overlay e testar a rede.

## Movimento

Apenas em resposta a uma ação: a página entra com 170 ms de opacidade e 230 ms de deslocamento curto, respeitando a opção "Mostrar animações no Windows" (`SystemParameters.ClientAreaAnimation`). Item de navegação selecionado ganha fundo elevado e ícone verde-água em 160 ms, sem barra lateral; a chave de tema desliza em 240 ms e troca sol e lua com um giro curto. Não há animação contínua: o app fica aberto durante jogos e benchmarks, e qualquer custo de CPU/GPU contaminaria a medição.

## Dados e honestidade

Gráficos e indicadores refletem só dados reais e ficam vazios ou com travessão quando não há amostra. Falta de sensor nunca aparece como zero. FPS estimado, resposta da API e FPS medido por ETW são identificados por texto. Sucesso parcial não usa mensagem de sucesso total.

## Verificação

- `tools/check_resources.py` cruza cada `{StaticResource}`/`{DynamicResource}` e cada chave usada em C# com as definidas no tema, respeitando a ordem de carga (o compilador de XAML não detecta chave inexistente).
- Compilação WPF, construção e navegação das 15 páginas, ida e volta de tema e regras de indisponibilidade estão no executor de testes (`tests/CoreBi.Tests`).
- Ainda pendente de revisão manual em Windows: navegação completa por teclado, leitor de tela, alto contraste e DPI em vários monitores.
