# Overlay em jogos

## Dois caminhos de apresentação

O painel WPF existente continua atendendo o desktop e jogos em janela/sem bordas. Uma janela `Topmost` não resolve a tela cheia exclusiva: nesse modo o jogo controla a apresentação. As otimizações de tela cheia do Windows podem apresentar um jogo configurado como exclusivo por um caminho semelhante a sem bordas; a configuração do jogo, sozinha, não prova o modo real de apresentação.

O caminho opcional de tela cheia usa **RivaTuner Statistics Server (RTSS)**, o serviço de OSD usado pelo MSI Afterburner. O CoreBi publica texto formatado e o pequeno histórico de frametime no protocolo público `RTSSSharedMemoryV2`. O RTSS é responsável pelos hooks e pelo desenho dentro do jogo. O CoreBi não carrega suas DLLs, não implementa injeção própria, não instala drivers e não altera perfis, limitadores, stealth, detecção ou opções de anticheat do RTSS.

O Xbox Game Bar hospeda widgets UWP XAML em seu espaço de overlay. Adotá-lo exigiria outro aplicativo empacotado, ativação própria, comunicação com o processo WPF e outra implementação da apresentação. Não foi escolhido para esta integração enxuta. Também não é uma garantia de compatibilidade com todo modo exclusivo ou todo jogo.

Referências: [MSI: OSD e instalação do RTSS](https://us.msi.com/support/technical_details/VGA_MSI_Utility_AfterBurner), [Microsoft: fullscreen, DWM e overlays](https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/), [Microsoft: arquitetura dos widgets Game Bar](https://learn.microsoft.com/en-us/xbox/game-bar/overview). O contrato binário foi conferido em `SDK/Include/RTSSSharedMemory.h` e nos exemplos `SharedMemory`/`OverlayEditor` da instalação do RTSS. Nenhum código ou binário desses exemplos é redistribuído pelo CoreBi.

## Usar

1. Instale o RTSS pelo [site oficial Guru3D](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/) ou pelo pacote oficial do Afterburner. Use RTSS 7.3.7 ou posterior: as tags de fonte própria e preenchimento arredondado são recursos dessa versão. O aplicativo Afterburner não precisa permanecer aberto.
2. Abra o RTSS **antes de abrir o jogo**. Mantenha o OSD habilitado e um perfil de detecção permitido pelo próprio jogo. Não use stealth ou técnicas para contornar anticheat. O CoreBi não inicia nem configura esse processo automaticamente.
3. Ative o OverlayBi no CoreBi normalmente, ou pressione **Ctrl + Shift + O**. Ao reconhecer um processo em primeiro plano ocupando o monitor e ativo no RTSS, o CoreBi publica o OSD. A janela WPF só é escondida após observar o consumo de uma atualização pelo jogo.
4. O estado da detecção e sua dica informam o caminho RTSS. Desligar o OverlayBi remove somente seu slot; outros clientes, incluindo Afterburner, permanecem intactos.

Sem servidor, sem slot livre, com protocolo inválido, renderer sem resposta ou jogo não reconhecido, o caminho WPF continua disponível. Ele não passa a cobrir tela cheia exclusiva por causa desse fallback. Se o servidor estiver ausente/incompatível, novas tentativas ocorrem a cada cinco segundos. Ao voltar ao desktop, o slot é limpo e o painel WPF reaparece.

Os dados, cores, seleção de itens, nomes de hardware, canto, tamanho e opacidade vêm das mesmas configurações. O aplicativo e o painel WPF mantêm sua aparência. O caminho RTSS mede o próprio painel WPF e projeta cada texto e separador em posições fixas, em vez de reorganizar os campos com tabulações. A tag `FNT` fixa Bahnschrift SemiCondensed e zoom 1 somente nesse conteúdo: o zoom e a fonte do perfil não ampliam o cartão. O preenchimento arredondado usa o raio do `Border` original, acompanhado por um contorno fino recortado no mesmo raio. Não há bitmap, textura personalizada nem arquivo temporário por atualização. Rasterização, pesos tipográficos e antialiasing de curvas podem diferir entre WPF e RTSS; não se promete identidade pixel a pixel em todos os drivers/DPI. Nenhum perfil global é alterado.

## Compatibilidade e fontes

- O RTSS oferece OSD para DirectX, OpenGL e Vulkan, mas a compatibilidade real depende da versão do jogo, API, driver, modo de apresentação, outros overlays e anticheat. Não existe promessa de funcionar em qualquer jogo. Desative a detecção no perfil RTSS de jogos que a proíbem.
- A detecção de janela ocupando o monitor não distingue exclusivo real de sem bordas. Ela também permite o caminho RTSS em um jogo sem bordas reconhecido, evitando inferências frágeis sobre o modo exclusivo. Janelas menores mantêm WPF.
- O CoreBi mantém as métricas ETW existentes. Quando ETW não fornece FPS/frametime de um jogo reconhecido (por exemplo, certos caminhos OpenGL/Vulkan), usa as macros de FPS/frametime do próprio RTSS. `1% low` e histórico sem dados ETW ficam ausentes; não são fabricados.
- RAM, GPU e demais sensores continuam vindo da coleta compartilhada atual. Não se abre outro monitor de hardware por jogo.
- O slot OSD do RTSS é global para aplicações reconhecidas: se várias aplicações 3D estiverem abertas, outras podem renderizar a mesma informação do jogo em primeiro plano. O CoreBi publica somente enquanto uma janela em tela cheia reconhecida está em primeiro plano.
- Um contador de consumo OSD confirma atividade do renderer, não certifica que cada pixel está visível: configurações do perfil e outro overlay ainda podem ocultar conteúdo. O teste visual no jogo continua necessário.
- Após encerramento anormal do CoreBi, ou se o lock global permanecer ocupado durante seu fechamento, o slot pode ficar reservado. Reiniciar o RTSS limpa esses slots. O CoreBi nunca toma slots alheios nem força o desbloqueio global.
- O RTSS é uma dependência externa opcional, com distribuição/licença próprias. Não está incluído no instalador nem no código open source do CoreBi.

## Instalador e licença

O Afterburner oferece o instalador separado do RTSS como componente opcional. Isso não concede automaticamente ao CoreBi as mesmas permissões de distribuição. A licença presente no SDK instalado (`SDK/Localization/Installer/RTSS.lic`, cláusulas 1 e 3) restringe redistribuição, modificação, criação de outro instalador e uso em produtos sem permissão do autor. O [site oficial](https://www.guru3d.com/pages/) também informa a restrição de redistribuição.

Para um setup público, a opção conservadora é detectar uma instalação existente e oferecer a abertura da página oficial para instalação separada pelo usuário. Incluir ou baixar/executar o instalador como parte do setup exige esclarecer a autorização com o autor, inclusive o alcance da integração no produto. Depois da autorização, o componente deve ser opcional, preservar o instalador/EULA originais, verificar integridade/origem e nunca remover a instalação compartilhada do RTSS ao desinstalar o CoreBi. Nesta alteração o setup não foi modificado e nenhum pacote RTSS foi incluído.

## Custo e limites da validação

A publicação acompanha o timer existente de **500 ms (2 Hz)**, independentemente do FPS do jogo. O acesso ao lock é não bloqueante e a atualização ocupada é descartada. Não há worker, processo auxiliar, coleta de sensores adicional, captura de tela, bitmap ou escrita em disco por atualização. O gráfico transfere no máximo **120 amostras / 516 bytes**; o texto é limitado à capacidade de 4 ou 32 KiB do protocolo. Offsets, versão, tamanhos e limites são validados antes de acessar a memória.

CPU e alocações adicionais pertencem à formatação e à cópia desse conteúdo. A memória da seção RTSS é compartilhada, mapeada e liberada a cada operação; isso não equivale a reservar sua capacidade como RAM privada permanente do CoreBi. O RTSS tem seu próprio custo de RAM/CPU e desenha o OSD na GPU a cada frame do jogo. Esse custo varia com a máquina e com o perfil. Ausência de custo perceptível ou perda zero de FPS precisa ser medida no jogo; testes do transporte não certificam isso.

Medição local isolada com 500 atualizações, todos os itens selecionados, cartão arredondado e gráfico de 120 pontos: mediana de **0,7291 ms**, p95 de **1,0848 ms** e aproximadamente **74 KiB de alocações gerenciadas temporárias por atualização**. O texto desse cenário ocupou 3457 bytes, além dos 516 bytes do gráfico. A 2 Hz são aproximadamente 147 KiB/s de alocações, sujeitos a GC, não crescimento permanente de RAM. O ensaio inclui atualização/medição do painel, projeção, inspeção e publicação numa seção de teste; exclui coleta de sensores, ETW, o processo RTSS e o desenho/FPS do jogo. Esses tempos de parede não são uma medição de percentual de CPU do aplicativo completo.

As regressões padrão usam seções de memória isoladas: exercitam os offsets do SDK, slots de outros clientes, publicação/limpeza, versões de texto, servidor ausente, lock ocupado, ranges inválidos, processo inativo, seleção, caracteres não confiáveis e gráfico limitado. Não iniciam RTSS nem injetam código em jogos. Consulte [VALIDATION.md](VALIDATION.md) para o alcance realmente executado e os ensaios ainda necessários.
