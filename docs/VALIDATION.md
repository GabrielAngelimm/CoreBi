# Validação 0.1.1

Revisão local em 23/10/2026, Windows x64, Ryzen 7 5700X, RTX 4060 Ti, 32 GiB e SSD XPG GAMMIX S70 BLADE. SDK 10.0.402 portátil; nenhuma alteração no SDK global ou no repositório original. Resultados são fotografias dessa execução, não garantias para todos os equipamentos.

## Comprovado por execução

| Grupo | Resultado | Alcance |
| --- | --- | --- |
| Regressões e WPF | 24/24 passaram | 8 regras originais, 15 cenários de robustez e composição/navegação das 15 páginas |
| Hardware administrativo | 4/4 passaram | Sessão ETW inicia/para; sistema/GPU, inventários, sensores/armazenamento reais |
| Inicialização WPF administrativa | 1/1 passou | Coleta, binding e timer no dispatcher real sem ativar uma janela em primeiro plano; ETW ocioso sem consumidor |
| Integrações administrativas | 4/4 passaram | winget --version, metadados NVIDIA, busca de drivers WU, ACL e arquivo temporário próprio na área protegida |
| Cargas reais reduzidas | 8/8 passaram | Disco, simulação CPU, RAM, carregamento, raster D3D11, DXR, início/parada de stress GPU e CPU |
| Build Release | Zero erros/avisos | Engine, Desktop e testes; avisos tratados como erros |
| NuGet | Sem avisos conhecidos retornados | Consulta incluindo transitivas à fonte NuGet em 23/10/2026; não certifica ausência de vulnerabilidades |
| Locks | Restauração bloqueada passou | Build normal e publicação single-file possuem locks próprios |

Total: **41 verificações funcionais/regressões distintas**, além de build, restauração, formato, auditorias e publicação. Os testes padrão são determinísticos e usam backends/HTTP isolados onde necessário; os outros modos executam APIs Windows e cargas reais explicitamente.

### Amostras observadas

- 299 linhas de sensores, 294 com valor; PawnIO instalado, CPU a 51,25 °C / 58,89 W em uma amostra. Não houve comparação externa de calibração nesta rodada.
- 22 entradas de inicialização e 91 drivers inventariados; somente leitura.
- SSD: 38 °C, 3% de desgaste, 97% de vida restante segundo os provedores disponíveis. O veredito não garante ausência de falhas.
- Disco 64 MiB: aproximadamente 2735/3139 MiB/s sequenciais e 98,5 mil IOPS de leitura 4 KiB com 8 workers. Execução curta, sujeita ao estado do sistema/cache do dispositivo; não é o preset do CrystalDiskMark.
- Shaders extraídos para recursos incorporados renderizaram frames reais: raster a 640 × 360 por 3 s e três passagens DXR de 1 s a 320 × 180. Stress GPU leve e CPU por 1 s iniciaram/pararam.
- winget respondeu com versão válida; NVIDIA retornou metadados/link oficial; Windows Update concluiu a busca com zero drivers disponíveis naquele momento. Isso não testa uma instalação.

## Falhas verificadas por testes isolados

Gravação JSON que falha preserva o arquivo anterior e elimina temporário; backup que falha impede remoção; restauração ignora campos editados e preserva backup após falha. Processos não travam com stdout/stderr cheios, mantêm argumentos literais e encerram o filho no cancelamento. Dados de sensores não finitos tornam-se indisponíveis; fechar durante a abertura libera recursos uma única vez. Políticas recusam domínios/publicadores parecidos com os oficiais. Resultados parciais do WU não viram sucesso completo. Cache de API valida números, separa credenciais e rejeita redirect. Modelos desconhecidos não geram FPS fictício; HTTP funcional não é rotulado offline só por ICMP bloqueado.

As operações de inicialização foram testadas com backend isolado e persistência real em pasta de teste. Nenhuma entrada real foi removida, restaurada ou ativada. ACL foi exercitada com administrador e apenas um arquivo próprio temporário.

## Limites ainda abertos

| Fluxo | Validação restante |
| --- | --- |
| Overlay/FPS em jogos | Comparação simultânea com contador confiável, modos de tela e APIs; ETW mede apresentações, não reconstrói todo o pipeline de frames exibidos |
| FPS estimado | Calibração das tabelas; contrato/autenticação HowManyFPS com chave real. HTTP simulado testa o cliente, não o serviço externo |
| Stress longo e presets completos | Estabilidade, repetibilidade, temperaturas e comparação com métodos compatíveis; a revisão executou cargas curtas |
| Inicialização real | Remover/restaurar entradas de teste controladas, falhas parciais, Registro/atalhos e reboot em ambiente dedicado |
| Instalações/backup de drivers | NVIDIA/AMD, WU com atualizações reais, pnputil, reboot, falha de instalador; consulta AMD não foi executada nesta GPU NVIDIA |
| Apps/PawnIO | Instalação, cancelamento e efeitos de instaladores reais; nenhum pacote foi instalado nesta revisão |
| Rede, bateria e USB | Sequência completa de tráfego, comparação externa, notebook e conexão/desconexão física |
| UI e distribuição | Foco/teclado, leitor de tela, alto contraste, DPI e outras máquinas. Construção XAML não é auditoria de acessibilidade |
| Arquitetura de apresentação | Ferramentas administrativas independentes; demais páginas conservam ponte com MainWindow, sem alegação de MVVM completo |

A CI Windows e os resultados do commit publicado ficam em [Actions](https://github.com/GabrielAngelimm/CoreBi/actions/workflows/verify.yml). A CI cobre build, 24 regressões/WPF, verificação do histórico e publicação; hardware/cargas reais exigem evidência local. Arquivos brutos podem conter caminhos e identificadores e não são incluídos no código público.
