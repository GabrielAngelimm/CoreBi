# Contribuir e manter

Use Windows x64 e o SDK de `global.json`. Leia [arquitetura](docs/ARCHITECTURE.md), [DESIGN.md](DESIGN.md) e [UX-CONTRACT.md](UX-CONTRACT.md). Preserve o layout atual até a etapa de UI/UX e mantenha créditos/licenças.

## Antes de enviar uma alteração

```powershell
dotnet restore CoreBi.slnx --locked-mode
dotnet format whitespace CoreBi.slnx --no-restore --verify-no-changes
.\tools\Build.ps1
.\tools\Test.ps1
.\tools\SecretScan.ps1
.\tools\Publish.ps1
```

Use testes com cenários e falhas relevantes; não replique a implementação nas asserções. Hardware, Startup, Integrations e Bench são modos optativos conforme a mudança. Informe equipamento, preset, permissões e dependências; remova IDs pessoais dos relatórios. A CI não instala drivers/apps nem modifica inicialização. Nunca execute cargas longas ou instalações administrativas como efeito de um teste padrão.

## Regras de implementação

- Coleta/regras no Engine, apresentação no Desktop; sem WPF no Engine.
- Estado ausente continua ausente. Não invente sensores, FPS ou sucesso quando houve falha/parcialidade.
- Cargas pesadas usam `WorkloadCoordinator`, cancelamento e limpeza em `finally`. Aguardar workers antes de liberar recursos é parte da operação.
- Processos usam executável/argumentos explícitos. Não componha comandos de shell a partir de dados externos.
- Artefatos executados com administrador ficam na área protegida; configuração editável não é fonte confiável para restauração elevada.
- Modelos e serviços ficam próximos da ferramenta que os usa. Evite duplicação, framework novo ou abstração sem consumidor real.
- Documente mudanças em algoritmo, unidades, persistência e permissões. Comente decisões e restrições; nomes claros dispensam narrar cada linha.

## Dependências

Versões diretas ficam no `.csproj`; NuGet resolve transitivas nos locks. Atualize intencionalmente com `dotnet restore CoreBi.slnx --force-evaluate`, revise o diff e regenere o lock de publicação com `dotnet restore src/CoreBi.Desktop -r win-x64 -p:PublishSingleFile=true --force-evaluate`. Execute restore bloqueado e publicação novamente. Não edite hashes manualmente.

Após publicação/restauração, execute `python tools/collect_licenses.py` com `NUGET_PACKAGES` correto ou `--packages <pasta>`, revise `THIRD-PARTY.md` e os textos em `licenses/`. `dotnet list CoreBi.slnx package --vulnerable --include-transitive` consulta avisos conhecidos; uma resposta vazia não certifica ausência de vulnerabilidades.

Ações da CI são fixadas por SHA. Gitleaks é baixado em versão fixada com SHA256 do arquivo oficial. Dependabot propõe atualizações semanalmente; o mantenedor revisa compatibilidade, locks e resultados antes de integrar.

## Dados e publicação

Não inclua `artifacts`, relatórios locais, credenciais, dumps, `.env`, chaves ou executáveis pessoais. Testes têm diretório isolado por execução e chaves artificiais. Nunca inclua a chave real da API ou um dump de configuração em issue. Use o canal de [segurança](SECURITY.md) para material sensível. Releases devem conter binário completo, licenças, código correspondente e SHA256, identificados pela mesma versão/commit validado na CI.
