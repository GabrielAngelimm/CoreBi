<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="brand/corebi-symbol-dark.svg">
    <img src="brand/corebi-symbol-light.svg" alt="" width="96" height="96">
  </picture>
</p>

<h1 align="center">CoreBi</h1>

<p align="center">
  Monitoramento, diagnóstico e testes de hardware para Windows, num app só e sem conta.
</p>

<p align="center">
  <a href="https://github.com/GabrielAngelimm/CoreBi/releases/latest"><img src="https://img.shields.io/badge/vers%C3%A3o-1.0.0-27C9BC" alt="Versão 1.0.0"></a>
  <a href="https://github.com/GabrielAngelimm/CoreBi/actions/workflows/verify.yml"><img src="https://github.com/GabrielAngelimm/CoreBi/actions/workflows/verify.yml/badge.svg" alt="Verify"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/licen%C3%A7a-MIT-informational" alt="Licença MIT"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-informational" alt="Windows 10 e 11 x64">
</p>

O CoreBi junta num lugar o que normalmente pede vários programas: a ficha do computador, os sensores ao vivo, a saúde dos discos, o consumo, a rede, um overlay de FPS para jogos, testes de carga e benchmarks, além de ferramentas para instalar apps, cuidar dos drivers e da inicialização do Windows. Tudo roda no próprio PC, sem servidor do CoreBi, e as leituras mostram só o que o hardware realmente informa: o que falta aparece como falta, nunca como zero.

Feito em C# com .NET 10 e WPF. As regras e os coletores ficam separados da interface.

## Funcionalidades

As páginas seguem os grupos da barra lateral.

| Grupo | Páginas | O que oferecem |
| --- | --- | --- |
| No topo | Visão geral | Resumo do PC: uso das peças, saúde do sistema, últimos testes e dispositivos conectados |
| Monitoramento | Ficha técnica, Sensores, Placa de vídeo, Armazenamento, Energia, Rede | Especificações completas com relatório para exportar (HTML, PDF ou JSON), todos os sensores com mínimo e máximo, a placa de vídeo ao vivo, vida útil e SMART dos discos, consumo da sessão e plano de energia, latência, perda e velocidade da conexão |
| Jogos e desempenho | OverlayBi, Teste de carga, Benchmark, Gargalo | Overlay de FPS e sensores por cima do jogo (também em tela cheia exclusiva, com o RTSS), testes de estabilidade de CPU, RAM e GPU com corte por temperatura, benchmarks de CPU, GPU, ray tracing, memória e disco com histórico, e a leitura de quem limita o FPS durante o jogo |
| Ferramentas | Instalar apps, Drivers, Inicialização | Catálogo de 73 apps instalados pelo winget em pacote, drivers da placa de vídeo e do Windows Update com backup e restauração, e os programas que abrem com o Windows, com backup antes de remover |

Dois atalhos valem para o app todo: **Ctrl + K** abre a paleta de comandos, que leva a qualquer página e às ações mais usadas, e **Ctrl + Shift + O** liga ou desliga o OverlayBi, inclusive com o jogo em primeiro plano. O tema claro ou escuro fica na barra lateral.

Os benchmarks e o teste de carga fazem trabalho real, com métodos próprios: os números não são comparáveis um a um com os de outros programas. O Gargalo combina leituras reais em regras e é uma estimativa. Os detalhes de cada ferramenta, as fontes de dados e os limites estão em [docs/FEATURES.md](docs/FEATURES.md).

## Requisitos

- Windows 10 ou 11, 64 bits.
- Permissão de administrador: sensores, eventos de FPS e as ferramentas de sistema precisam dela.
- Opcional, e recomendado:
  - **PawnIO**, o driver que lê temperatura, consumo e clock internos do processador. Sem ele, esses sensores ficam indisponíveis e o resto funciona.
  - **RTSS** (RivaTuner Statistics Server), para o OverlayBi aparecer em jogos em tela cheia exclusiva. Sem ele, o overlay funciona em janela e sem bordas.

O runtime do .NET vem junto; não é preciso instalar nada da Microsoft.

## Instalar

Cada [release](https://github.com/GabrielAngelimm/CoreBi/releases/latest) traz três arquivos:

| Arquivo | Para quê |
| --- | --- |
| `CoreBi-1.0.0-win-x64-setup.exe` | Instalador, o caminho recomendado |
| `CoreBi-1.0.0-win-x64-portable.zip` | Versão portátil, sem instalar |
| `SHA256SUMS.txt` | O SHA256 dos dois, para conferir o download |

1. Baixe o instalador e confira o arquivo com o `SHA256SUMS.txt`:

   ```powershell
   Get-FileHash .\CoreBi-1.0.0-win-x64-setup.exe -Algorithm SHA256
   ```

2. Execute o instalador. Ele segue o tema claro ou escuro do Windows, instala em `Program Files`, cria os atalhos e registra o CoreBi para desinstalação. Na tela de componentes, PawnIO e RTSS vêm marcados: cada um é baixado pelo winget do repositório oficial, com a licença do próprio fornecedor, e nenhum vem dentro do instalador. Desmarque o que não quiser.

O instalador não tem assinatura digital, então o Windows pode mostrar "O Windows protegeu o computador". Depois de conferir o SHA256, use **Mais informações** e **Executar assim mesmo**.

**Portátil:** extraia o zip inteiro numa pasta e abra `CoreBi\CoreBi.exe`. O executável precisa das DLLs, da pasta `amd64` e da pasta `licenses` que vêm ao lado dele. Nessa forma, o PawnIO é instalado pelo próprio app, em **Corrigir**, no pé do menu.

**Atualizar:** rode o instalador da versão nova por cima; configurações, históricos e backups continuam onde estão.

**Desinstalar:** pelo Windows, em Aplicativos instalados. O desinstalador pergunta se também apaga as configurações, o histórico e os backups de inicialização; o padrão é manter, porque esses backups são o que permite restaurar um programa removido da inicialização. PawnIO e RTSS ficam instalados, porque outros programas podem usá-los.

## Dados e privacidade

O CoreBi não tem conta, não envia relatórios nem estatísticas de uso e não tem servidor próprio. Ele só usa a rede quando você pede algo que depende dela: buscar drivers nos sites da NVIDIA, da AMD ou no Windows Update, instalar apps pelo winget e testar a conexão em servidores públicos de medição de velocidade.

| O quê | Onde fica |
| --- | --- |
| Configurações, históricos e logs | `%AppData%\CoreBi` |
| Downloads administrativos e backups de inicialização | `%ProgramData%\CoreBi\<SID>` (preserve para poder restaurar) |
| Backup de drivers | `Documentos\CoreBi-Drivers`, ou a pasta escolhida na hora |

O relatório exportado deixa de fora números de série e o nome do usuário, mas leva o nome do computador e detalhes das peças: revise antes de compartilhar. Os detalhes estão em [privacidade](docs/PRIVACY.md) e [segurança](docs/SECURITY.md).

## Compilar a partir do código

Requisitos: Windows 10 ou 11 x64, Git e Python 3 (para as verificações). Para gerar o instalador, também o [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
git clone https://github.com/GabrielAngelimm/CoreBi.git
cd CoreBi
.\tools\Build.ps1
```

Se o PowerShell recusar os scripts, libere só a sessão atual com `Set-ExecutionPolicy -Scope Process Bypass`.

O `Build.ps1` restaura os pacotes em modo bloqueado e compila. Os scripts de `tools/` usam o SDK exato do `global.json`: o do sistema, se for essa versão, ou uma cópia baixada na primeira vez para `%LOCALAPPDATA%\CoreBiDev\<versão>` pelo script oficial da Microsoft, sem instalador nem UAC. Para outros comandos, use `.\tools\Dotnet.ps1` no lugar de `dotnet`. Para usar outro SDK portátil, defina `$env:COREBI_DOTNET` com o caminho do `dotnet.exe`.

Para abrir o build, num PowerShell como administrador, na raiz do projeto:

```powershell
.\tools\Dotnet.ps1 run --project src/CoreBi.Desktop -c Release --no-build
```

Para gerar o pacote e o instalador:

```powershell
.\tools\Publish.ps1      # artifacts/publish/win-x64, com as licenças
.\tools\Installer.ps1    # artifacts/installer/CoreBi-<versão>-win-x64-setup.exe e o SHA256
```

As releases oficiais saem do workflow **Release** do GitHub Actions, que compila, testa e gera os três arquivos numa máquina limpa; os passos estão no [guia de contribuição](docs/CONTRIBUTING.md#versão-e-release).

## Testes

```powershell
.\tools\Test.ps1
```

A suíte padrão roda sem internet e sem hardware específico: regras do Engine, segurança, rede contra servidores locais, RTSS em memória isolada e a construção e navegação das 14 páginas. Os modos `Hardware`, `Startup`, `Integrations`, `Bench` e `OverlayTelemetry` usam o equipamento de verdade e são opcionais; os comandos e os cuidados estão em [CONTRIBUTING.md](docs/CONTRIBUTING.md). A CI do GitHub roda a suíte padrão, as capturas das páginas, a publicação, o instalador e as capturas das telas do instalador a cada push.

## Estrutura

```text
src/CoreBi.Engine/       Coletores, regras, benchmarks, integrações e persistência (sem WPF)
src/CoreBi.Desktop/      Janela, páginas, controles e serviços da interface (WPF)
tests/CoreBi.Tests/      Regressões, interface e verificações opcionais de hardware
tools/                   Compilação, testes, publicação, licenças e manutenção
installer/               Script e imagens do instalador (Inno Setup)
brand/                   Símbolo, logotipo e ícone (tools/generate_brand.py)
docs/                    Arquitetura, design, privacidade, segurança e validação
licenses/                Licenças e avisos dos componentes distribuídos
```

## Documentação

- [Arquitetura e responsabilidades](docs/ARCHITECTURE.md)
- [Design da interface](docs/DESIGN.md) e [contrato da interface](docs/UI-CONTRACT.md)
- [Ferramentas, fontes e limites](docs/FEATURES.md) e [OverlayBi](docs/OVERLAY.md)
- [Validações e o que ainda falta validar](docs/VALIDATION.md)
- [Histórico de mudanças](docs/CHANGELOG.md)

## Contribuir

Leia o [guia de contribuição](docs/CONTRIBUTING.md) antes de abrir um PR: ele traz as verificações, as regras de implementação e como manter licenças e locks em dia. Problemas de segurança vão pelo canal privado descrito em [SECURITY.md](docs/SECURITY.md), nunca numa issue pública.

## Licença e créditos

O CoreBi é criado e mantido por [Gabriel Angelim](https://github.com/GabrielAngelimm) e distribuído sob a [licença MIT](LICENSE).

Os sensores vêm do [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0), os eventos de FPS do TraceEvent e os testes gráficos do Vortice, cada um com a própria licença, listada em [licenses/THIRD-PARTY.md](licenses/THIRD-PARTY.md). PawnIO e RTSS são programas de terceiros, instalados à parte e com as licenças dos autores. Os ícones dos apps do catálogo pertencem aos seus donos ([licenses/APP-ICONS.md](licenses/APP-ICONS.md)).
