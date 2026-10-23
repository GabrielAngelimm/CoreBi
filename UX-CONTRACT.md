# Contrato da interface

## Canonical UI Map

| Capability | Canonical owner | Source of truth | Allowed variants | Verification |
| --- | --- | --- | --- | --- |
| Table Selection | WPF DataGrid/ListBox e estilos compartilhados | src/CoreBi.Desktop/Shell/Theme.xaml | Seleção simples ou múltipla conforme operação original | tests/CoreBi.Tests/Program.cs constrói todas as páginas; teclado completo pendente |
| Select/Listbox | WPF ComboBox/ListBox e chips RadioButton | src/CoreBi.Desktop/Shell/Theme.xaml | Chips para poucas opções; lista para inventário | Construção e navegação WPF; revisão manual de foco pendente |
| Form | Controles WPF, handlers próprios em ferramentas administrativas, eventos por feature nas demais e validação do Engine | src/CoreBi.Desktop/Views e Features | PasswordBox para chave; campos de filtros e opções originais | Teste de persistência DPAPI e regras; instalações não executadas |
| Scrollbar | WPF ScrollViewer/ScrollBar com estilo herdado | src/CoreBi.Desktop/Shell/Theme.xaml | Rolagem da navegação e do conteúdo independentes | XAML compilado; múltiplos DPI pendentes |
| Toast | ToastWindow | src/CoreBi.Desktop/Shell/ToastWindow.xaml | Janela escura para eventos USB e falhas de persistência | Construção dos recursos; conexão física pendente |

Não há CRUD de registros de negócio ou seletor de datas. Operações administrativas são comandos específicos e não entram em um fluxo CRUD genérico. Não há HTML/select/date nativo do navegador; as exceções de plataforma são os controles WPF e diálogos Windows acima.

## Estados e consistência

- Dados ausentes: travessão, mensagem de causa ou estado desconhecido. Não preencher uma medição com zero fictício.
- Cargas pesadas: apenas uma operação ativa, incluindo sua preparação. Cancelar a operação correta, restaurar botões/monitoramento e limpar temporários.
- Inicialização/drivers/apps: preservar confirmação e resultado por operação; não alegar teste de instalação quando só houve inventário.
- Relatórios e configurações pertencem ao CoreBi. Nome original pode aparecer nos créditos legais e documentação de origem, sem marca na interface.
- FPS estimado local, resposta da API e apresentação ETW têm fontes distintas; não converter a estimativa em leitura medida.

## Preservação e pendências

O usuário pediu preservar a interface nesta etapa. Recursos de acessibilidade e detalhes visuais herdados não foram certificados só por compilar. A futura etapa de UI/UX deve revisar foco/atalhos, leitura por tecnologia assistiva, contraste, DPI e legibilidade, preservando os comandos reais do Engine.
