# CoreBi

[![Verify](https://github.com/GabrielAngelimm/CoreBi/actions/workflows/verify.yml/badge.svg)](https://github.com/GabrielAngelimm/CoreBi/actions/workflows/verify.yml)

Aplicativo open source de monitoramento, diagnóstico e testes de hardware para Windows x64. Reúne informações do sistema, sensores, benchmarks e utilitários em uma aplicação local.

Desenvolvido em C#/.NET 10 e WPF. As regras, os coletores e as integrações são separados da interface.

## Funcionalidades

| Seção | Ferramentas |
| --- | --- |
| Visão geral | Resumo do computador, histórico de sensores e alertas |
| Monitoramento | Sistema, sensores, placa de vídeo, discos, energia e rede |
| Jogos | FPS estimado, análise de gargalo e overlay com ETW e integração opcional RTSS para tela cheia |
| Desempenho | Benchmarks de CPU, GPU, DXR, simulação, RAM, carregamento e disco; stress de CPU, RAM e GPU |
| Ferramentas | Inicialização, drivers e instalação de apps via winget |

As ferramentas de monitoramento e benchmark usam leituras ou cargas reais. FPS estimado e classificação de gargalo são aproximações. Consulte [implementação, fontes e limites](docs/FEATURES.md).

Para overlay em tela cheia exclusiva, abra o RTSS antes do jogo e ative o OverlayBi normalmente. O RTSS é instalado separadamente; compatibilidade depende do jogo e anticheat. Veja [configuração, arquitetura e custo do overlay](docs/OVERLAY.md).

## Tecnologias

| Tecnologia | Uso |
| --- | --- |
| C# / .NET 10 / WPF | Aplicação desktop, operações assíncronas e apresentação |
| LibreHardwareMonitor | Leitura de sensores e hardware suportado |
| WMI, APIs Windows, DXGI e NVML | Inventário, armazenamento, energia e informações da GPU |
| TraceEvent / ETW | Eventos de apresentação para métricas de FPS e overlay |
| Vortice, D3D11, D3D12/DXR e HLSL | Benchmarks e cargas gráficas |
| winget, Windows Update e pnputil | Instalação de apps e ferramentas de drivers |

## Instalar e executar

Baixe `CoreBi-*-win-x64-setup.exe` em [Releases](https://github.com/GabrielAngelimm/CoreBi/releases) e execute o instalador. Ele instala em `Program Files`, cria atalhos e registra o CoreBi para desinstalação. O driver de sensores PawnIO vem marcado e o RTSS (overlay em tela cheia) vem desmarcado; ambos são instalados pelo winget, com uma explicação na tela de componentes, e nenhum é incluído no setup. O runtime .NET está incluído; o SDK é necessário apenas para desenvolvimento.

Sem instalador, extraia **todo o conteúdo** de `CoreBi-*-win-x64.zip` e abra `CoreBi.exe`.

O aplicativo solicita administrador para sensores, ETW e ferramentas administrativas. Sensores internos de CPU dependem de PawnIO e do suporte do LibreHardwareMonitor ao equipamento. Pela versão zip, instalar esse driver é uma ação explícita na interface. Pelo instalador, ele é uma escolha feita durante a instalação.

## Configuração

- Configurações, históricos e cache: `%AppData%\CoreBi`.
- Downloads administrativos e backups de inicialização: `%ProgramData%\CoreBi\<SID>`. Preserve essa pasta para restaurar entradas removidas.
- API opcional de FPS: credencial própria configurada na página da ferramenta e protegida por DPAPI. A integração ainda requer validação com uma chave real.

As pastas são criadas automaticamente. Consulte [privacidade](docs/PRIVACY.md) e [segurança](docs/SECURITY.md) para detalhes de armazenamento, conexões e permissões.

## Compilar a partir do código

Requisitos: Windows x64, Git e SDK indicado em `global.json`. Na pasta em que deseja clonar:

```powershell
git clone https://github.com/GabrielAngelimm/CoreBi.git
cd CoreBi
dotnet restore CoreBi.slnx --locked-mode
.\tools\Build.ps1
```

Para executar o build, abra o PowerShell como administrador na raiz do projeto:

```powershell
dotnet run --project src/CoreBi.Desktop -c Release --no-build
```

Para gerar a aplicação independente do runtime instalado:

```powershell
.\tools\Publish.ps1
```

O pacote é gerado em `artifacts/publish/win-x64`, incluindo as licenças. Para usar SDK portátil nos scripts, defina `$env:COREBI_DOTNET` com o caminho do `dotnet.exe`.

## Estrutura

```text
src/CoreBi.Engine/       Coletores, regras, benchmarks, integrações e persistência
src/CoreBi.Desktop/      Shell WPF, páginas, apresentação e serviços da interface
tests/CoreBi.Tests/      Regressões, WPF e verificações opcionais de hardware
tools/                   Compilação, testes, publicação e manutenção
docs/                    Guias, arquitetura, manutenção, segurança e validação
licenses/                Licenças e avisos dos componentes distribuídos
```

## Verificação e manutenção

```powershell
.\tools\Test.ps1
```

O modo padrão executa 24 regressões e verificações WPF. Os modos `Hardware`, `Startup`, `Integrations` e `Bench` são optativos; veja os comandos, o alcance e os cuidados em [CONTRIBUTING.md](docs/CONTRIBUTING.md).

- [Arquitetura e responsabilidades](docs/ARCHITECTURE.md)
- [Validações realizadas e pendências](docs/VALIDATION.md)
- [Histórico de mudanças](docs/CHANGELOG.md)

A versão distribuída é uma prévia técnica. Instalações administrativas, precisão do FPS em jogos, stress prolongado e compatibilidade em outros equipamentos ainda exigem validações específicas.
