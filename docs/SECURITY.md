# Segurança

CoreBi 0.2.1 é uma prévia técnica. Correções se concentram na versão mais recente; não há promessa de suporte a versões antigas.

## Reportar

Use [Report a vulnerability](https://github.com/GabrielAngelimm/CoreBi/security/advisories/new) para comunicar problemas em privado. Descreva versão, caminho afetado, impacto e reprodução mínima. Não publique credenciais, serial de hardware ou arquivos completos de configuração em uma issue pública.

## Limites de confiança

O aplicativo atualmente exige administrador. Leituras dependem de bibliotecas/drivers e as ferramentas podem instalar software ou alterar a inicialização quando solicitadas. Separar um processo administrativo mínimo é uma evolução futura; esta revisão não implementa sandbox ou driver próprio.

Downloads de drivers validam domínio HTTPS, redirecionamentos, assinatura Authenticode, cadeia/revogação e publicador exato. Downloads e journal de inicialização usam diretório com ACL restrita a Administradores/SYSTEM, sem reparse points. Restauração usa registro canônico por ID; não confia no JSON editável de configurações. Não importe backups de origem desconhecida.

Processos são criados com argumentos separados. Cancelar uma instalação iniciada pelo winget pode interromper o processo sem desfazer mudanças já feitas pelo instalador; o app informa o término disponível, não garante rollback de terceiros. Windows Update também pode exigir reinicialização.

DPAPI protege a chave da API para o usuário atual, mas não protege contra comprometimento desse usuário ou de um administrador. Assinatura confiável confirma origem/integridade do arquivo, não ausência de falhas do fornecedor. O CoreBi não substitui backup do sistema.

Logs locais têm rotação e omitem mensagens de exceção/headers; contêm tipos, operações e stacks. Ainda assim, revise qualquer material antes de compartilhá-lo. Testes de política e falhas não equivalem a um teste de invasão nem certificam instalações reais.
