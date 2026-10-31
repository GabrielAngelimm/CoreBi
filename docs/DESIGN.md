---
version: 3.1
name: CoreBi
description: 'Bancada: a interface do CoreBi como um instrumento de medição. Painéis de grafite (ou papel), a marca acendendo o que se clica e cada componente de hardware com a sua cor e o seu desenho, tudo em cor chapada.'
colors:
  # Tema escuro "Grafite" (o claro, "Papel", está na tabela de ThemeManager.cs)
  bg: "#0E1214"
  sidebar: "#0A0D0F"
  surface: "#15191C"
  raised: "#1C2022"
  selected: "#282D31"
  line: "#24282B"
  line-strong: "#353A3D"
  foreground: "#E6EAEC"
  muted: "#A3AAAE"
  faint: "#737A7E"
  signal: "#27C9BC"
  signal-hover: "#4DD9CD"
  signal-deep: "#12AA9D"
  signal-foreground: "#03201D"
  focus: "#5FDCCF"
  nav-selected: "#10373A"
  ink: "#E3E7E9"
  cpu: "#1F8A48"
  gpu: "#E8894A"
  memory: "#B296FF"
  memory-virtual: "#7A5AC8"
  storage: "#4A82CC"
  board: "#C8A370"
  net: "#E3D45B"
  steel: "#8696A2"
  ok: "#6FC889"
  warn: "#EDB755"
  danger: "#EE6E63"
  light-bg: "#F1F4F6"
  light-foreground: "#182228"
  light-signal: "#0A857C"
typography:
  display:
    fontFamily: "Bahnschrift"
    fontWeight: "SemiBold"
  label:
    fontFamily: "Bahnschrift"
  text:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
  small:
    fontFamily: "Segoe UI Variable Small, Segoe UI"
  readout:
    fontFamily: "Bahnschrift"
    fontStretch: "SemiCondensed"
    numerals: "tabular"
  mono:
    fontFamily: "Cascadia Mono, Consolas"
  icons:
    fontFamily: "Segoe Fluent Icons, Segoe MDL2 Assets (só para ações e estados; as peças são desenhadas em HwIcon)"
rounded:
  panel: "10px"
  inset: "8px"
  control: "7px"
  small: "4px"
  pill: "14px"
spacing:
  page-gutter: "36px"
  header-gap: "18px"
  panel-gap: "16px"
  panel-padding: "24px 20px"
  title-height: "52px"
  sidebar-width: "244px"
components:
  primary-button:
    backgroundColor: "{colors.signal}"
    textColor: "{colors.signal-foreground}"
  secondary-button:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.foreground}"
  stop-button:
    backgroundColor: "{colors.danger} 15%"
    textColor: "{colors.danger}"
  selected:
    backgroundColor: "{colors.nav-selected} (navegação), {colors.selected} (listas e segmentos)"
    textColor: "{colors.foreground}"
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.foreground}"
  navigation:
    backgroundColor: "{colors.sidebar}"
    textColor: "{colors.muted}"
  overlay:
    backgroundColor: "#0E1214"
---

# CoreBi Design System: Bancada

## Ideia central

O CoreBi mede hardware. A interface se comporta como um **instrumento de bancada**: painéis de grafite, leituras grandes em letra de mostrador, retícula de osciloscópio e medidores segmentados. A versão 2 deixou o painel neutro demais (preto, branco e cinza); a 3 devolveu a cor, mas com degradês e brilhos que poluíram; a 3.1 mantém a cor e tira o excesso: **tudo é cor chapada**, a cor vem do que se clica (o sinal da marca) e de cada peça do computador (a cor e o desenho dela), e o resto é grafite.

Quatro regras resumem o sistema:

1. **O que se clica acende no sinal.** A ação principal é o verde-água do "i" do logo, chapado (uma por tela). A página aberta na navegação ganha um fundo fixo, o mesmo verde-água bem escuro (`NavSel`), que desliza de um item para o outro, com o ícone aceso. Interruptores e caixas ligados, o foco de teclado, a pílula de filtro marcada e o glifo dos botões fantasmas também são sinal. Segmentos, abas de categoria e listas escolhem pelo fundo elevado (a tecla que sobe), sem cor.
2. **Cada componente de hardware tem uma cor e um desenho, e só um, em todas as telas.** Processador verde escuro, placa de vídeo laranja, memória RAM violeta, memória física e virtual do Windows violeta bem mais escuro, armazenamento azul, placa-mãe bege, rede amarelo. A cor aparece nos gráficos, medidores, anéis, legendas, na barra lateral, no ícone da peça e no fundo claro da ficha dela (a placa de Sistema, o cabeçalho de Sensores). Download e upload usam a cor da rede e o sinal. Em OverlayBi os glifos dos grupos são todos da mesma cor, a do sinal: são as linhas do overlay, não peças.
3. **Saudável é silencioso.** Verde, âmbar e vermelho são veredito. "Tudo bem" é um LED verde pequeno ao lado de um texto em tinta; atenção e crítico ganham fundo e texto na cor, porque precisam ser vistos. Faixas "normais" de escala (ideal, tolerância) ficam neutras; só as zonas de atenção e crítica são pintadas.
4. **Ícone sem fundo.** O ícone de uma peça é só o desenho, grande (26 a 40 px), na cor dela: sem ladrilho, círculo nem superfície colorida ao redor. É ele o único sinal visual daquela peça, então é desenhado com cuidado (ver Ícones). Nada de degradê, brilho, neon ou sombra colorida em lugar nenhum.

Conteúdo em português brasileiro, em caixa de frase. Nomes de hardware, APIs e unidades mantêm a grafia técnica.

## Cores

A fonte única é a tabela `Palette` de `src/CoreBi.Desktop/Shell/ThemeManager.cs` (pares escuro/claro), que troca os pincéis na hora da troca de tema. `Theme.xaml` repete os valores do tema escuro num bloco gerado (`python tools/sync_theme.py`; o `tools/check_resources.py` avisa quando está desatualizado), só para o designer do XAML. Nenhuma tela define cor própria: tudo é `DynamicResource` (ou `SetResourceReference` e `ThemeManager.Brush`, `Tint`, `WithAlpha` no código) para uma destas chaves, então trocar a paleta é trocar valores numa tabela.

| Papel | Chaves | Uso |
| --- | --- | --- |
| Superfícies | `Bg`, `Sidebar`, `Surface`, `Raised`, `Hover`, `Selected`, `Well`, `Scrim` | Bancada (fundo), barra lateral, painel, controle elevado, realce, item escolhido, palco de imagens, fundo da paleta de comandos |
| Fios | `Line`, `LineStrong` | Divisões de 1 px e contorno de controles |
| Texto | `Fg`, `Muted`, `Faint` | Texto principal, secundário, notas e eixos |
| Sinal | `Signal`, `SignalHover`, `SignalDeep`, `SignalFg`, `SignalSoft`, `Focus`, `NavSel` | Ação principal, página aberta na navegação, interruptor e caixa ligados, filtro marcado, foco, progresso de tarefa, LED "ao vivo", glifo dos botões fantasmas, sessão atual em Energia, FPS do overlay |
| Tinta | `Ink`, `InkFg` | Leituras neutras (carga da bateria) |
| Componentes | `Cpu`, `Gpu`, `Memory`, `Virtual`, `Storage`, `Board`, `Net` (cada um com a variante `Soft`), `NetDown`, `NetUp`, `Steel`, `SteelSoft` | O que cada peça mede e é; `Steel` é o neutro técnico (sistema operacional, tempo ligado, pings em repouso, faixas de tolerância); `Soft` é o fundo claro da ficha da peça (`ChannelWash`) |
| Veredito | `Ok`, `Warn`, `Danger` e as variantes `Soft` | LEDs, selos, valores fora do limite, zonas de atenção e crítica |
| Fixa | `Hud*` em `Shell/HudPalette.xaml` | Overlay e aviso (toast), sempre grafite, por cima de jogos e da área de trabalho |

O grafite (e o papel) é quase neutro, levemente frio (OKLCH com croma ≈ 0,007 e matiz 232°): conversa com o grafite azulado da marca (`brand/geometry.json`) sem virar o "azul-marinho de painel" genérico.

### Componentes de hardware

| Componente | Chave | Escuro | Claro | Onde aparece |
| --- | --- | --- | --- | --- |
| Processador | `Cpu` | `#1F8A48` | `#17703A` | Gráfico, cartão e atividade da Visão geral, barra lateral, consumo em Energia, faixa de desempenho do plano, aba e placa de Sistema, índice e filete de Sensores, cartão de Benchmark e Teste de carga |
| Placa de vídeo | `Gpu` | `#E8894A` | `#C55F1D` | Gráfico, cartão, anel da VRAM e atividade na Visão geral, barra lateral, consumo em Energia, aba de Sistema, índice de Sensores, página Placa de vídeo, Drivers |
| Memória RAM | `Memory` | `#B296FF` | `#8A66D9` | Anel da RAM, barra lateral, aba de Sistema, módulos em Sensores, Benchmark de memória, Teste de carga |
| Memória física e virtual | `Virtual` | `#7A5AC8` | `#5B3FA6` | Memória física e memória virtual do Windows em Sensores (a virtual com o contorno tracejado) |
| Armazenamento | `Storage` | `#4A82CC` | `#1F5FA8` | Anel do disco, barra lateral, Discos inteira (rack, vida útil, régua térmica, histórico, espaço), aba de Sistema, Benchmark de disco |
| Placa-mãe | `Board` | `#C8A370` | `#9A7430` | Aba e placa de Sistema, índice de Sensores, Drivers |
| Rede | `Net` (`NetDown` é a mesma cor; `NetUp` é o sinal) | `#E3D45B` | `#9A8700` | Ícones e fundo do caminho em Rede, o download no mostrador e nos pings sob carga, Ethernet em Sensores |

O verde escuro do processador é diferente do verde-água do sinal de propósito (matiz 150 contra 180 e bem mais escuro), para o processador nunca ser confundido com a marca. O amarelo da rede é limão (mais verde que o âmbar de aviso) e o aviso só aparece como selo e texto, então os dois não se misturam.

Validação de daltonismo (validador de paletas de dados, todos os pares): as cores que dividem um mesmo gráfico (processador, placa de vídeo, memória e armazenamento) têm ΔE ≥ 16 com visão normal; verde escuro × laranja fica em ΔE ≈ 7,7 para protanopia (faixa que só vale com codificação secundária), por isso o processador e a placa de vídeo sempre aparecem com o nome ao lado (legenda e marca de canal). As demais cores (placa-mãe, memória virtual, rede) nunca dividem um gráfico: identificam peça por ícone e nome.

### O que não fazer

- Degradê, brilho, neon, sombra colorida ou texto em degradê, em qualquer lugar.
- Ladrilho, círculo ou fundo colorido atrás de um ícone.
- Pintar botão, aba, segmento ou ícone de ação com a cor de um componente: cor de componente é dado.
- Usar verde para dizer "está tudo bem" em grande (selo cheio, texto verde de 24 px, bolha verde atrás do ícone). Saudável é LED pequeno e texto em tinta.
- Escrever um hex numa tela. Cor nova é uma linha nova na tabela do `ThemeManager` (ou do `HudPalette.xaml`, se for do overlay).

## Tipografia

Duas vozes: a do instrumento e a do conteúdo.

| Papel | Família | Onde |
| --- | --- | --- |
| Estrutura | Bahnschrift (DIN 1451, a letra de placas e mostradores e da assinatura CoreBi) | Título da página (`PageTitle`, 32 seminegrito), título de painel (`SectionTitle`, 17), título de grupo (`GroupTitle`, 15,5), nomes em destaque (placa do Sistema 30, componente em Sensores 23, modelo do disco 23), navegação (14) e os cabeçalhos dela (12), botões (13,5 seminegrito), rótulos de leitura (`StatLabel` e `Label`, 12,5), segmentos e filtros (13), selos, teclas de atalho e cabeçalhos de coluna (`LabelFont`) |
| Mostrador | Bahnschrift SemiCondensed, algarismos tabulares | Leituras (`ReadoutText` 22, `ReadoutLarge` 38, `ReadoutHero` 54), eixos de gráfico, overlay |
| Conteúdo | Segoe UI Variable Text | Nomes, valores em texto, descrições e campos (13; `Body` com 20 de entrelinha) |
| Texto pequeno | Segoe UI Variable Small (o tamanho óptico feito para letra miúda) | Legendas (`Caption` 12/17), notas (`Footnote` 11,5/16), subtextos (`StatSub` 11,5) |
| Código | Cascadia Mono | Registro do teste de carga e linhas de comando da Inicialização |

Hierarquia de uma tela: título da página (DIN 32) → título de painel (DIN 17) → rótulo (DIN 12,5, `Muted`) → valor (mostrador ou Segoe 13 em `Fg`) → nota (Segoe Small 11,5, `Faint`). A estrutura fala a língua do instrumento e o conteúdo fica na letra de leitura do Windows, então título, rótulo e botão nunca se confundem com o dado. Números em texto comum usam `Typography.NumeralAlignment="Tabular"` (estilo `Num`) para não "dançar" quando o valor muda. Rótulos são curtos e em caixa de frase; não há rótulos em caixa alta. Botões dizem exatamente o que acontece ("Iniciar teste", "Exportar relatório", "Ler novamente").

**Centralização vertical.** A Bahnschrift assenta cerca de 1,5 px acima do centro da caixa de linha do WPF, então os controles com texto DIN compensam no preenchimento (filtros, opções em pílula, segmentos, teclas de atalho, o item de menu e a aba de Sistema), e os campos de texto (Segoe) sobem 1,5 px. O desvio foi medido nas capturas (`--shots`) em todos os controles de uma lista e fica em ±1 px (centro do corpo da letra contra o centro da caixa).

## Ícones

Duas fontes de desenho, uma só aparência (traço de 1,5 numa malha de 24, pontas redondas):

- **Peças do computador**, desenhadas à mão em `Shell/HwIcon.cs` (`Kind`): `cpu` (chip com pinos nos quatro lados), `gpu` (placa com duas ventoinhas e o encaixe PCIe), `ram` e `ramv` (pente de memória; o virtual é tracejado), `vram` (chip com pinos dos lados), `storage` (SSD), `board` (placa-mãe com soquete, slots e PCIe), `net` (rede em árvore, para Ethernet), `router`, `desktop` (computador, o sistema operacional e Este PC) e `fan` (ventoinha). Não dependem da versão do Windows (a fonte de ícones do sistema não tem placa-mãe nem placa de vídeo).
- **Ações e estados**: os glifos da fonte de ícones do Windows (Segoe Fluent Icons), no mesmo traço, via o mesmo `HwIcon` (um caractere no `Kind`) ou direto num botão (`Ui.Icon`).

Tamanhos (como não há fundo, o ícone é grande): barra lateral 19, linhas de lista e cartões pequenos 26 a 30, abas de Sistema 32, cartões de componente 36, páginas dedicadas e cabeçalhos 38 a 44, cartões de teste 30. A cor vem do contexto: `ChannelIcon` com `Ui.Channel` para uma peça; o item de menu e o ícone de uma ação herdam o texto.

## Forma e espaçamento

Sem sombras e sem degradês: a profundidade vem do tom (bancada → painel → elevado → escolhido) e de fios de 1 px. A única sombra do app é a da paleta de comandos, que flutua acima de tudo (e a do aviso, fora da janela).

Raios por hierarquia, como recursos (`RadiusPanel` 10, `RadiusInset` 8, `RadiusControl` 7, `RadiusSmall` 4): painéis 10, poços, teclas de categoria e cartões internos 8, controles 7, selos e teclas de atalho 4; filtros e opções em pílula são inteiramente redondos (14). Recortes feitos em código usam `Ui.PanelRadius`.

Espaçamento numa grade de 4: margem da página 36, título → primeiro painel 18 (`PageHeader`, igual em todas as telas), 16 entre painéis, 24 × 20 dentro deles (`PanelPadding`). A marca segue a malha 256 × 256 de `brand/geometry.json`, desenhada em vetor na barra lateral.

## Layout

- Janela de 1360 × 860 (mínimo 1120 × 680), sem moldura nativa, cantos arredondados do Windows 11 via DWM. Maximizada, ocupa exatamente o espaço de trabalho do monitor (`WM_GETMINMAXINFO`), sem passar por baixo da barra de tarefas.
- Barra lateral de 244 px: marca, navegação agrupada (a pílula do item escolhido desliza de um para o outro) (Monitoramento, Jogos, Desempenho, Ferramentas) e, no rodapé, a central de monitoramento (CPU, GPU, RAM e disco, cada um com medidor segmentado na cor do seu componente, que desliza até a leitura nova; oculta na Visão geral, que mostra o mesmo em tamanho grande) e, por último, a chave de tema ("Modo escuro"), que fica sempre no mesmo lugar. O número da versão fica discreto na barra de título, ao lado do botão de minimizar.
- Barra de título de 52 px: atalho da paleta de comandos à esquerda; número da versão e controles da janela à direita. O estado da coleta aparece na própria página de Sensores.
- Página: margem de 36 px, cabeçalho com título e subtítulo (o subtítulo carrega o estado vivo da página quando existe) e a ação principal à direita. Painéis com 16 px entre si.
- Visão geral cabe numa tela com a janela maximizada: o gráfico e a atividade recente ocupam a sobra de altura; em janelas menores a página rola e os componentes passam a duas colunas. Processador e placa de vídeo têm cartão de uso (ícone do componente na cor dele, nome, leitura, medidor segmentado e grandezas); RAM, VRAM e disco do sistema dividem um mostrador de anéis concêntricos (Memory, Gpu e Storage, cada arco na cor do componente; os anéis enchem do zero ao abrir a página) com quanto está em uso e o que sobra, sem campos que não se aplicam a eles. As setas e as linhas dos anéis levam à seção de cada item, o gráfico tem uma área chapada e translúcida sob cada canal, o ponto atual pulsa a cada amostra e mostra, sob o mouse, a hora e o valor exato de cada canal; enquanto o mouse está sobre ele o gráfico pausa e retoma ao sair. Na saúde do PC cada item tem só o glifo na cor do estado (visto verde ou alerta âmbar), sem bolha atrás; a atividade recente mostra cada teste com o ícone do componente testado.
- Overlay cabe numa tela com a janela maximizada: interruptor e atalho no cabeçalho; o monitor (desenho fixo escalado por `Viewbox` dentro de `FillBox`) ocupa a sobra de altura e tem um canto tracejado clicável em cada quarto da tela (contorno claro, que acende no sinal sob o mouse); o canto escolhido mostra a prévia, que é a mesma `OverlayPanel` do jogo com os itens marcados, e o LED do monitor acende no sinal com o overlay ligado. "O que mostrar" fica ao lado, uma linha por componente distribuída por `SpreadPanel`, com os glifos todos no sinal quando a linha aparece no overlay (neutros quando nada dela está marcado); a detecção de FPS é uma faixa no rodapé, com um LED (sinal medindo um jogo, cinza aguardando) que só mostra texto quando há o que dizer (overlay desligado não tem aviso). Os controles de posição, opacidade (trilho aceso no sinal) e tamanho formam uma faixa de 48 px com três blocos separados por fios; nada nela muda de lugar ao trocar de opção. Não há selo sobre a prévia. Os subtítulos dos dois cartões explicam o que fazer, e nomes de outras seções entre aspas (“O que mostrar”). A VRAM só tem uso em % e GB usados (a temperatura não tinha sensor confiável). Em janelas estreitas os dois painéis ficam um embaixo do outro e a página rola.
- Discos cabe numa tela com a janela maximizada: rack de 300 px à esquerda (uma baia compacta por disco, selecionável, com o ícone do disco no canal Storage, nome, características e LED de veredito) e o disco escolhido à direita, com a placa de identificação (modelo, selo de veredito com LED, especificação e o espaço usado, total e livre), três instrumentos lado a lado (vida útil em arco de gomos, temperatura em régua térmica com histórico, integridade em linhas com LED) e o odômetro no rodapé. Os instrumentos são desenhados no canal Storage (o arco, a régua e o espaço acendem na cor do canal e o histórico tem área chapada e ponto que pulsa a cada leitura) em `Features/Monitoring/Disks/DiskInstruments.cs` com pincéis por referência de recurso, então acompanham o tema sem redesenho manual; a faixa ideal da régua é neutra e só atenção e crítico são pintados. "Na faixa ideal" e as linhas de integridade saudáveis ficam em tinta com o LED verde; o que pede atenção vira selo âmbar ou vermelho. Em janelas estreitas a integridade desce para o lado do odômetro e a página rola.
- Rede cabe numa tela com a janela maximizada, com o título alinhado ao topo como nas outras páginas: o console do teste (botão de 46 px aceso no sinal, etapa atual, barra contínua no sinal com as estações Latência, Download e Upload nas frações do motor e porcentagem) fica ao lado do título e desce para baixo dele em janelas estreitas; a barra só avança quando o motor informa trabalho feito e cada passo desliza 420 ms com desaceleração. O diagnóstico tem duas metades. À esquerda, o instrumento, que cresce até 350 px de altura e fica rente à margem: download (`NetDown`, na cor da rede) e upload (`NetUp`, o sinal) em números de 96 px ao lado de dois arcos segmentados espelhados, em escala logarítmica de 1 Mbps a 1 Gbps, com a unidade (Mbps) uma vez só no centro e as leituras parciais acendendo durante o teste. À direita, num painel de até 520 px separado por um fio, a latência em destaque e, numa grade 2 × 2 alinhada por fios, jitter, perda de pacotes, latência sob carga e resolução de nome; os achados fecham o painel, cada um com o glifo na cor do veredito. Em painéis abaixo de 1200 px a latência e as grandezas descem para uma faixa sob o instrumento (a latência à esquerda e as quatro grandezas em fila, e o nome da fonte do teste na linha do título) e, abaixo de 900 px, voltam à grade 2 × 2; o instrumento diminui junto e a página continua cabendo numa tela a 1120 × 800. Sem textos de apoio: só estado, números e achados (o que o teste envia e baixa fica na dica do botão). O que sobra de altura vai para o caminho PC → roteador → internet (ícones de 40 px na cor da rede, sem fundo; trecho estável num fio neutro, e o que tem problema fica âmbar ou vermelho, para o atraso saltar aos olhos no ponto certo), com o traço dos pings a 1.1.1.1 em repouso (aço) e durante o download (cor da rede; o bufferbloat aparece como o salto entre os blocos), que se estica até o fim da página, ao lado dos dados do adaptador (as linhas dividem a altura por igual), lidos ao abrir a página.
- Energia cabe numa tela com a janela maximizada, em quatro painéis: em cima, a alimentação (fonte ou bateria desenhada com gomos em tinta e a saúde em capacidade de fábrica × atual, planos instalados (o em uso com anel e ponto acesos no sinal), em que escolher um mostra a prévia do que muda e só Aplicar troca de fato, e só o essencial da regulagem: faixa e preferência do processador, tela e suspensão (o turbo só aparece se alguém o mudou; a prévia da troca tem título curto, "Prévia da troca", fundo elevado, e o aviso de máximo abaixo de 100 % é a cor âmbar do valor, para o cartão nunca mudar de altura ao escolher um plano)) e o consumo ao vivo de processador e placa de vídeo (placa com a leitura grande, a divisão entre processador e placa de vídeo em um medidor segmentado, o histórico de 5 min empilhado em faixas chapadas, com o ponto atual pulsando a cada leitura, e consumo médio, maior consumo e energia gasta na sessão numa régua, com a duração da sessão no cabeçalho), na mesma altura; embaixo, as tensões (trilhos ATX numa régua de ±10 % com a tolerância de ±5 % acesa em aço; componentes numa escala fixa) e o uso do computador nos últimos 7 dias, reconstruído do registro de eventos (faixa de totais com vãos iguais e fios entre eles, uma linha por dia com o total, hoje no topo; trechos ligados em aço e a sessão atual no sinal), que se estica até o fim da página. Detalhes ficam nas dicas, não em legendas. Em janelas estreitas os painéis descem um por linha. Os instrumentos ficam em `Features/Monitoring/Power/EnergyInstruments.cs`.
- Sistema é uma ficha técnica num painel só, sem ilustrações: hierarquia, grupos e tipografia. O topo é uma faixa um tom abaixo do painel com seis botões iguais (3 × 2 em janelas estreitas), cada um com fundo e contorno próprios, ícone de 32 px, sem fundo, na cor do componente (aço para o sistema operacional), o nome da categoria em DIN 15 e uma seta discreta à direita, para ler como botão; o mouse clareia o fundo e o escolhido ganha o fundo da placa, contorno mais forte e texto aceso, e perde a seta. Embaixo, a placa, toda ela com o fundo claro da cor do componente escolhido (cor chapada e uniforme), com sobretítulo (fabricante, na cor do canal), o nome da peça e as quatro leituras que a resumem, separadas por fios com o mesmo espaço dos dois lados (sem largura mínima). Depois, na largura toda, os grupos de linhas chave/valor (título de grupo em DIN 15 e uma frase que explica o assunto) que se distribuem em 3, 2 ou 1 coluna (`ColumnsPanel`) e rolam sozinhos. Tudo é texto: sem LEDs, selos ou etiquetas; a explicação, quando há, vem embaixo do valor em itálico de 12 px, um tom abaixo e com 8 px de ar, para a hierarquia chave → valor → explicação ficar clara (estilo `Explain`, o mesmo na frase dos grupos). A placa é o resumo; os grupos são a ficha completa, então um dado de identificação (nome completo, modelo, versão) pode estar na placa e de novo no grupo dele. O que não se repete é um dado entre dois grupos (como o espaço livre do disco). A página mostra o hardware, não o uso ao vivo. Campo que o Windows não preenche não aparece (nada de linha com travessão), e a página não cita outros programas. Clicar numa linha copia o valor. Armazenamento mostra primeiro um disco por linha com as unidades em proporção ao tamanho, em gomos no canal Storage, e depois a ficha de cada disco nos grupos. O atalho para as leituras ao vivo (Sensores, Placa de vídeo, Discos) fica no rodapé do card, à esquerda: botão discreto com contorno, ícone de pulso e seta. As fichas são montadas em `Features/Monitoring/Inventory/SystemSheets.cs`. Os tons seguem os cabeçalhos de Sensores: a faixa dos botões em `Raised`, a placa em `Selected` (fio `LineStrong`) e os grupos no fundo do painel; o botão escolhido usa o mesmo `Selected` da placa, para ler como a aba dela. O rodapé do processador, da memória RAM e da placa-mãe abre Sensores já filtrado no componente; o da placa de vídeo abre Placa de vídeo e o do armazenamento, Discos. No canto superior direito, "Exportar relatório" é o único botão preenchido da página (sinal, ícone solto, 13,5 seminegrito, cantos de 8 px): o mouse clareia, o clique afunda 1 px e, enquanto gera, ele diz "Gerando relatório…" e fica esmaecido.
- Sensores é um monitor de hardware que rola: índice fixo à esquerda (300 px) que funciona como filtro real. "Visão geral" mostra todos os componentes; escolher um mostra só ele (o filtro de tipo e a busca valem dentro da escolha, e os tipos e contagens são os do componente). O índice vai até a base da página. Cada item tem o ícone de 30 px, sem fundo, na cor do componente (a mesma do filete dos grupos; sinal na Visão geral, aço nos neutros), nome inteiro em até duas linhas e o tipo abaixo, sem leituras; o selecionado ganha fundo elevado, sem filete. Ordem: processador, placa de vídeo, armazenamento, placa-mãe, cada módulo de RAM, rede, memória física e memória virtual (a biblioteca as mede num bloco só; viram dois componentes, com dica). Os filtros por tipo ficam numa faixa em cima, na largura toda, em pílulas com a quantidade ("Todos os tipos" separado por um fio; a escolhida fica tingida de sinal); abaixo, o índice (300 px) e o cartão de sensores ocupam a mesma altura, começando e terminando na mesma linha. O cartão é um só, com um bloco por componente: o cabeçalho tem duas faixas, cada uma num tom, e os sensores ficam no fundo do cartão (três níveis): a principal (80 px, fundo `Selected`, fio `LineStrong`) com o nome em DIN 23 sobre o fundo claro da cor do componente, embaixo o tipo (13, tom secundário) e a quantidade de sensores (13, cor de texto) e a seta que recolhe (some quando há um componente só); e a das colunas (32 px, fundo `Raised`) com Atual, Mín., Média e Máx. alinhados à direita com os números de cada coluna de grupos. No cabeçalho fixo, o fundo das duas faixas cobre a largura toda e o conteúdo fica na largura útil; a rolagem tem modelo próprio em que a barra começa abaixo do cabeçalho (112 px), então nada dela fica escondido. Acima do cartão de sensores, alinhados com ele, os filtros por tipo ficam num grupo de fundo `Surface` e cantos redondos, com o rótulo "Filtrar por tipo de sensor" (funil no sinal, DIN 13 seminegrito, tom secundário) antes das pílulas; o índice e o cartão continuam começando na mesma linha. Os atalhos de Sistema abrem Sensores já filtrado: "PageSensors:cpu", ":ram" (primeiro módulo) e ":board" (`SensorBoard.Find`, `SensorsView.ShowComponent`). Os grupos ficam em duas colunas de propósito: numa coluna só, a 1920 px, o nome ficaria a mais de 1000 px dos números e a página do processador dobraria de altura. O cabeçalho do componente à vista fica fixo no topo da rolagem (`UpdateSticky`): mostra o último cujo cabeçalho já chegou ao topo e, quando o do próximo se aproxima, sobe junto com ele sem sobrepor; a troca é no instante em que os dois são idênticos e estão na mesma posição, então não há salto. Leitura nenhuma no cabeçalho, para cada valor aparecer uma vez só. O corpo distribui os grupos de tipo em 2 colunas (`ColumnsPanel`, 1 em janelas estreitas), cada um com título em DIN 16, um filete de 3 px na cor do canal do componente (`SensorComponent.Tone`: Cpu, Gpu, Memory, Virtual para a memória física e virtual do Windows, Storage, Board para a placa-mãe e Net para a rede; aço nos demais) e quantidade, e os sensores recuados 12 px embaixo. A linha tem 32 px, fio em cima, nome claro (o técnico fica na dica), a leitura em mostrador 16 com a unidade pequena (âmbar ou vermelho quando a temperatura passa do limite) e as estatísticas sem unidade, com casas decimais fixas por grandeza para alinhar pela vírgula. Mínimo, média e máximo são contados pelo CoreBi desde a abertura ou desde "Zerar estatísticas". Só placas de rede físicas e conectadas são lidas (`PhysicalNetwork`). O rodapé com a nota das estatísticas fica fixo, numa linha, sob o índice e os cartões. Os cartões só existem com a página à vista. A montagem fica em `Features/Monitoring/Sensors/SensorBoard.cs` e os nomes em `SensorNaming` (motor).
- O relatório exportado (HTML para ler e imprimir, PDF ou JSON) repete as seis fichas do Sistema na mesma ordem e com os mesmos dados (`Features/Reports/SystemReport.cs` → `ReportBuilder`): capa com o nome do computador e uma linha de resumo, sumário numerado, cada ficha com placa, leituras e grupos em duas colunas, e depois a parte de diagnóstico (discos, energia, rede, sensores, benchmarks). O logo (versão escura no papel claro, clara no tema escuro) abre a capa e assina o rodapé; o resumo de cada peça fica num painel tingido, com as leituras numa fila separada por fios, e os títulos de seção têm um traço verde-água sobre o fio (o relatório tem CSS próprio, ainda na paleta anterior). Na tela, o HTML abre no tema do aplicativo e uma barra flutuante alterna claro e escuro e imprime; a impressão é sempre a folha A4 clara. O PDF sai do Microsoft Edge em modo headless (`Features/Reports/PdfPrinter.cs`), com perfil temporário próprio e limite de 60 s. Na impressão, cada grupo fica inteiro numa página, títulos não ficam sozinhos no fim da folha, o diagnóstico começa em página nova e cada folha (menos a capa) traz no topo "CoreBi · Relatório do sistema" e o computador, e no rodapé a data e "Página X de Y". Números de série e o nome do usuário ficam fora.
- Telas de teste (Benchmark, Teste de carga) usam a mesma composição: palco ou leitura principal à esquerda e uma coluna de 320–330 px com configuração, resultado e histórico.

## Componentes

Todos em `Shell/Theme.xaml`; templates de dados compartilhados em `Shell/FeatureTemplates.xaml`; a paleta fixa do overlay e do aviso em `Shell/HudPalette.xaml`. As propriedades anexadas e os ajudantes de `Shell/Ui.cs` evitam estilos duplicados:

- `Ui.Icon`: ícone (nome de peça ou glifo) em botões, itens de navegação, cartões de teste, cartões de componente e campos.
- `Ui.Caption`: legenda de ladrilhos e cartões de seleção.
- `Ui.Placeholder` e `Ui.Suffix`: texto de espera e unidade (°C, min) dentro do campo. O texto de espera some quando o campo recebe o foco, para o cursor não piscar sobre a primeira letra dele; a busca de Sensores também solta o foco com um clique em qualquer lugar fora dela.
- `HwIcon`: o ícone de linha (peças desenhadas e glifos). Estilo `ChannelIcon` + `Ui.Channel` (Cpu, Gpu, Memory, Virtual, Storage, Board, Net ou Signal; vazio = Steel) para o ícone de um componente; `ChannelWash` + `Ui.Channel` para o fundo claro da ficha.
- `Ui.HoverFill` e `Ui.IconBrush`: a camada que acende por cima do botão sob o mouse e a cor própria do glifo (o fantasma acende o glifo no sinal); cada estilo de botão escolhe as suas.
- `SlidingSelectionPanel` (estilos `NavRail` e `SegmentRail`): pilha de opções com um destaque só, que desliza até a marcada; os itens sabem disso por `Ui.SlidingGroup` e não pintam o próprio fundo.
- `Ui.Glide(medidor, valor, animar)`: medidor que desliza até a leitura nova.
- `Ui.SetRunning(botão, rodando, …)`: o botão de iniciar vira botão de parar (cor, glifo e texto juntos).
- `Ui.SetTone(selo, tom)`: selo de veredito (`Badge` com `BadgeLed` e `BadgeText`): "Ok" em tinta com LED verde, "Warn" e "Danger" com fundo e texto na cor, "" sem dados.

| Componente | Estilo | Quando usar |
| --- | --- | --- |
| Ação principal | `PrimaryButton` | Uma por tela, verde-água chapado |
| Ação secundária | `SecondaryButton`, `GhostButton` | Ler novamente, abrir página, predefinições (o fantasma tem o glifo no sinal) |
| Interromper | `StopButton` | Teste em andamento |
| Remover em lista | `DangerGhostButton` | Inicialização |
| Navegação | `NavItem` em `SlidingSelectionPanel` (`NavRail`) | Barra lateral: fundo fixo `NavSel` que desliza até a página aberta, ícone aceso |
| Opções exclusivas | `SegmentGroup` + `SegmentRail` + `Segment` | Resolução, duração, preset (poucas opções fixas); a tecla elevada desliza até a escolhida; o item marcado reserva a largura do negrito, então marcar não desloca os vizinhos |
| Filtro gerado | `Chip` | Tipos de sensor, GPUs, unidades de disco (marcado = tingido de sinal) |
| Escolha de teste | `TestTile` | Benchmark (ícone do componente testado, na cor dele) |
| Componente a carregar | `ToggleCard` | Teste de carga (ícone do componente, na cor dele) |
| Ligar e desligar | `Switch` | Overlay, programas da inicialização, Driver Studio |
| Tema | `ThemeToggle` | Última linha da barra lateral; ligada = modo escuro (sol em cobre, lua em lavanda) |
| Seleção múltipla | `CheckBox` padrão | Apps |
| Opção em pílula | `OptionChip` | Itens do overlay (visto no sinal quando marcado, "+" quando não) |
| Ícone de componente | `HwIcon` + `ChannelIcon` | Abas de Sistema, índice de Sensores, cartões e atividade da Visão geral, rack de Discos, grupos do OverlayBi (sinal), Rede, páginas dedicadas |
| Fundo da ficha | `ChannelWash` | Placa de Sistema e cabeçalho de componente em Sensores |
| Veredito | `Badge` + `BadgeLed` + `Ui.SetTone`, `StatusLed` | Discos, Energia, Rede, saúde do PC |
| Marca de canal | `ChannelMark` | Legenda de gráfico, régua de consumo, download e upload |
| Medidor | `Meter`, máscara `SegmentMask` | Gargalo, histórico de benchmarks |

### Estados

| Estado | Como aparece |
| --- | --- |
| Normal | Texto `Muted` (itens de lista e navegação) ou `Fg` (conteúdo); contorno `Line` ou `LineStrong` |
| Mouse em cima | Uma camada de luz acende por cima em 120 ms (`Hover` nos neutros, `SignalHover` na ação principal); listas e navegação ganham fundo `Hover` e texto `Fg` |
| Escolhido | Navegação: fundo `NavSel` que desliza até o item e ícone no sinal. Segmentos: a tecla elevada desliza. Listas e abas: fundo `Selected`, texto `Fg`, seminegrito |
| Ligado / marcado | Trilho ou caixa cheios de `Signal`, com o botão ou o visto em `SignalFg` |
| Foco de teclado | Anel de 2 px em `Focus` (sinal claro), 3 px para fora do controle; campos acendem o contorno e o glifo no sinal |
| Pressionado | O botão afunda 1 px |
| Desativado | 40 % de opacidade, sem mudar o desenho (o destaque que desliza esmaece junto) |

Paleta de comandos (Ctrl + K): busca sem acento entre as páginas (lidas da própria navegação, com os mesmos ícones) e ações como alternar tema, exportar relatório, mostrar o overlay e testar a rede. No console de Rede, o botão inicia o teste e a troca de servidor é automática; a fonte acompanha o resultado e o relatório.

## Movimento

Movimento com função, curto e com desaceleração: a seleção desliza entre os itens da navegação e dos segmentos (260 ms) em vez de saltar; a luz do botão acende em 120 ms e apaga em 180 ms; os anéis da Visão geral enchem do zero até a leitura ao abrir a página (900 ms); a chave de tema desliza (240 ms) e troca sol e lua com um giro curto; a página entra com 170 ms de opacidade e 230 ms de deslocamento curto.

O que acompanha as leituras (o ponto atual dos gráficos pulsa uma vez a cada amostra; os medidores da barra lateral deslizam até o valor novo) só roda com a janela em primeiro plano e sem benchmark ou teste de carga em andamento (`MainWindow.LiveMotion`): o CoreBi fica aberto durante jogos e testes, e nada dele pode pesar na medição. Fora disso tudo vai direto ao valor. Todo o movimento respeita a opção "Mostrar animações no Windows" (`SystemParameters.ClientAreaAnimation`).

## Dados e honestidade

Gráficos e indicadores refletem só dados reais e ficam vazios ou com travessão quando não há amostra. Falta de sensor nunca aparece como zero. FPS estimado, resposta da API e FPS medido por ETW são identificados por texto. Sucesso parcial não usa mensagem de sucesso total.

## Como trocar a paleta

1. Cores do app: a tabela `Palette` de `Shell/ThemeManager.cs`; depois, `python tools/sync_theme.py` copia os valores escuros para o bloco gerado de `Shell/Theme.xaml`.
2. Overlay e aviso: `Shell/HudPalette.xaml`.
3. Tipografia, raios e respiros: os recursos `UiFont`, `SmallFont`, `DisplayFont`, `LabelFont`, `Readout`, `Radius*` e `PanelPadding` de `Shell/Theme.xaml`.

Nada mais precisa mudar: as telas só usam essas chaves.

## Verificação

- `tools/check_resources.py` cruza cada `{StaticResource}`/`{DynamicResource}` e cada chave usada em C# com as definidas no tema (incluindo `HudPalette.xaml`), respeitando a ordem de carga (o compilador de XAML não detecta chave inexistente), e confere se a paleta do `Theme.xaml` está em dia com o `ThemeManager.cs`.
- Compilação WPF, construção e navegação das 15 páginas, ida e volta de tema e regras de indisponibilidade estão no executor de testes (`tests/CoreBi.Tests`).
- Prints para revisar o visual: `CoreBi.Tests.exe --shots pasta [segundos] ["páginas"] [dark,light]` abre a janela fora da tela, invisível e sem roubar o foco, lê o hardware de verdade (elevado, também os sensores do processador) e salva cada página em PNG nos temas pedidos, com uma cópia das configurações.
- Ainda pendente de revisão manual em Windows: navegação completa por teclado, leitor de tela, alto contraste e DPI em vários monitores.
