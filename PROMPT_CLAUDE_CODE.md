# PROMPT MESTRE — DESENVOLVIMENTO DO MERCADO NO CONTROLE

Atue como um engenheiro de software sênior, especialista em Angular, TypeScript, C#, ASP.NET Core, Entity Framework Core e SQLite.

Sua responsabilidade é desenvolver o projeto **Mercado no Controle** seguindo rigorosamente a especificação técnica e funcional disponível em:

`docs/ESPECIFICACAO.md`

Esse documento é a principal fonte de verdade do projeto.

## 1. Antes de começar

Antes de escrever qualquer código:

1. Leia integralmente `docs/ESPECIFICACAO.md`.
2. Analise a estrutura atual do repositório.
3. Verifique se já existem arquivos, projetos ou implementações.
4. Identifique as tecnologias e versões instaladas.
5. Verifique a consistência entre a estrutura existente e a especificação.
6. Identifique decisões técnicas necessárias para implementar corretamente o projeto.

Não recrie projetos que já estejam implementados corretamente. Não sobrescreva código existente sem entender sua finalidade.

## 2. Documentação como referência permanente

Durante todo o desenvolvimento:

- Consulte a especificação antes de implementar cada módulo.
- Não altere silenciosamente regras de negócio.
- Não substitua tecnologias definidas sem justificativa.
- Não invente funcionalidades fora do escopo.
- Preserve a consistência entre frontend, backend e banco de dados.

Se identificar necessidade de alterar arquitetura ou especificação:

1. Explique o problema.
2. Apresente a alteração recomendada.
3. Atualize a documentação correspondente.
4. Mantenha a documentação coerente com o código.

Crie `docs/STATUS_DESENVOLVIMENTO.md` para acompanhar:
- Fases concluídas.
- Funcionalidades implementadas.
- Funcionalidades pendentes.
- Testes executados.
- Problemas conhecidos.
- Decisões técnicas.
- Próxima etapa.

Atualize esse arquivo ao concluir cada fase.

## 3. Arquitetura obrigatória

Frontend:
- Angular.
- TypeScript.
- Angular Material.
- SCSS.
- Componentes standalone.
- Angular Router.
- Angular Signals.
- HttpClient.
- Apache ECharts.
- Vitest.

Backend:
- ASP.NET Core Web API.
- C#.
- .NET 10.
- Entity Framework Core.
- SQLite.
- Swagger / OpenAPI.
- xUnit.

IA:
- API multimodal.
- Abstração através de `IReceiptReader`.
- Chaves exclusivamente no backend.
- Retorno estruturado em JSON.
- Conferência obrigatória pelo usuário.

Não utilizar:
- Vue.js.
- Quasar.
- SQL Server.
- Capacitor.
- Aplicativo mobile.
- Microserviços.
- RabbitMQ.
- CQRS completo.
- NgRx sem necessidade justificada.
- Infraestrutura complexa desnecessária.

## 4. Organização do código

Respeite a estrutura de diretórios definida na especificação.

Mantenha:
- Controllers para endpoints.
- Services para regras de negócio.
- Models para entidades.
- DTOs para comunicação.
- Data para persistência e configuração.
- Validators para validações.
- Components para componentes visuais.
- Services Angular para comunicação com a API.
- Features Angular separadas por funcionalidade.

Evite concentrar regras de negócio nos Controllers ou componentes Angular.

Não crie abstrações excessivas. Prefira soluções simples e fáceis de manter.

## 5. Banco de dados

Utilize SQLite com Entity Framework Core.

Requisitos:
- Criar o banco automaticamente na primeira execução, aplicando migrations.
- Utilizar migrations para alterações de esquema.
- Configurar relacionamentos e chaves estrangeiras.
- Criar índices necessários.
- Preservar integridade referencial.
- Armazenar imagens fora do banco.
- Manter o caminho do banco configurável.
- Utilizar transações para confirmar compras.

Atenção aos valores monetários:

Não utilizar float ou double para dinheiro.

Utilize decimal no domínio C# e uma estratégia explícita de persistência SQLite com inteiros escalados, conversores testados e precisão definida para cada tipo de valor.

Não dependa de agregações SQLite que possam perder precisão. Teste arredondamentos, conversões, somas e médias.

## 6. Backend

Implemente a API REST conforme a especificação.

Requisitos:
- Utilizar DTOs.
- Validar entradas.
- Tratar exceções.
- Padronizar respostas de erro.
- Utilizar CancellationToken em operações assíncronas.
- Documentar endpoints com Swagger.
- Implementar paginação, filtros e ordenação quando aplicável.
- Evitar consultas desnecessárias.
- Manter serviços organizados.

As regras de negócio definitivas deverão ser executadas no backend. Não confiar nos valores enviados pelo frontend sem validação.

## 7. Frontend

Crie interface moderna, intuitiva e responsiva com Angular Material.

A aplicação deverá possuir:
- Sidebar.
- Cabeçalho.
- Navegação organizada.
- Dashboard.
- Tabelas.
- Formulários.
- Modais.
- Filtros.
- Gráficos.
- Feedback de operações.
- Estados de carregamento.
- Estados vazios.
- Tratamento de erros.

Toda comunicação com backend deverá ocorrer por serviços Angular. Não coloque chamadas HTTP diretamente nos componentes.

Utilize Signals quando fizer sentido. Evite NgRx ou gerenciamento global complexo sem necessidade demonstrada.

## 8. Integração com IA

Implemente a leitura de notas conforme a especificação.

O fluxo deverá:

1. Receber a imagem.
2. Validar o arquivo.
3. Armazenar a imagem.
4. Enviar ao provedor de IA.
5. Receber dados estruturados.
6. Validar o resultado.
7. Apresentar os dados para conferência.
8. Permitir edição.
9. Confirmar somente após aprovação do usuário.

Implemente `IReceiptReader` e mantenha o provedor substituível.

Não exponha a chave no Angular.

Não invente dados quando a IA não identificar informações.

Sem chave de API configurada, o sistema deverá continuar funcionando com cadastro manual.

Não crie implementação falsa que simule leitura real. Dados simulados deverão ficar isolados em testes ou fixtures.

O processamento deverá possuir estado e identificador próprios, inclusive antes de existir uma nota fiscal definitiva. Não vincule obrigatoriamente uma tentativa de importação a uma nota já confirmada.

## 9. Regras de negócio prioritárias

Implemente rigorosamente:
- Identificação de notas duplicadas.
- Associação de itens aos produtos.
- Preservação das descrições originais.
- Registro do histórico de preços.
- Comparação de produtos equivalentes.
- Cálculo de preços normalizados.
- Atualização consistente após edição de compras.
- Remoção consistente de observações após exclusão.
- Validação dos totais das notas.

Toda confirmação deverá ser transacional. Uma compra não poderá ficar parcialmente registrada.

## 10. Testes

Implemente testes desde o início.

Backend:
- Serviços e regras de negócio.
- Persistência.
- Cálculos financeiros.
- Relacionamentos.
- Histórico de preços.
- Duplicidade.
- Processamento de notas.
- Edição e exclusão.
- Relatórios.
- Falhas da IA.

Frontend:
- Componentes.
- Formulários.
- Validações.
- Serviços HTTP.
- Upload.
- Conferência.
- Filtros.
- Dashboard.
- Histórico de preços.
- Estados de carregamento e erro.

Execute testes após cada etapa relevante. Não considere uma funcionalidade concluída apenas porque o código foi escrito: compile e teste.

Não ignore erros de compilação ou testes falhando.

## 11. Estratégia de implementação

Desenvolva em fases:

### Fase 1 — Fundação
- Estrutura do repositório.
- Backend.
- Frontend.
- SQLite e EF Core.
- Migrations.
- Comunicação entre frontend e backend.
- Layout principal.

### Fase 2 — Cadastros
- Supermercados.
- Produtos.
- Categorias.
- Descrições alternativas.

### Fase 3 — Compras
- Cadastro manual.
- Notas fiscais e itens.
- Upload de imagens.
- Consulta e edição.
- Detecção de duplicidade.

### Fase 4 — Inteligência artificial
- Interface do provedor.
- Integração.
- Extração estruturada.
- Validação.
- Conferência.
- Confirmação da compra.

### Fase 5 — Histórico de preços
- Registro automático.
- Associação de produtos.
- Preços normalizados.
- Comparações.
- Gráficos.

### Fase 6 — Dashboard e relatórios
- Indicadores.
- Gráficos.
- Filtros.
- Relatórios.
- Exportações.

### Fase 7 — Finalização
- Testes.
- Correções.
- Segurança.
- Backup.
- Documentação.
- Preparação para execução e publicação.

## 12. Forma de trabalho

Não tente implementar o sistema inteiro em uma única execução.

Para cada fase:

1. Consulte a especificação.
2. Analise dependências.
3. Defina tarefas pequenas.
4. Implemente as funcionalidades.
5. Execute migrations, quando necessárias.
6. Compile os projetos.
7. Execute testes.
8. Corrija problemas.
9. Atualize `docs/STATUS_DESENVOLVIMENTO.md`.
10. Apresente um resumo do que foi realizado e o que falta.

Não avance se a fase atual possuir erros críticos ou funcionalidades essenciais incompletas.

Evite interromper o trabalho para pedir aprovação em decisões técnicas rotineiras. Tome decisões coerentes com a arquitetura e documente-as.

Solicite esclarecimentos somente quando existir ambiguidade relevante que não possa ser resolvida pela especificação.

## 13. Qualidade

Priorize:
- Código legível.
- Nomes descritivos.
- Métodos pequenos.
- Responsabilidades bem definidas.
- Tratamento de erros.
- Tipagem adequada.
- Reutilização com moderação.
- Testabilidade.
- Baixa complexidade.

Não implemente funcionalidades desnecessárias apenas para demonstrar conhecimento de arquitetura.

O objetivo é construir um sistema funcional, consistente e fácil de manter.

## 14. Primeira execução

Neste primeiro momento, não implemente todas as funcionalidades.

Execute a análise e preparação:

1. Leia a especificação completa.
2. Inspecione o repositório.
3. Verifique as ferramentas disponíveis.
4. Apresente a arquitetura a ser utilizada.
5. Apresente a estrutura de diretórios definitiva.
6. Identifique incompatibilidades.
7. Elabore o plano detalhado da Fase 1.
8. Liste os comandos necessários para inicializar os projetos.

Depois da análise, inicie a Fase 1 seguindo a estratégia incremental.

## 15. Resultado esperado

Ao final, o Mercado no Controle deverá ser uma aplicação web funcional para controle pessoal de despesas de supermercado, com:
- Cadastro de compras assistido por IA.
- Cadastro manual.
- Histórico de notas fiscais.
- Cadastro de supermercados.
- Catálogo de produtos.
- Histórico de preços.
- Comparação entre estabelecimentos.
- Dashboard financeiro.
- Relatórios e exportações.
- Persistência SQLite.
- Backend ASP.NET Core.
- Frontend Angular.
- Testes automatizados.
- Documentação de execução.

**Instrução final:** mantenha `docs/ESPECIFICACAO.md` como referência obrigatória durante todo o desenvolvimento. A implementação deve atender ao documento, e não reinterpretar livremente o projeto.
