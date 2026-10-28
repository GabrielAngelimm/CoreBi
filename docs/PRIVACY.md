# Dados locais e conexões

CoreBi não possui conta própria, servidor próprio de coleta ou envio automático de relatórios/logs. Ações de rede usam os provedores abaixo conforme a ferramenta.

| Uso | Destino e dados necessários |
| --- | --- |
| Drivers | NVIDIA/AMD: modelo/identificadores de busca, versão e metadados de download; suporte da placa-mãe abre o site no navegador |
| Windows Update | Serviço e políticas do Windows, via API COM; busca e instalação solicitadas na interface |
| Instalar apps / PawnIO | winget e suas fontes/instaladores; identificador do pacote escolhido |
| Teste de rede | Cloudflare e, se necessário, M-Lab: tráfego de teste de download/upload; DNS/ICMP aos destinos definidos no código e gateway |
| API opcional de FPS | HowManyFPS: chave do usuário e parâmetros de hardware/jogo/preset; contrato externo ainda não validado com chave real |
| Links de ajuda | Navegador padrão abre o endereço selecionado |

Conexões expõem IP e metadados normais de HTTP ao provedor. Provedores externos seguem suas próprias políticas e disponibilidade. O teste de rede consome banda; execute-o de forma consciente em conexões limitadas.

Configurações, históricos e cache ficam em `%AppData%\CoreBi`. A chave usa DPAPI; o cache não grava a chave em texto e tem expiração/limites. Diagnósticos locais ficam na subpasta `Logs`, com dois arquivos de aproximadamente 1 MiB. O override `COREBI_DATA_DIR` existe para testes/desenvolvimento.

Quando a Cloudflare pede uma pausa no teste de rede, `network-speed-cooldown.json` guarda somente o horário até o qual aguardar. Ele não registra IP, velocidade ou histórico; o prazo é observado mesmo após reiniciar o aplicativo.

O M-Lab pode ser usado automaticamente quando a Cloudflare falha. A [política de privacidade do M-Lab](https://www.measurementlab.net/privacy/) informa que o IP público e os resultados são publicados e mantidos por prazo indefinido; a [política de uso](https://www.measurementlab.net/aup/) também se aplica. Esta versão pessoal não solicita consentimento na interface: o proprietário o autorizou para seu teste; para distribuição, é necessário obter o consentimento informado de cada usuário, conforme as regras do serviço. Outros dados do CoreBi e relatórios não são enviados ao M-Lab.

`%ProgramData%\CoreBi\<SID>` guarda downloads administrativos e registros/arquivos necessários para restaurar inicialização. Remover essa pasta elimina esses backups. Dados do programa original são separados.

Relatórios exportados podem conter nome de computador, serial, IDs, rede e detalhes de hardware. Sua exportação é uma ação local do usuário; revise e oculte identificadores antes de compartilhar. Desinstalar/excluir o executável não apaga automaticamente dados ou backups.
