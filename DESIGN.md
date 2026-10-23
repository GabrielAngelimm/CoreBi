---
version: alpha
name: CoreBi
description: Diagnóstico de hardware com marca própria e interface WPF preservada nesta etapa.
colors:
  primary: "#F5F5F5"
  brand-graphite: "#111B24"
  brand-signal: "#27C9BC"
  brand-paper: "#EDF4F4"
  brand-steel: "#8295A3"
  brand-ink: "#172E3A"
  brand-signal-light: "#087E78"
  background: "#000000"
  panel: "#080808"
  card: "#0E0E0E"
  foreground: "#F5F5F5"
  muted: "#8C8C8C"
  border: "#1F1F1F"
typography:
  sans:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
  mono:
    fontFamily: "Cascadia Mono, Consolas"
  brand:
    fontFamily: "Bahnschrift, Segoe UI"
rounded:
  DEFAULT: "10px"
  card: "16px"
  sidebar: "20px"
spacing:
  card-padding: "20px"
  title-height: "44px"
  sidebar-width: "256px"
components:
  button:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.background}"
  card:
    backgroundColor: "{colors.card}"
    textColor: "{colors.foreground}"
  divider:
    backgroundColor: "{colors.border}"
  navigation:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.muted}"
  table:
    backgroundColor: "{colors.card}"
    textColor: "{colors.foreground}"
  dialog:
    textColor: "{colors.foreground}"
  toast:
    backgroundColor: "{colors.background}"
  brand-dark:
    backgroundColor: "{colors.brand-graphite}"
    textColor: "{colors.brand-paper}"
  brand-signal:
    textColor: "{colors.brand-signal}"
  brand-caption:
    textColor: "{colors.brand-steel}"
  brand-light:
    backgroundColor: "{colors.brand-paper}"
    textColor: "{colors.brand-ink}"
  brand-signal-light:
    textColor: "{colors.brand-signal-light}"
---

# CoreBi Design System

## Overview

Um instrumento de bancada para ler o hardware com clareza: informação técnica densa, navegação previsível e marca geométrica discreta. Público inicial: o autor e amigos, em PCs Windows, conforme o pedido do projeto. Conteúdo em português brasileiro; modelos de hardware, APIs e unidades mantêm seus nomes técnicos.

A geometria da marca está em `brand/geometry.json`: C e B têm a mesma altura e centro; ponto e haste formam o i centralizado.

**Escopo atual:** preservar layout e componentes. Uma reformulação de UI/UX será tratada separadamente, sem alterar as regras das ferramentas.

## Colors

A marca combina graphite `#111B24`, signal `#27C9BC`, paper `#EDF4F4` e steel `#8295A3`. A variante clara usa ink `#172E3A` e signal-light `#087E78` para manter o símbolo legível. Acento representa leitura/sinal, não estado de saúde.

A interface atual permanece monocromática: fundo `#000000`, painel `#080808`, cartão `#0E0E0E`, texto `#F5F5F5`, secundário `#8C8C8C` e borda `#1F1F1F`. O modo claro troca os recursos em tempo de execução. Sobreposições e notificações conservam o fundo escuro.

**Fonte canônica:** `brand/geometry.json` para a marca; `src/CoreBi.Desktop/Shell/Theme.xaml` e `ThemeManager.cs` para a interface. Este documento descreve esses valores; não gera tokens. Valores em pixels correspondem a unidades independentes de dispositivo WPF. O build verifica a resolução dos recursos; o roundtrip de tema é exercitado em `tests/CoreBi.Tests/Program.cs`.

## Typography

Segoe UI Variable Text/Segoe UI para leitura e controles. Cascadia Mono/Consolas para métricas. Bahnschrift apenas para a assinatura CoreBi e sua prancha de marca; fonte não é redistribuída. SVGs de wordmark usam texto com fallback; o símbolo é inteiramente vetorial e independente de fonte. A versão PNG/ICO fixa a aparência.

Corpo atual de 13 unidades, títulos de 26, métricas de 22. Preservar nomes técnicos e unidades explícitas. FPS estimado, medido e indisponível devem ser distinguíveis por texto.

## Layout

Janela de referência 1320 × 840, mínimo 1120 × 660. Barra de título de 44 e lateral de 256, com rolagem própria. As 15 páginas são UserControls. Navegação agrupada em Monitoramento, Jogos, Desempenho e Ferramentas; Visão geral permanece acessível diretamente. Cartões mantêm padding de 20.

Operações assíncronas usam o estado/progresso existente; a dimensão de botões e do conteúdo não deve depender do tamanho de uma mensagem de progresso. Páginas não precisam reiniciar o coletor de sensores. A reforma de densidade/espaçamento para telas menores fica para UI/UX.

## Elevation & Depth

Hierarquia por tons, bordas de 1 unidade e tipografia, sem efeitos tridimensionais na marca. O desenho do ícone é plano. Janela e cartões preservam o tratamento original; nenhuma camada decorativa foi adicionada.

## Shapes

Marca em malha 256 × 256; C e B têm limites verticais 35–221. O i tem ponto quadrado e haste retangular, com centro vertical do conjunto em 128. Usar a geometria canônica para variantes. Não esticar um eixo do símbolo isoladamente.

Interface: botões com raio 10, cartões 16 e lateral 20, conforme recursos existentes. Evitar novos raios arbitrários nesta fase.

## Components

Botões, chips, grupos e listas reutilizam `Theme.xaml`. Templates de dados compartilhados usam `FeatureTemplates.xaml`. Seleção, desabilitado, hover e pressionado mantêm os estilos atuais. A verificação automatizada cobre construção e navegação, mas não certifica toda a acessibilidade dos templates herdados.

Diálogos de arquivo e mensagens são nativos WPF/Windows. Lista/tabela usa DataGrid/ListBox; seleção é do controle. Campos numéricos são WPF, sem reconstruir controles de navegador. Datas são informações de leitura, não há seletor de calendário.

Toast é `ToastWindow`; overlay é uma janela própria e independente da navegação. Ícones de ações usam Segoe Fluent Icons/Segoe MDL2 Assets; ações de janela têm tooltip. A marca não substitui rótulos funcionais.

Não adicionar animação nesta etapa. Gráficos e indicadores devem refletir dados reais e ficar vazios/indisponíveis quando não houver amostra. Falta de sensores não pode parecer um valor zero válido. Sucesso parcial não deve usar uma mensagem de sucesso total.

## Verification

Compilação WPF, teste de todas as páginas, ida/volta de tema, regras de indisponibilidade e cargas curtas estão no executor de testes. Evidências e limites em `docs/VALIDATION.md`.

O auditor estático premium analisa contratos e fontes web; não interpreta XAML. Seu resultado não certifica contraste, foco, leitor de tela ou interação real WPF. Revisão de teclado, leitores de tela, alto contraste e DPI em vários monitores continua pendente para a etapa de UI/UX.

## Maintenance boundary

As páginas administrativas, Disco, Energia e Rede são donas de seus handlers e recebem FeatureContext; as demais conservam a ponte tipada com MainWindow. A organização da apresentação está documentada em [arquitetura](docs/ARCHITECTURE.md). Falha de persistência usa a notificação existente; sucesso parcial, hardware desconhecido e indisponibilidade são expressos em texto.
