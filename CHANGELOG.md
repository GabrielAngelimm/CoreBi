# Mudanças

## Não lançado

- Disco, Energia e Rede com estado/handlers próprios, tarefas aguardáveis e proteção contra atualizações após fechamento.
- Tema, benchmark de disco e exportação separados nos diretórios correspondentes; remoção de encaminhamentos e método sem uso no shell.
- Executor WPF isolado da instância única do produto; cobertura de exclusão entre cargas e vida útil das páginas.
- Resumo de saúde de disco distingue falta de dados de ausência de problemas detectados.
- README exclusivamente técnico; contrato da interface movido para docs e documentação organizada por responsabilidade.
- Exclusões de artefatos ancoradas na raiz para não ocultar fontes em Features/Reports.

## 0.1.1 — 2026-10-23

- Isolamento de handlers/estado das três ferramentas administrativas em suas páginas, com contexto mínimo de vida útil.
- Inicialização com backup durável antes da remoção e restauração por registro canônico protegido; conflitos/falhas preservam a recuperação.
- Download de driver com domínio/redirecionamento validado, publicador exato, Authenticode e proteção contra substituição do arquivo.
- Windows Update com identidades por revisão, liberação de objetos COM e resultado individual/possibilidade de reboot.
- Processos com argumentos separados, saída limitada e cancelamento da árvore criada pelo aplicativo.
- Abertura/fechamento de sensores idempotentes, snapshots imutáveis, isolamento de dispositivo e ETW sob demanda.
- Benchmarks/stress com validação, cancelamento/limpeza e propagação de falhas de workers; HLSL separado em recursos incorporados.
- Configurações atômicas/normalizadas, diagnóstico local limitado, tratamento de falhas de gravação e cache de API validado/limitado.
- Hardware sem referência não produz FPS inventado; rede distingue ICMP bloqueado de ausência de conexão.
- 24 regressões lógicas/WPF e modos separados para hardware, integração e cargas reais; CI Windows, dependências/ações fixadas, varredura de segredos e documentação revisada.

## 0.1.0 — 2026-10-23

Primeira base independente CoreBi: marca autoral, divisão Engine/Desktop, 15 páginas mantidas, remoção do escopo excluído e correções iniciais de dados/concorrência. Créditos MIT e licenças de terceiros preservados.
