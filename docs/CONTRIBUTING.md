# Contribuir e manter

Use Windows 10 ou 11 x64, com Python 3 para as verificações e Inno Setup 6 se for gerar o instalador. Os scripts de `tools/` usam o SDK exato de `global.json` e baixam uma cópia se ele faltar (veja o README); para os demais comandos, use `.\tools\Dotnet.ps1` no lugar de `dotnet`. Se o PowerShell recusar os scripts, libere só a sessão atual com `Set-ExecutionPolicy -Scope Process Bypass`.

Leia [arquitetura](ARCHITECTURE.md), [design da interface](DESIGN.md) e [contrato da interface](UI-CONTRACT.md). Mudanças visuais seguem o design validado: a identidade (cores por peça, raios, escala de texto, movimento) não muda sem uma decisão do mantenedor. Mantenha créditos e licenças ([LICENSE](../LICENSE), [NOTICE.md](../NOTICE.md) e `licenses/`).

## Antes de enviar uma alteração

```powershell
python tools/sync_theme.py --check
python tools/check_resources.py
python tools/collect_licenses.py --check
.\tools\Build.ps1
.\tools\Dotnet.ps1 format whitespace CoreBi.slnx --no-restore --verify-no-changes
.\tools\Test.ps1
.\tools\SecretScan.ps1
.\tools\Publish.ps1
```

Use testes com cenários e falhas relevantes; não replique a implementação nas asserções. Hardware, Startup, Integrations, Bench e OverlayTelemetry são modos optativos conforme a mudança. Informe equipamento, preset, permissões e dependências; remova IDs pessoais dos relatórios. A CI não instala drivers/apps nem modifica inicialização. Nunca execute cargas longas ou instalações administrativas como efeito de um teste padrão.

## Modos de teste

| Comando | Alcance |
| --- | --- |
| `.\tools\Test.ps1` | Regressões de regras/falhas, regras do Engine que viram veredito na tela e composição/navegação das páginas WPF |
| `.\tools\Test.ps1 -Mode Hardware` | Leituras reais; com administrador inclui abertura/parada de ETW |
| `.\tools\Test.ps1 -Mode Startup` | Carga do sistema, sensores, Disco e Energia no dispatcher WPF, sem ativar a janela |
| `.\tools\Test.ps1 -Mode Integrations` | Consultas winget/NVIDIA ou AMD/Windows Update; com administrador inclui ACL |
| `.\tools\Test.ps1 -Mode Bench` | Cargas reais curtas e arquivos temporários; não certifica estabilidade térmica |
| `.\tools\Test.ps1 -Mode OverlayTelemetry` | RAM física e uso da GPU pelos contadores do Windows (e NVML, se houver) por alguns segundos, sem abrir a janela |

Execute `Bench` com outras cargas pesadas encerradas. Resultados JSON ficam em `artifacts/tests`; configurações de teste ficam isoladas em `artifacts/tests/settings`. O override `COREBI_DATA_DIR` é definido pelo executor, sem alterar os dados pessoais do aplicativo.

## Ferramentas

| Script | Para que serve | Requisitos |
| --- | --- | --- |
| `Build.ps1` | Restaura em modo bloqueado e compila a solução (`-Configuration`, padrão Release) | — |
| `Test.ps1` | Restaura em modo bloqueado e roda o executor de testes no modo escolhido (`-Mode`, `-NoBuild`) | — |
| `Publish.ps1` | Publica o app self-contained (`CoreBi.exe` com as DLLs nativas ao lado, que não são extraídas para a pasta temporária), copia `licenses/`, `LICENSE` e `NOTICE.md`. A pasta padrão `artifacts/publish/win-x64` é apagada antes; uma pasta passada em `-OutputDirectory` é usada como está | — |
| `Installer.ps1` | Publica (exceto com `-SkipPublish`) e gera `artifacts/installer/CoreBi-<versão>-win-x64-setup.exe` a partir de `installer/CoreBi.iss`, com a versão do `Directory.Build.props`; mostra o SHA256 | Inno Setup 6 (`ISCC.exe` no caminho padrão, `-Iscc` ou `$env:COREBI_ISCC`) |
| `SecretScan.ps1` | Procura segredos em todo o histórico Git com o Gitleaks; relatório em `artifacts/secrets.json` | Rede na primeira vez: baixa o Gitleaks em versão fixada e confere o SHA256 |
| `Dotnet.ps1` | Roda qualquer comando do `dotnet` com o SDK exato do `global.json` | — |
| `Common.ps1` | Funções comuns dos scripts (localiza ou baixa o SDK); não é chamado diretamente | — |
| `sync_theme.py` | Copia a paleta do `ThemeManager.cs` para o `Theme.xaml`; `--check` só confere | Python 3 |
| `check_resources.py` | Confere as chaves `StaticResource`/`DynamicResource` do XAML e as usadas no C# do Desktop, e a paleta do `Theme.xaml` | Python 3 |
| `collect_licenses.py` | Gera `licenses/` (avisos, `packages.json`, `THIRD-PARTY.md`) a partir dos pacotes restaurados; `--check` compara `licenses/packages.json` com o lock do Desktop e o `THIRD-PARTY.md` com o `packages.json`, sem precisar de restore | Python 3; sem `--check`: restore win-x64, cache NuGet e rede para textos SPDX que faltarem |
| `generate_brand.py` | Gera a identidade visual (SVG, PNG e ícone) em `brand/` e `artifacts/brand` e copia logo e ícone para `src/CoreBi.Desktop/Assets` | Python 3 com Pillow e as fontes do Windows (Segoe UI, Bahnschrift, Consolas) |

Os scripts PowerShell e o `.iss` ficam em UTF-8 com BOM (veja `.editorconfig`): sem o BOM, o Windows PowerShell 5.1 e o Inno Setup leem os acentos como ANSI.

## Regras de implementação

- Coleta/regras no Engine, apresentação no Desktop; sem WPF no Engine.
- Estado ausente continua ausente. Não invente sensores, FPS ou sucesso quando houve falha/parcialidade.
- Cargas pesadas usam `WorkloadCoordinator`, cancelamento e limpeza em `finally`. Aguardar workers antes de liberar recursos é parte da operação.
- Processos usam executável/argumentos explícitos. Não componha comandos de shell a partir de dados externos.
- Artefatos executados com administrador ficam na área protegida; configuração editável não é fonte confiável para restauração elevada.
- Modelos e serviços ficam próximos da ferramenta que os usa. Evite duplicação, framework novo ou abstração sem consumidor real.
- Documente mudanças em algoritmo, unidades, persistência e permissões. Comente decisões e restrições; nomes claros dispensam narrar cada linha.

## Dependências

Versões diretas ficam no `.csproj`; NuGet resolve transitivas nos locks. Atualize intencionalmente com `.\tools\Dotnet.ps1 restore CoreBi.slnx --force-evaluate`, revise o diff e regenere o lock de publicação com `.\tools\Dotnet.ps1 restore src/CoreBi.Desktop -r win-x64 -p:PublishSingleFile=true --force-evaluate` (o lock de publicação depende da versão exata do SDK). Execute restore bloqueado e publicação novamente. Não edite hashes manualmente.

Após publicação/restauração, execute `python tools/collect_licenses.py` com `NUGET_PACKAGES` correto ou `--packages <pasta>`, revise `licenses/THIRD-PARTY.md` e os textos em `licenses/`. A CI roda `python tools/collect_licenses.py --check` e falha se `licenses/packages.json` não bater com o lock ou se o `THIRD-PARTY.md` não bater com ele. `.\tools\Dotnet.ps1 list CoreBi.slnx package --vulnerable --include-transitive` consulta avisos conhecidos; uma resposta vazia não certifica ausência de vulnerabilidades.

Ações da CI são fixadas por SHA. Gitleaks é baixado em versão fixada com SHA256 do arquivo oficial. Dependabot propõe atualizações semanalmente, com os pacotes `Vortice.*` e `System.*` em grupo; o mantenedor revisa compatibilidade, locks e resultados antes de integrar. Um PR do Dependabot atualiza os locks, mas não `licenses/`: rode `collect_licenses.py` no branch dele antes do merge, ou a CI falha no `--check`.

## Versão e release

Uma troca de versão passa por:

- `<Version>` em `Directory.Build.props` (assemblies, nome do instalador e versão mostrada no app);
- a versão de reserva `AppVersion` em `installer/CoreBi.iss`, usada quando o `.iss` é compilado sem o `Installer.ps1`;
- a série com suporte em `docs/SECURITY.md`, quando muda a versão principal;
- uma seção nova em `docs/CHANGELOG.md`;
- os locks: `tests/CoreBi.Tests/packages.lock.json` fixa `CoreBi.Engine` na versão do projeto, então rode `.\tools\Dotnet.ps1 restore CoreBi.slnx --force-evaluate` e confira o diff antes do restore bloqueado.

O texto de `SideVersion` em `src/CoreBi.Desktop/Shell/MainWindow.xaml` é só para o designer; em execução o app mostra a versão do assembly.

Uma release é manual e sai de um commit da `main` com a CI verde:

1. Troque a versão como acima e feche a seção do CHANGELOG com a data.
2. `.\tools\Installer.ps1` gera o instalador e mostra o SHA256.
3. Para a versão portátil, compacte a pasta `artifacts/publish/win-x64` inteira (o `CoreBi.exe` não roda sozinho: as DLLs nativas e `licenses/` vão junto). Use uma ferramenta que grave caminhos com `/`, porque o `Compress-Archive` do Windows PowerShell 5.1 grava `\`.
4. Gere o zip do código-fonte daquele commit e um `SHA256SUMS.txt` com os três arquivos.
5. Crie a tag `vX.Y.Z` no commit e publique a release com as notas em português, tiradas do CHANGELOG.

## Dados e publicação

Não inclua `artifacts`, relatórios locais, credenciais, dumps, `.env`, chaves ou executáveis pessoais. Testes usam configurações separadas dos dados pessoais. Nunca inclua um dump de configuração em issue. Use o canal de [segurança](SECURITY.md) para material sensível. Releases devem conter binário completo, licenças, código correspondente e SHA256, identificados pela mesma versão/commit validado na CI.
