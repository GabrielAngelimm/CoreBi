# Dados locais e conexões

CoreBi não possui conta própria, servidor próprio de coleta ou envio automático de relatórios/logs. Ações de rede usam os provedores abaixo conforme a ferramenta.

| Uso | Destino e dados necessários |
| --- | --- |
| Drivers | NVIDIA/AMD: modelo/identificadores de busca, versão e metadados de download; suporte da placa-mãe abre o site do fabricante no navegador (para Gigabyte, uma busca no google.com restrita ao site dela) |
| Windows Update | Serviço e políticas do Windows, via API COM; busca e instalação solicitadas na interface |
| Instalar apps / PawnIO | winget e suas fontes/instaladores; identificador do pacote escolhido |
| Teste de rede | Cloudflare e, se necessário, M-Lab: tráfego de teste de download/upload; DNS/ICMP aos destinos definidos no código e gateway |
| Links de ajuda | Navegador padrão abre o endereço selecionado |

Conexões expõem IP e metadados normais de HTTP ao provedor. Provedores externos seguem suas próprias políticas e disponibilidade. O teste de rede consome banda; execute-o de forma consciente em conexões limitadas.

Configurações e históricos ficam em `%AppData%\CoreBi`. Diagnósticos locais ficam na subpasta `Logs`, com dois arquivos de aproximadamente 1 MiB. O override `COREBI_DATA_DIR` existe para testes/desenvolvimento.

Quando a Cloudflare pede uma pausa no teste de rede, `network-speed-cooldown.json` guarda somente o horário até o qual aguardar. Ele não registra IP, velocidade ou histórico; o prazo é observado mesmo após reiniciar o aplicativo.

O M-Lab é usado automaticamente quando a Cloudflare não conclui a medição, sem pergunta na tela. A [política de privacidade do M-Lab](https://www.measurementlab.net/privacy/) informa que o IP público e os resultados são publicados e mantidos por prazo indefinido; a [política de uso](https://www.measurementlab.net/aup/) também se aplica. Essa troca automática é uma decisão do mantenedor para a 1.0, que é distribuída só entre ele e pessoas próximas, que sabem como o teste funciona. O motor já tem a trava (`NetworkTest.Run(..., mlabConsent)`): se o CoreBi passar a ser distribuído para quem não conhece o projeto, a página de Rede deve pedir o consentimento antes da primeira troca. Outros dados do CoreBi e relatórios não são enviados ao M-Lab.

`%ProgramData%\CoreBi\<SID>` guarda downloads administrativos e registros/arquivos necessários para restaurar inicialização. Remover essa pasta elimina esses backups. O backup de drivers vai, por padrão, para `Documentos\CoreBi-Drivers`, ou para a pasta escolhida na hora.

Relatórios exportados deixam de fora números de série e o nome do usuário, mas contêm o nome do computador, identificadores de hardware, dados da rede e detalhes das peças. A exportação é uma ação local do usuário; revise o arquivo antes de compartilhar. Desinstalar/excluir o executável não apaga automaticamente dados ou backups.
