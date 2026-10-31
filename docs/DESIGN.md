---
version: 2.0
name: CoreBi
description: 'Bancada: a interface do CoreBi como um instrumento de medição. O painel é neutro (grafite ou papel), as ações são tinta e a cor fica para o que é medido.'
colors:
  # Tema escuro "Grafite" (o claro, "Papel", está na tabela de ThemeManager.cs)
  bg: "#0E1214"
  sidebar: "#0A0D0F"
  surface: "#15191C"
  raised: "#1C2022"
  selected: "#282D30"
  line: "#24282B"
  line-strong: "#353A3D"
  foreground: "#E6EAEC"
  muted: "#A3AAAE"
  faint: "#737A7E"
  ink: "#E3E7E9"
  ink-foreground: "#101315"
  focus: "#C9CFD2"
  signal: "#27C9BC"
  cpu: "#07A293"
  gpu: "#C77E50"
  memory: "#A183D4"
  storage: "#4E719D"
  steel: "#8696A2"
  ok: "#6FC889"
  warn: "#EDB755"
  danger: "#EE6E63"
  light-bg: "#F1F4F6"
  light-foreground: "#182228"
  light-ink: "#1A242A"
  light-signal: "#087E78"
typography:
  display:
    fontFamily: "Bahnschrift"
    fontWeight: "SemiBold"
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
    fontFamily: "Segoe Fluent Icons, Segoe MDL2 Assets"
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
    backgroundColor: "{colors.ink}"
    textColor: "{colors.ink-foreground}"
  secondary-button:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.foreground}"
  stop-button:
    backgroundColor: "{colors.danger} 15%"
    textColor: "{colors.danger}"
  selected:
    backgroundColor: "{colors.selected}"
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

O CoreBi mede hardware. A interface se comporta como um **instrumento de bancada**: um painel fosco, leituras grandes em letra de mostrador, retícula de osciloscópio e medidores segmentados. Num instrumento de verdade o painel não tem cor; acende só o que está medindo e o que pede atenção. O CoreBi segue a mesma regra: **o painel é neutro, a medição tem cor**.

Quatro regras resumem o sistema:

1. **Tudo o que se clica é tinta.** Navegação, abas, filtros, segmentos, interruptores, caixas marcadas, foco de teclado e a ação principal usam a escala neutra: a seleção se lê pelo contraste (fundo elevado, texto aceso, contorno mais forte) e pela forma, nunca por uma cor de destaque. A ação principal é tinta cheia: papel sobre grafite no tema escuro, grafite sobre papel no claro.
2. **Cada componente tem um canal, e só um.** Processador, placa de vídeo, memória e armazenamento têm cada um a sua cor, a mesma em todas as telas (gráficos, medidores, anéis, legendas, a barra lateral e o filete dos grupos de Sensores). Download e upload usam o par do processador e da placa de vídeo, como os dois canais da marca. Ícones nunca levam cor de canal: são todos da mesma cor neutra.
3. **Saudável é silencioso.** Verde, âmbar e vermelho são veredito. "Tudo bem" é um LED verde pequeno ao lado de um texto em tinta; atenção e crítico ganham fundo e texto na cor, porque precisam ser vistos. Faixas "normais" de escala (ideal, tolerância) ficam neutras; só as zonas de atenção e crítica são pintadas.
4. **O sinal é a marca.** O verde-água do "i" do logo aparece no logo, no que está ao vivo (LED "Ao vivo", jogo sendo medido, overlay ligado) e no FPS do overlay. Não é cor de botão, de seleção nem de enfeite.

Conteúdo em português brasileiro, em caixa de frase. Nomes de hardware, APIs e unidades mantêm a grafia técnica.

## Cores

A fonte única é a tabela de `src/CoreBi.Desktop/Shell/ThemeManager.cs` (pares escuro/claro); `Theme.xaml` repete os valores do tema escuro só para o designer do XAML. Nenhuma tela define cor própria: tudo é `DynamicResource` (ou `SetResourceReference` no código) para uma destas chaves, então trocar a paleta é trocar valores numa tabela.

| Papel | Chaves | Uso |
| --- | --- | --- |
| Superfícies | `Bg`, `Sidebar`, `Surface`, `Raised`, `Hover`, `Selected`, `Well`, `Scrim` | Bancada (fundo), barra lateral, painel, controle elevado, realce, item escolhido, palco de imagens, fundo da paleta de comandos |
| Fios | `Line`, `LineStrong` | Divisões de 1 px e contorno de controles |
| Texto | `Fg`, `Muted`, `Faint` | Texto principal, secundário, notas e eixos |
| Tinta | `Ink`, `InkHover`, `InkFg`, `Focus` | Ação principal, interruptor ligado, caixa marcada, progresso de tarefa, foco de teclado |
| Sinal | `Signal`, `SignalSoft` | Logo, LED "ao vivo", FPS do overlay |
| Canais | `Cpu`, `Gpu`, `Memory`, `Storage`, `NetDown`, `NetUp`, `Steel` | O que cada componente mede; `Steel` é o neutro técnico (tempo ligado, pings em repouso, faixas de tolerância) |
| Veredito | `Ok`, `Warn`, `Danger` e as variantes `Soft` | LEDs, selos, valores fora do limite, zonas de atenção e crítica |
| Fixa | `Hud*` em `Shell/HudPalette.xaml` | Overlay e aviso (toast), sempre grafite, por cima de jogos e da área de trabalho |

O grafite (e o papel) é quase neutro, levemente frio (OKLCH com croma ≈ 0,007 e matiz 232°): conversa com o grafite azulado da marca (`brand/geometry.json`) sem virar o "azul-marinho de painel" genérico.

### Canais

| Componente | Chave | Escuro | Claro | Onde aparece |
| --- | --- | --- | --- | --- |
| Processador | `Cpu` | `#07A293` | `#0A8E82` | Gráfico e cartão da Visão geral, barra lateral, consumo em Energia, faixa de desempenho do plano, filete dos grupos em Sensores |
| Placa de vídeo | `Gpu` | `#C77E50` | `#A25A30` | Gráfico, cartão e anel da VRAM na Visão geral, barra lateral, consumo em Energia |
| Memória | `Memory` | `#A183D4` | `#9274C3` | Anel da RAM, barra lateral, módulos e memória em Sensores |
| Armazenamento | `Storage` | `#4E719D` | `#3A5B8D` | Anel do disco, barra lateral, vida útil, régua térmica, histórico e espaço em Discos, unidades em Sistema |
| Rede | `NetDown`, `NetUp` | `#07A293`, `#C77E50` | `#0A8E82`, `#A25A30` | Arcos de download e upload e pings sob carga |

Os canais foram escolhidos em OKLCH com a mesma força (L ≈ 0,52–0,67 no escuro, croma ≈ 0,1) e conferidos com o validador de paletas de dados: os quatro são distinguíveis entre si, aos pares, também para protanopia e deuteranopia (ΔE ≥ 9) e com visão normal (ΔE ≥ 15), nos dois temas. Áreas sob linhas são planas e claras (o próprio canal com 10 a 20 % de opacidade), nunca degradês.

### O que não fazer

- Pintar botão, aba, filtro, segmento, interruptor ou ícone com a cor de um canal ou com o sinal.
- Usar verde para dizer "está tudo bem" em grande (selo cheio, texto verde de 24 px, bolha verde atrás do ícone). Saudável é LED pequeno e texto em tinta.
- Escrever um hex numa tela. Cor nova é uma linha nova na tabela do `ThemeManager` (ou do `HudPalette.xaml`, se for do overlay).

## Tipografia

Duas famílias, com papéis claros:

| Papel | Família | Onde |
| --- | --- | --- |
| Títulos | Bahnschrift SemiBold (DIN 1451, a letra da assinatura CoreBi) | Título da página (`PageTitle`, 30), título de painel (`SectionTitle`, 16), título de grupo de uma ficha (`GroupTitle`, 15), nomes em destaque (placa do Sistema 30, componente em Sensores 23, modelo do disco 23) |
| Mostrador | Bahnschrift SemiCondensed, algarismos tabulares | Leituras (`ReadoutText` 22, `ReadoutLarge` 36, `ReadoutHero` 52), eixos de gráfico, overlay |
| Texto | Segoe UI Variable Text | Corpo, rótulos e controles (13; `Body` com 20 de entrelinha) |
| Texto pequeno | Segoe UI Variable Small (o tamanho óptico feito para letra miúda) | Legendas (`Caption` 12/17), notas (`Footnote` 11,5/16), subtextos (`StatSub` 11,5) e cabeçalhos da navegação |
| Código | Cascadia Mono | Registro do teste de carga e linhas de comando da Inicialização |

Hierarquia de uma tela: título da página (DIN 30) → título de painel (DIN 16) → rótulo (Segoe 12, `Muted`) → valor (mostrador ou Segoe 13 em `Fg`) → nota (Segoe Small 11,5, `Faint`). Números em texto comum usam `Typography.NumeralAlignment="Tabular"` (estilo `Num`) para não "dançar" quando o valor muda. Rótulos são curtos e em caixa de frase; não há rótulos em caixa alta. Botões dizem exatamente o que acontece ("Iniciar teste", "Exportar relatório", "Ler novamente").

## Forma e espaçamento

Sem sombras nos painéis: a hierarquia vem do tom (bancada → painel → elevado → escolhido) e de fios de 1 px. A única sombra do app é a da paleta de comandos, que flutua acima de tudo (e a do aviso, fora da janela).

Raios por hierarquia, como recursos (`RadiusPanel` 10, `RadiusInset` 8, `RadiusControl` 7, `RadiusSmall` 4): painéis 10, poços, teclas de categoria e cartões internos 8, controles 7, selos e teclas de atalho 4; filtros e opções em pílula são inteiramente redondos (14). Recortes feitos em código usam `Ui.PanelRadius`.

Espaçamento numa grade de 4: margem da página 36, título → primeiro painel 18 (`PageHeader`, igual em todas as telas), 16 entre painéis, 24 × 20 dentro deles (`PanelPadding`). A marca segue a malha 256 × 256 de `brand/geometry.json`, desenhada em vetor na barra lateral.

## Layout

- Janela de 1360 × 860 (mínimo 1120 × 680), sem moldura nativa, cantos arredondados do Windows 11 via DWM. Maximizada, ocupa exatamente o espaço de trabalho do monitor (`WM_GETMINMAXINFO`), sem passar por baixo da barra de tarefas.
- Barra lateral de 244 px: marca, navegação agrupada (Monitoramento, Jogos, Desempenho, Ferramentas) e, no rodapé, a central de monitoramento (CPU, GPU, RAM e disco, cada um com medidor segmentado na cor do seu canal; oculta na Visão geral, que mostra o mesmo em tamanho grande) e, por último, a chave de tema ("Modo escuro"), que fica sempre no mesmo lugar. O número da versão fica discreto na barra de título, ao lado do botão de minimizar.
- Barra de título de 52 px: atalho da paleta de comandos à esquerda; número da versão e controles da janela à direita. O estado da coleta aparece na própria página de Sensores.
- Página: margem de 36 px, cabeçalho com título e subtítulo (o subtítulo carrega o estado vivo da página quando existe) e a ação principal à direita. Painéis com 16 px entre si.
- Visão geral cabe numa tela com a janela maximizada: o gráfico e a atividade recente ocupam a sobra de altura; em janelas menores a página rola e os componentes passam a duas colunas. Processador e placa de vídeo têm cartão de uso (marca de canal, nome, leitura, medidor e grandezas); RAM, VRAM e disco do sistema dividem um mostrador de anéis concêntricos (Memory, Gpu e Storage) com quanto está em uso e o que sobra, sem campos que não se aplicam a eles. As setas e as linhas dos anéis levam à seção de cada item, e o gráfico mostra, sob o mouse, a hora e o valor exato de cada canal; enquanto o mouse está sobre ele o gráfico pausa e retoma ao sair. Na saúde do PC cada item tem só o glifo na cor do estado (visto verde ou alerta âmbar), sem bolha atrás; a atividade recente usa glifos neutros soltos.
- Overlay cabe numa tela com a janela maximizada: interruptor e atalho no cabeçalho; o monitor (desenho fixo escalado por `Viewbox` dentro de `FillBox`) ocupa a sobra de altura e tem um canto tracejado clicável em cada quarto da tela (contorno claro, que acende sob o mouse); o canto escolhido mostra a prévia, que é a mesma `OverlayPanel` do jogo com os itens marcados, e o LED do monitor acende no sinal com o overlay ligado. "O que mostrar" fica ao lado, uma linha por componente distribuída por `SpreadPanel`, com os glifos todos em tinta; a detecção de FPS é uma faixa no rodapé, com um LED (sinal medindo um jogo, cinza aguardando) que só mostra texto quando há o que dizer (overlay desligado não tem aviso). Os controles de posição, opacidade (trilho em tinta) e tamanho formam uma faixa de 48 px com três blocos separados por fios; nada nela muda de lugar ao trocar de opção. Não há selo sobre a prévia. Os subtítulos dos dois cartões explicam o que fazer, e nomes de outras seções entre aspas (“O que mostrar”). A VRAM só tem uso em % e GB usados (a temperatura não tinha sensor confiável). Em janelas estreitas os dois painéis ficam um embaixo do outro e a página rola.
- Discos cabe numa tela com a janela maximizada: rack de 300 px à esquerda (uma baia compacta por disco, selecionável, só com nome, características e LED de veredito) e o disco escolhido à direita, com a placa de identificação (modelo, selo de veredito com LED, especificação e o espaço usado, total e livre), três instrumentos lado a lado (vida útil em arco de gomos, temperatura em régua térmica com histórico, integridade em linhas com LED) e o odômetro no rodapé. Os instrumentos são desenhados no canal Storage em `Features/Monitoring/Disks/DiskInstruments.cs` com pincéis por referência de recurso, então acompanham o tema sem redesenho manual; a faixa ideal da régua é neutra e só atenção e crítico são pintados. "Na faixa ideal" e as linhas de integridade saudáveis ficam em tinta com o LED verde; o que pede atenção vira selo âmbar ou vermelho. Em janelas estreitas a integridade desce para o lado do odômetro e a página rola.
- Rede cabe numa tela com a janela maximizada, com o título alinhado ao topo como nas outras páginas: o console do teste (botão de 46 px em tinta, etapa atual, barra contínua em tinta com as estações Latência, Download e Upload nas frações do motor e porcentagem) fica ao lado do título e desce para baixo dele em janelas estreitas; a barra só avança quando o motor informa trabalho feito e cada passo desliza 420 ms com desaceleração. O diagnóstico é um instrumento e ocupa a sobra de altura: download (`NetDown`) e upload (`NetUp`) em dois arcos segmentados espelhados, em escala logarítmica de 1 Mbps a 1 Gbps e sem marcas numéricas, com a latência no centro e as leituras parciais acendendo durante o teste; jitter, perda, latência sob carga e DNS numa régua de quatro células e os achados no rodapé do painel, cada um com o glifo na cor do veredito. Sem textos de apoio: só estado, números e achados (o que o teste envia e baixa fica na dica do botão). Com menos peso, o caminho PC → roteador → internet (trecho estável num fio neutro; o que tem problema fica âmbar ou vermelho, para o atraso saltar aos olhos no ponto certo), o traço dos pings a 1.1.1.1 em repouso (aço) e durante o download (canal do download; o bufferbloat aparece como o salto entre os blocos) e os dados do adaptador, lidos ao abrir a página.
- Energia cabe numa tela com a janela maximizada, em quatro painéis: em cima, a alimentação (fonte ou bateria desenhada com gomos em tinta e a saúde em capacidade de fábrica × atual, planos instalados, em que escolher um mostra a prévia do que muda e só Aplicar troca de fato, e só o essencial da regulagem: faixa e preferência do processador, tela e suspensão (o turbo só aparece se alguém o mudou; a prévia da troca tem título curto, "Prévia da troca", fundo elevado, e o aviso de máximo abaixo de 100 % é a cor âmbar do valor, para o cartão nunca mudar de altura ao escolher um plano)) e o consumo ao vivo de processador e placa de vídeo (placa com a leitura grande, a divisão entre processador e placa de vídeo em um medidor segmentado, o histórico de 5 min empilhado em faixas planas e consumo médio, maior consumo e energia gasta na sessão numa régua, com a duração da sessão no cabeçalho), na mesma altura; embaixo, as tensões (trilhos ATX numa régua de ±10 % com a tolerância de ±5 % acesa em aço; componentes numa escala fixa) e o uso do computador nos últimos 7 dias, reconstruído do registro de eventos (faixa de totais com vãos iguais e fios entre eles, uma linha por dia com o total, hoje no topo; trechos ligados em aço e a sessão atual em tinta), que se estica até o fim da página. Detalhes ficam nas dicas, não em legendas. Em janelas estreitas os painéis descem um por linha. Os instrumentos ficam em `Features/Monitoring/Power/EnergyInstruments.cs`.
- Sistema é uma ficha técnica num painel só, sem ilustrações: hierarquia, grupos e tipografia. O topo é uma faixa um tom abaixo do painel com seis botões iguais (3 × 2 em janelas estreitas), cada um com fundo e contorno próprios, ícone em ladrilho de 38 px, o nome da categoria e uma seta discreta à direita, para ler como botão; o mouse clareia o fundo e o escolhido ganha o fundo da placa, contorno mais forte, texto e ícone acesos em tinta, e perde a seta. Embaixo, a placa com sobretítulo (fabricante), o nome da peça e as quatro leituras que a resumem, separadas por fios com o mesmo espaço dos dois lados (sem largura mínima). Depois, na largura toda, os grupos de linhas chave/valor (título de grupo em DIN 15 e uma frase que explica o assunto) que se distribuem em 3, 2 ou 1 coluna (`ColumnsPanel`) e rolam sozinhos. Tudo é texto: sem LEDs, selos ou etiquetas; a explicação, quando há, vem embaixo do valor em itálico de 12 px, um tom abaixo e com 8 px de ar, para a hierarquia chave → valor → explicação ficar clara (estilo `Explain`, o mesmo na frase dos grupos). A placa é o resumo; os grupos são a ficha completa, então um dado de identificação (nome completo, modelo, versão) pode estar na placa e de novo no grupo dele. O que não se repete é um dado entre dois grupos (como o espaço livre do disco). A página mostra o hardware, não o uso ao vivo. Campo que o Windows não preenche não aparece (nada de linha com travessão), e a página não cita outros programas. Clicar numa linha copia o valor. Armazenamento mostra primeiro um disco por linha com as unidades em proporção ao tamanho, em gomos no canal Storage, e depois a ficha de cada disco nos grupos. O atalho para as leituras ao vivo (Sensores, Placa de vídeo, Discos) fica no rodapé do card, à esquerda: botão discreto com contorno, ícone de pulso e seta. As fichas são montadas em `Features/Monitoring/Inventory/SystemSheets.cs`. Os tons seguem os cabeçalhos de Sensores: a faixa dos botões em `Raised`, a placa em `Selected` (fio `LineStrong`) e os grupos no fundo do painel; o botão escolhido usa o mesmo `Selected` da placa, para ler como a aba dela. O rodapé do processador, da memória RAM e da placa-mãe abre Sensores já filtrado no componente; o da placa de vídeo abre Placa de vídeo e o do armazenamento, Discos. No canto superior direito, "Exportar relatório" é o único botão preenchido da página (tinta, ladrilho de ícone translúcido, 13,5 seminegrito, cantos de 8 px): o mouse clareia, o clique afunda 1 px e, enquanto gera, ele diz "Gerando relatório…" e fica esmaecido.
- Sensores é um monitor de hardware que rola: índice fixo à esquerda (300 px) que funciona como filtro real. "Visão geral" mostra todos os componentes; escolher um mostra só ele (o filtro de tipo e a busca valem dentro da escolha, e os tipos e contagens são os do componente). O índice vai até a base da página. Cada item tem um bloco de ícone de 34 px, centralizado e neutro, nome inteiro em até duas linhas e o tipo abaixo, sem leituras; o selecionado ganha fundo elevado, o bloco ganha contorno e o ícone acende em tinta, sem filete. Ordem: processador, placa de vídeo, armazenamento, placa-mãe, cada módulo de RAM, rede, memória física e memória virtual (a biblioteca as mede num bloco só; viram dois componentes, com dica). Os filtros por tipo ficam numa faixa em cima, na largura toda, em pílulas com a quantidade ("Todos os tipos" separado por um fio; a escolhida tem fundo elevado e texto em tinta); abaixo, o índice (300 px) e o cartão de sensores ocupam a mesma altura, começando e terminando na mesma linha. O cartão é um só, com um bloco por componente: o cabeçalho tem duas faixas, cada uma num tom, e os sensores ficam no fundo do cartão (três níveis): a principal (80 px, fundo `Selected`, fio `LineStrong`) com o nome em DIN 23 (sem ícone nem cor por canal), embaixo o tipo (13, tom secundário) e a quantidade de sensores (13, cor de texto) e a seta que recolhe (some quando há um componente só); e a das colunas (32 px, fundo `Raised`) com Atual, Mín., Média e Máx. alinhados à direita com os números de cada coluna de grupos. No cabeçalho fixo, o fundo das duas faixas cobre a largura toda e o conteúdo fica na largura útil; a rolagem tem modelo próprio em que a barra começa abaixo do cabeçalho (112 px), então nada dela fica escondido. Acima do cartão de sensores, alinhados com ele, os filtros por tipo ficam num grupo de fundo `Surface` e cantos redondos, com o rótulo "Filtrar por tipo de sensor" (ícone de funil, 12,5 seminegrito, tom secundário) antes das pílulas; o índice e o cartão continuam começando na mesma linha. Os atalhos de Sistema abrem Sensores já filtrado: "PageSensors:cpu", ":ram" (primeiro módulo) e ":board" (`SensorBoard.Find`, `SensorsView.ShowComponent`). Os grupos ficam em duas colunas de propósito: numa coluna só, a 1920 px, o nome ficaria a mais de 1000 px dos números e a página do processador dobraria de altura. O cabeçalho do componente à vista fica fixo no topo da rolagem (`UpdateSticky`): mostra o último cujo cabeçalho já chegou ao topo e, quando o do próximo se aproxima, sobe junto com ele sem sobrepor; a troca é no instante em que os dois são idênticos e estão na mesma posição, então não há salto. Leitura nenhuma no cabeçalho, para cada valor aparecer uma vez só. O corpo distribui os grupos de tipo em 2 colunas (`ColumnsPanel`, 1 em janelas estreitas), cada um com título em DIN 16, um filete de 3 px na cor do canal do componente (`SensorComponent.Tone`: processador, placa de vídeo, memória ou armazenamento; aço nos demais) e quantidade, e os sensores recuados 12 px embaixo. A linha tem 32 px, fio em cima, nome claro (o técnico fica na dica), a leitura em mostrador 16 com a unidade pequena (âmbar ou vermelho quando a temperatura passa do limite) e as estatísticas sem unidade, com casas decimais fixas por grandeza para alinhar pela vírgula. Mínimo, média e máximo são contados pelo CoreBi desde a abertura ou desde "Zerar estatísticas". Só placas de rede físicas e conectadas são lidas (`PhysicalNetwork`). O rodapé com a nota das estatísticas fica fixo, numa linha, sob o índice e os cartões. Os cartões só existem com a página à vista. A montagem fica em `Features/Monitoring/Sensors/SensorBoard.cs` e os nomes em `SensorNaming` (motor).
- O relatório exportado (HTML para ler e imprimir, PDF ou JSON) repete as seis fichas do Sistema na mesma ordem e com os mesmos dados (`Features/Reports/SystemReport.cs` → `ReportBuilder`): capa com o nome do computador e uma linha de resumo, sumário numerado, cada ficha com placa, leituras e grupos em duas colunas, e depois a parte de diagnóstico (discos, energia, rede, sensores, benchmarks). O logo (versão escura no papel claro, clara no tema escuro) abre a capa e assina o rodapé; o resumo de cada peça fica num painel tingido, com as leituras numa fila separada por fios, e os títulos de seção têm um traço verde-água sobre o fio (o relatório tem CSS próprio, ainda na paleta anterior). Na tela, o HTML abre no tema do aplicativo e uma barra flutuante alterna claro e escuro e imprime; a impressão é sempre a folha A4 clara. O PDF sai do Microsoft Edge em modo headless (`Features/Reports/PdfPrinter.cs`), com perfil temporário próprio e limite de 60 s. Na impressão, cada grupo fica inteiro numa página, títulos não ficam sozinhos no fim da folha, o diagnóstico começa em página nova e cada folha (menos a capa) traz no topo "CoreBi · Relatório do sistema" e o computador, e no rodapé a data e "Página X de Y". Números de série e o nome do usuário ficam fora.
- Telas de teste (Benchmark, Teste de carga) usam a mesma composição: palco ou leitura principal à esquerda e uma coluna de 320–330 px com configuração, resultado e histórico.

## Componentes

Todos em `Shell/Theme.xaml`; templates de dados compartilhados em `Shell/FeatureTemplates.xaml`; a paleta fixa do overlay e do aviso em `Shell/HudPalette.xaml`. As propriedades anexadas e os ajudantes de `Shell/Ui.cs` evitam estilos duplicados:

- `Ui.Icon`: glifo Segoe Fluent em botões, itens de navegação, ladrilhos de teste, cartões de componente e campos.
- `Ui.Caption`: legenda de ladrilhos e cartões de seleção.
- `Ui.Placeholder` e `Ui.Suffix`: texto de espera e unidade (°C, min) dentro do campo. O texto de espera some quando o campo recebe o foco, para o cursor não piscar sobre a primeira letra dele; a busca de Sensores também solta o foco com um clique em qualquer lugar fora dela.
- `Ui.SetRunning(botão, rodando, …)`: o botão de iniciar vira botão de parar (cor, glifo e texto juntos).
- `Ui.SetTone(selo, tom)`: selo de veredito (`Badge` com `BadgeLed` e `BadgeText`): "Ok" em tinta com LED verde, "Warn" e "Danger" com fundo e texto na cor, "" sem dados.

| Componente | Estilo | Quando usar |
| --- | --- | --- |
| Ação principal | `PrimaryButton` | Uma por tela, em tinta |
| Ação secundária | `SecondaryButton`, `GhostButton` | Ler novamente, abrir página, predefinições |
| Interromper | `StopButton` | Teste em andamento |
| Remover em lista | `DangerGhostButton` | Inicialização |
| Opções exclusivas | `SegmentGroup` + `Segment` | Resolução, duração, preset (poucas opções fixas); a escolhida sobe com fundo elevado e fio; o item marcado reserva a largura do negrito, então marcar não desloca os vizinhos |
| Filtro gerado | `Chip` | Tipos de sensor, GPUs, unidades de disco |
| Escolha de teste | `TestTile` | Benchmark |
| Componente a carregar | `ToggleCard` | Teste de carga |
| Ligar e desligar | `Switch` | Overlay, programas da inicialização, Driver Studio |
| Tema | `ThemeToggle` | Última linha da barra lateral; ligada = modo escuro |
| Seleção múltipla | `CheckBox` padrão | Apps |
| Opção em pílula | `OptionChip` | Itens do overlay (visto quando marcado, "+" quando não) |
| Veredito | `Badge` + `BadgeLed` + `Ui.SetTone`, `StatusLed` | Discos, Energia, Rede, saúde do PC |
| Marca de canal | `ChannelMark` | Legenda de gráfico, cartão de componente, régua de consumo |
| Medidor | `Meter`, máscara `SegmentMask` | Gargalo, histórico de benchmarks |

### Estados

| Estado | Como aparece |
| --- | --- |
| Normal | Texto `Muted` (itens de lista e navegação) ou `Fg` (conteúdo); contorno `Line` ou `LineStrong` |
| Mouse em cima | Fundo `Hover`, texto `Fg`, contorno um passo mais forte (`Faint`) |
| Escolhido | Fundo `Selected`, texto e ícone `Fg`, seminegrito; nas teclas de categoria e nos segmentos também um fio mais forte |
| Ligado / marcado | Trilho ou caixa cheios de `Ink`, com o botão ou o visto em `InkFg` |
| Foco de teclado | Anel de 2 px em `Focus`, 3 px para fora do controle |
| Pressionado | O botão afunda 1 px |
| Desativado | 40 % de opacidade, sem mudar o desenho |

Paleta de comandos (Ctrl + K): busca sem acento entre as páginas (lidas da própria navegação) e ações como alternar tema, exportar relatório, mostrar o overlay e testar a rede. No console de Rede, o botão inicia o teste e a troca de servidor é automática; a fonte acompanha o resultado e o relatório.

## Movimento

Apenas em resposta a uma ação: a página entra com 170 ms de opacidade e 230 ms de deslocamento curto, respeitando a opção "Mostrar animações no Windows" (`SystemParameters.ClientAreaAnimation`). Item de navegação selecionado ganha fundo elevado e ícone aceso em 160 ms, sem barra lateral; a chave de tema desliza em 240 ms e troca sol e lua com um giro curto. Não há animação contínua: o app fica aberto durante jogos e benchmarks, e qualquer custo de CPU/GPU contaminaria a medição.

## Dados e honestidade

Gráficos e indicadores refletem só dados reais e ficam vazios ou com travessão quando não há amostra. Falta de sensor nunca aparece como zero. FPS estimado, resposta da API e FPS medido por ETW são identificados por texto. Sucesso parcial não usa mensagem de sucesso total.

## Como trocar a paleta

1. Cores do app: só a tabela `Palette` de `Shell/ThemeManager.cs` (e, por coerência, os valores escuros repetidos no topo de `Shell/Theme.xaml`).
2. Overlay e aviso: `Shell/HudPalette.xaml`.
3. Tipografia, raios e respiros: os recursos `UiFont`, `SmallFont`, `DisplayFont`, `Readout`, `Radius*` e `PanelPadding` de `Shell/Theme.xaml`.

Nada mais precisa mudar: as telas só usam essas chaves.

## Verificação

- `tools/check_resources.py` cruza cada `{StaticResource}`/`{DynamicResource}` e cada chave usada em C# com as definidas no tema (incluindo `HudPalette.xaml`), respeitando a ordem de carga (o compilador de XAML não detecta chave inexistente).
- Compilação WPF, construção e navegação das 15 páginas, ida e volta de tema e regras de indisponibilidade estão no executor de testes (`tests/CoreBi.Tests`).
- Prints para revisar o visual: `CoreBi.Tests.exe --shots pasta [segundos] ["páginas"] [dark,light]` abre a janela fora da tela, invisível e sem roubar o foco, lê o hardware de verdade (elevado, também os sensores do processador) e salva cada página em PNG nos temas pedidos, com uma cópia das configurações.
- Ainda pendente de revisão manual em Windows: navegação completa por teclado, leitor de tela, alto contraste e DPI em vários monitores.
