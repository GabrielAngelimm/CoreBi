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

- Janela de 1360 × 860 (mínimo 1120 × 680), sem moldura nativa, cantos arredondados do Windows 11 via DWM.
- Barra lateral de 244 px: marca, navegação agrupada (Monitoramento, Jogos, Desempenho, Ferramentas) e, no rodapé, a telemetria permanente (CPU e GPU com medidor segmentado) e o estado do teste de carga.
- Barra de título de 52 px: atalho da paleta de comandos à esquerda, pílula com o estado da coleta de sensores e os controles da janela à direita.
- Página: margem de 36 px, cabeçalho com título e subtítulo (o subtítulo carrega o estado vivo da página quando existe) e a ação principal à direita. Painéis com 16 px entre si.
- Telas de teste (Benchmark, Teste de carga) usam a mesma composição: palco ou leitura principal à esquerda e uma coluna de 320–330 px com configuração, resultado e histórico.

## Profundidade e forma

Sem sombras nos painéis: a hierarquia vem do tom (fundo → painel → elevado) e de fios de 1 px. A única sombra do app é a da paleta de comandos, que flutua acima de tudo (e a do toast, fora da janela).

Raios: 8 em controles, 9 em poços internos, 12 em painéis, 14 na paleta. Selos e teclas usam 5. A marca segue a malha 256 × 256 de `brand/geometry.json`, desenhada em vetor na barra lateral.

## Componentes

Todos em `Shell/Theme.xaml`; templates de dados compartilhados em `Shell/FeatureTemplates.xaml`. As propriedades anexadas de `Shell/Ui.cs` evitam estilos duplicados:

- `Ui.Icon`: glifo Segoe Fluent em botões, itens de navegação, ladrilhos de teste, cartões de componente e campos.
- `Ui.Caption`: legenda de ladrilhos e cartões de seleção.
- `Ui.Placeholder` e `Ui.Suffix`: texto de espera e unidade (°C, min) dentro do campo.
- `Ui.SetRunning(botão, rodando, …)`: o botão de iniciar vira botão de parar (cor, glifo e texto juntos).

| Componente | Estilo | Quando usar |
| --- | --- | --- |
| Ação principal | `PrimaryButton` | Uma por tela |
| Ação secundária | `SecondaryButton`, `GhostButton` | Ler novamente, abrir página, predefinições |
| Interromper | `StopButton` | Teste em andamento |
| Remover em lista | `DangerGhostButton` | Inicialização |
| Opções exclusivas | `SegmentGroup` + `Segment` | Resolução, duração, preset (poucas opções fixas) |
| Filtro gerado | `Chip` | Categorias de sensores, GPUs, unidades de disco |
| Escolha de teste | `TestTile` | Benchmark |
| Componente a carregar | `ToggleCard` | Teste de carga |
| Ligar e desligar | `Switch` | Overlay, programas da inicialização, Driver Studio |
| Seleção múltipla | `CheckBox` padrão | Apps, itens do overlay |
| Veredito | `CardTemplate` | Discos, Energia, Rede (faixa lateral + selo colorido) |
| Medidor | `Meter`, máscara `SegmentMask` | Gargalo, histórico de benchmarks |

Paleta de comandos (Ctrl + K): busca sem acento entre as páginas (lidas da própria navegação) e ações como alternar tema, exportar relatório, mostrar o overlay e testar a rede.

## Movimento

Apenas em resposta a uma ação: a página entra com 170 ms de opacidade e 230 ms de deslocamento curto, respeitando a opção "Mostrar animações no Windows" (`SystemParameters.ClientAreaAnimation`). Não há animação contínua: o app fica aberto durante jogos e benchmarks, e qualquer custo de CPU/GPU contaminaria a medição.

## Dados e honestidade

Gráficos e indicadores refletem só dados reais e ficam vazios ou com travessão quando não há amostra. Falta de sensor nunca aparece como zero. FPS estimado, resposta da API e FPS medido por ETW são identificados por texto. Sucesso parcial não usa mensagem de sucesso total.

## Verificação

- `tools/check_resources.py` cruza cada `{StaticResource}`/`{DynamicResource}` e cada chave usada em C# com as definidas no tema, respeitando a ordem de carga (o compilador de XAML não detecta chave inexistente).
- Compilação WPF, construção e navegação das 15 páginas, ida e volta de tema e regras de indisponibilidade estão no executor de testes (`tests/CoreBi.Tests`).
- Ainda pendente de revisão manual em Windows: navegação completa por teclado, leitor de tela, alto contraste e DPI em vários monitores.
