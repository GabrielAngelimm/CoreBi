# Validação

## Revisão de organização

Verificações locais em 23/10/2026, Windows x64, SDK 10.0.402, Ryzen 7 5700X, RTX 4060 Ti, 32 GiB e SSD XPG GAMMIX S70 BLADE.

- As 24 regressões passaram, incluindo construção/navegação das 15 páginas, alternância de tema e exclusão entre cargas.
- A página de Rede recusou iniciar enquanto outra carga detinha o coordenador. Após fechar a janela, Disco/Energia/Rede recusaram novas coletas sem alterar os controles.
- O carregamento WPF real passou com administrador: 299 sensores, uma GPU, um cartão de disco e um de energia. Disco e Energia preencheram seus próprios controles usando o coletor compartilhado.
- O executor WPF usa os recursos reais da interface com um ciclo de aplicação isolado, sem disputar a instância única nem abrir outra janela do produto. A verificação passou com o CoreBi normal aberto.
- O build Release passou sem erros/avisos. Links relativos dos 12 documentos do projeto foram conferidos; avisos/licenças de terceiros permanecem preservados.

Os algoritmos de coleta e medição não foram alterados nesta revisão. As cargas gráficas/benchmarks e instalações externas não foram repetidas apenas por mudança de localização de arquivos. A compilação, as regressões e o carregamento real cobrem os fluxos reorganizados.

## Referência da versão 0.1.1

A [versão publicada](https://github.com/GabrielAngelimm/CoreBi/releases/tag/v0.1.1), commit `71d7cc9`, passou por **41 verificações distintas** na mesma máquina:

| Grupo | Resultado | Alcance |
| --- | --- | --- |
| Regressões e WPF | 24/24 | Regras, falhas e composição/navegação das páginas |
| Hardware administrativo | 4/4 | Sessão ETW inicia/para, inventário, sensores e armazenamento |
| Inicialização WPF | 1/1 | Coleta, binding e timer; ETW ocioso sem consumidor |
| Integrações | 4/4 | winget --version, metadados NVIDIA, busca de drivers WU, ACL e arquivo temporário próprio |
| Cargas reais reduzidas | 8/8 | Disco, simulação CPU, RAM, carregamento, D3D11, DXR e início/parada do stress GPU/CPU |

Os testes de disco usaram 64 MiB; raster usou 640 × 360 por 3 s; DXR, três passagens de 1 s a 320 × 180. São verificações funcionais, sem equivalência aos presets completos de outros utilitários. A consulta NuGet com transitivas não retornou vulnerabilidades conhecidas em 23/10/2026. O executável self-contained abriu e respondeu; seus ZIPs foram publicados com SHA256.

A [CI daquela versão](https://github.com/GabrielAngelimm/CoreBi/actions/runs/39909144675) passou. O estado do código atual está no [workflow Verify](https://github.com/GabrielAngelimm/CoreBi/actions/workflows/verify.yml), que repete varredura do histórico, restore bloqueado, formato, build, 24 regressões/WPF e publicação Windows.

## Falhas cobertas e limites

As regressões verificam falha de gravação/serialização, backup antes da remoção, restauração canônica e conflitos, cancelamento e saída de processos, vida útil dos sensores, dados não finitos, política de domínio/publicador, parcialidade do Windows Update, cache/API e hardware sem referência. Operações de inicialização usam backend isolado e persistência real de teste; a ACL foi testada com administrador e arquivo próprio temporário.

| Área | Validação ainda necessária |
| --- | --- |
| Overlay e estimativas | Comparação de FPS em jogos, APIs/modos de tela e calibração das tabelas; HowManyFPS com contrato/chave reais |
| Stress e benchmarks | Sessões longas, presets completos, repetibilidade e outros equipamentos |
| Inicialização | Remover/restaurar entradas de teste controladas, falhas parciais e reboot em ambiente dedicado |
| Drivers/apps/PawnIO | Instalações reais NVIDIA/AMD/WU/winget, backup pnputil, cancelamento, falhas e reboot |
| Rede, bateria e USB | Tráfego completo/comparação externa, notebook e conexão/desconexão física |
| Interface | Teclado, leitor de tela, alto contraste, DPI e outras máquinas; construção XAML não certifica acessibilidade |

Nenhuma entrada pessoal de inicialização foi modificada e nenhum driver/app foi instalado pelos testes. O repositório original permaneceu intacto. Resultados brutos locais podem conter caminhos/identificadores e não são incluídos no código público. Os mecanismos e limites de cada ferramenta estão em [FEATURES.md](FEATURES.md).
