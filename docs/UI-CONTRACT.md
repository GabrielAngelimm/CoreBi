# Contrato da interface

## Canonical UI Map

| Capability | Canonical owner | Source of truth | Allowed variants | Verification |
| --- | --- | --- | --- | --- |
| Table Selection | WPF DataGrid/ListBox e estilos compartilhados | src/CoreBi.Desktop/Shell/Theme.xaml | Seleção simples ou múltipla conforme operação original | tests/CoreBi.Tests/Program.cs constrói todas as páginas; teclado completo pendente |
| Select/Listbox | WPF ListBox, controle segmentado, chips e ladrilhos (RadioButton) | src/CoreBi.Desktop/Shell/Theme.xaml | Segmentos para opções fixas; chips para listas geradas; ladrilhos para escolher o teste; lista para inventário e paleta de comandos | Construção e navegação WPF; revisão manual de foco pendente |
| Form | Controles WPF, handlers próprios em ferramentas administrativas, Disco, Energia e Rede, eventos por feature nas demais e validação do Engine | src/CoreBi.Desktop/Views e Features | PasswordBox para chave; campos de filtros e opções originais | Teste de persistência DPAPI e regras; instalações não executadas |
| Scrollbar | WPF ScrollViewer/ScrollBar com estilo do tema | src/CoreBi.Desktop/Shell/Theme.xaml | Rolagem da navegação e do conteúdo independentes | XAML compilado; múltiplos DPI pendentes |
| Toast | ToastWindow | src/CoreBi.Desktop/Shell/ToastWindow.xaml | Cartão de grafite fixo (independe do tema) para eventos USB e falhas de persistência | Construção dos recursos; conexão física pendente |
| Overlay | OverlayPanel e projeção RtssOverlayFormatter | src/CoreBi.Desktop/Features/Games/Overlay/OverlayPanel.xaml e respectivas medidas em execução | Janela WPF no desktop; camadas nativas RTSS em jogos compatíveis, preservando posição/tamanho dos campos, cores, seleção e raio do cartão, sem alterar perfis globais | OverlayTests e RtssTests; apresentação final confirmada pelo usuário no For Honor, com pequeno deslocamento dos separadores aceito |

Operações administrativas usam comandos específicos e diálogos Windows. Seleção, rolagem e foco seguem os controles WPF nativos.

## Estados e consistência

- Dados ausentes: travessão, mensagem de causa ou estado desconhecido. Não preencher uma medição com zero fictício.
- Rede: Cloudflare é a opção inicial; se a medição ficar incompleta, o M-Lab é tentado automaticamente. A origem acompanha o resultado, e a espera da Cloudflare não bloqueia o M-Lab. Nesta versão pessoal não há confirmação na tela; antes de distribuir, obter consentimento informado conforme as regras do M-Lab.
- Cargas pesadas: apenas uma operação ativa, incluindo sua preparação. Cancelar a operação correta, restaurar botões/monitoramento e limpar temporários.
- Inicialização/drivers/apps: preservar confirmação e resultado por operação; não alegar teste de instalação quando só houve inventário.
- Relatórios e configurações pertencem ao CoreBi. Nome original pode aparecer nos créditos legais e documentação de origem, sem marca na interface.
- FPS estimado local, resposta da API e apresentação ETW têm fontes distintas; não converter a estimativa em leitura medida.

- Botões de iniciar e parar trocam de papel no mesmo lugar (`Ui.SetRunning`): cor, glifo e texto mudam juntos.
- Veredito usa somente `Ok`, `Warn` e `Danger`, e saudável é discreto (LED verde e texto em tinta). O que se clica acende no sinal verde-água (`Signal`: ação principal, item escolhido da navegação, controles ligados, foco), e cada componente de hardware tem a sua cor (`Cpu`, `Gpu`, `Memory`, `Virtual`, `Storage`, `Board`, `Net`), nunca usada em botão, aba ou ícone de ação.
- Ícone de componente é só o desenho (`HwIcon` com `ChannelIcon`), grande e sem fundo, na cor da peça; a interface não usa degradê, brilho nem ladrilho colorido.

## Reformulação e pendências

A interface foi redesenhada (direção "Bancada", descrita em [DESIGN.md](DESIGN.md)) mantendo nomes de elementos, handlers e fluxos usados pelo código e pelos testes. Compilar não certifica acessibilidade: falta revisar em Windows a navegação completa por teclado (a paleta de comandos Ctrl + K cobre a navegação entre páginas), leitor de tela, alto contraste e DPI em vários monitores.
