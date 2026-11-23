# Mercado no Controle — Especificação Técnica e Funcional

**Versão:** 1.1  
**Status:** Aprovada para início do desenvolvimento  
**Tipo:** Aplicação web pessoal  
**Objetivo:** Controle de despesas de supermercado e acompanhamento histórico de preços.

> Este documento é a fonte principal de verdade do projeto. A implementação deve respeitar seus requisitos, regras de negócio e critérios de aceitação.

---

# PARTE I — ESPECIFICAÇÃO DO PROJETO

## 1. Visão geral

Mercado no Controle é uma aplicação web para registrar, organizar e analisar compras de supermercado a partir de fotografias de notas fiscais.

O sistema utilizará inteligência artificial multimodal para extrair informações das notas fiscais, incluindo estabelecimento, data, produtos, quantidades, preços, descontos e total da compra.

Antes de registrar uma compra, o usuário deverá conferir e corrigir os dados extraídos. Após a confirmação, os dados alimentarão automaticamente os relatórios financeiros e o histórico de preços dos produtos.

O projeto será desenvolvido inicialmente para uso pessoal, com arquitetura simples, organizada e fácil de manter.

### 1.1 Objetivos

- Automatizar o cadastro de compras por fotografia de notas fiscais.
- Centralizar o histórico de compras.
- Acompanhar gastos por período, supermercado e categoria.
- Monitorar a evolução dos preços dos produtos.
- Comparar preços entre estabelecimentos.
- Identificar produtos com maior aumento de preço.
- Gerar relatórios e gráficos financeiros.

### 1.2 Escopo

O produto será exclusivamente uma aplicação web responsiva.

Não fazem parte do escopo:
- Aplicativo Android ou iOS.
- Capacitor ou sincronização mobile.
- Banco de dados em nuvem obrigatório.
- Microserviços ou infraestrutura distribuída.

A aplicação deverá funcionar localmente em um computador e poderá ser publicada futuramente em um servidor.

## 2. Stack tecnológica

### 2.1 Frontend

- Angular.
- TypeScript.
- Angular Material.
- SCSS.
- Angular Router.
- Angular Signals.
- HttpClient.
- Apache ECharts para gráficos.
- Vitest para testes de frontend.

Utilizar componentes standalone e organização por funcionalidades.

### 2.2 Backend

- ASP.NET Core Web API.
- C#.
- .NET 10 LTS.
- Entity Framework Core 10.
- Swagger / OpenAPI.
- xUnit para testes de backend.

FluentValidation poderá ser utilizado caso agregue clareza às validações.

### 2.3 Banco de dados

- SQLite.
- Entity Framework Core.
- Migrations para gerenciamento do esquema.

O banco será armazenado em arquivo local, fora do diretório de código-fonte. Nome sugerido: `mercado-no-controle.db`.

### 2.4 Inteligência artificial

Utilizar um provedor multimodal com capacidade de interpretar imagens e retornar dados estruturados.

O backend deverá abstrair o provedor por meio de uma interface, permitindo sua substituição sem alterar as regras de negócio.

A chave de API deverá existir exclusivamente no backend, configurada por variável de ambiente ou mecanismo seguro equivalente.

### 2.5 Infraestrutura

- Git para versionamento.
- Armazenamento local de imagens.
- Configuração por ambiente.
- Logs estruturados.
- Docker opcional, não obrigatório para execução local.

## 3. Arquitetura

Adotar um monólito modular simples. Não utilizar microserviços, CQRS completo, RabbitMQ ou camadas desnecessárias.

### 3.1 Fluxo geral

1. O usuário acessa a aplicação Angular.
2. O frontend realiza requisições à API ASP.NET Core.
3. O backend executa as regras de negócio.
4. O Entity Framework Core acessa o SQLite.
5. O backend chama o provedor de IA quando uma nota precisa ser interpretada.
6. As imagens são armazenadas no sistema de arquivos, separadas do banco.
7. O frontend apresenta os dados e relatórios.

### 3.2 Estrutura sugerida

```text
MercadoNoControle/
├── backend/
│   ├── MercadoNoControle.Api/
│   │   ├── Controllers/
│   │   ├── Services/
│   │   ├── Models/
│   │   ├── DTOs/
│   │   ├── Data/
│   │   ├── Validators/
│   │   ├── Configuration/
│   │   └── Program.cs
│   └── MercadoNoControle.Tests/
├── frontend/
│   └── mercado-no-controle/
│       └── src/app/
│           ├── core/
│           ├── shared/
│           ├── features/
│           │   ├── dashboard/
│           │   ├── receipts/
│           │   ├── products/
│           │   ├── supermarkets/
│           │   ├── categories/
│           │   ├── price-history/
│           │   ├── reports/
│           │   └── settings/
│           └── app.routes.ts
├── data/
├── uploads/
├── docs/
├── .gitignore
└── README.md
```

A estrutura poderá ser refinada durante a implementação, sem perder a separação entre frontend, backend, dados, arquivos e documentação.

## 4. Módulos funcionais

### 4.1 Dashboard

Apresentar uma visão consolidada das despesas.

Indicadores:
- Total gasto no mês.
- Total gasto no ano.
- Quantidade de compras no período.
- Ticket médio por compra.
- Maior compra registrada.
- Supermercado com maior volume de gastos.

Gráficos:
1. Evolução mensal dos gastos.
2. Distribuição por supermercado.
3. Distribuição por categoria.
4. Produtos com maior participação nas despesas.
5. Comparativo entre meses.

Filtros:
- Data inicial e final.
- Mês e ano.
- Supermercado.
- Categoria.

Somente compras confirmadas e válidas deverão compor os indicadores.

### 4.2 Notas fiscais e compras

Funcionalidades:
- Enviar fotografia de uma nota fiscal.
- Selecionar imagem do computador.
- Processar a imagem com IA.
- Visualizar o status do processamento.
- Conferir informações extraídas.
- Corrigir dados manualmente.
- Confirmar a compra.
- Consultar compras anteriores.
- Editar e excluir compras.
- Visualizar a imagem original.
- Cadastrar compras manualmente quando a IA não estiver disponível.

Dados gerais da nota:
- Supermercado.
- CNPJ.
- Número.
- Série.
- Chave de acesso, quando disponível.
- Data e horário de emissão.
- Valor dos produtos.
- Descontos.
- Acréscimos.
- Valor total.
- Forma de pagamento, quando disponível.
- Caminho da imagem original.
- Status da nota.

Dados de cada item:
- Descrição original.
- Produto associado, quando identificado.
- Quantidade.
- Unidade.
- Valor unitário.
- Desconto.
- Valor total.

### 4.3 Supermercados

Campos:
- Nome fantasia.
- Razão social.
- CNPJ.
- Endereço.
- Cidade.
- Estado.
- Observações.

Regras:
- Utilizar CNPJ para identificar estabelecimentos.
- Impedir duplicidade de CNPJ.
- Permitir cadastro e edição manual.
- Consultar histórico de compras do estabelecimento.
- Tentar identificar estabelecimentos durante a importação da nota.

### 4.4 Produtos

Campos:
- Nome padronizado.
- Marca.
- Categoria.
- Código de barras.
- Unidade de medida.
- Conteúdo da embalagem.
- Situação do cadastro.

Unidades previstas:
- UN.
- KG.
- G.
- L.
- ML.
- CX.
- PCT.
- DZ.

Manter separadas as descrições originais encontradas nas notas e os nomes padronizados dos produtos.

Exemplo:

| Descrição na nota | Produto padronizado |
|---|---|
| LEITE INT 1L | Leite Integral 1L |
| LEITE INTEGRAL | Leite Integral 1L |
| LT INT 1000ML | Leite Integral 1L |

A IA poderá sugerir associações, mas associações ambíguas deverão ser confirmadas pelo usuário. Produtos com marcas, tamanhos ou apresentações diferentes não devem ser considerados equivalentes automaticamente.

### 4.5 Histórico de preços

Cada item de uma compra confirmada, associado a um produto, deverá gerar um registro histórico de preço.

Informações:
- Produto.
- Supermercado.
- Nota fiscal de origem.
- Item da nota de origem.
- Data da compra.
- Quantidade.
- Preço unitário.
- Preço total.
- Preço normalizado, quando aplicável.

Indicadores:
- Último preço pago.
- Menor preço registrado.
- Maior preço registrado.
- Preço médio.
- Evolução histórica.
- Variação percentual.
- Histórico por supermercado.

Permitir consultar um produto, visualizar sua evolução em gráfico de linhas e comparar preços entre supermercados.

A comparação deverá considerar produtos equivalentes e unidades compatíveis. O preço normalizado poderá ser calculado por kg, litro ou unidade quando os dados da embalagem permitirem.

### 4.6 Categorias

Categorias iniciais:
- Mercearia.
- Hortifruti.
- Carnes.
- Laticínios.
- Bebidas.
- Higiene pessoal.
- Limpeza.
- Congelados.
- Padaria.
- Outros.

Permitir listar, criar, editar e excluir categorias sem utilização. A IA poderá sugerir categorias durante o processamento.

### 4.7 Relatórios

Relatórios:
- Gastos mensais e anuais.
- Gastos por supermercado.
- Gastos por categoria.
- Produtos mais comprados.
- Produtos com maior gasto acumulado.
- Produtos com maior aumento de preço.
- Histórico de compras.
- Comparação entre períodos.

Permitir filtros por período, supermercado e categoria.

Exportações:
- CSV.
- Excel.
- PDF.

As exportações deverão refletir os filtros aplicados e os dados efetivamente consultados.

### 4.8 Configurações

- Configurações gerais.
- Configuração do provedor de IA.
- Preferências de exibição.
- Gerenciamento de categorias.
- Backup e exportação dos dados.
- Gerenciamento das imagens.

## 5. Integração com inteligência artificial

### 5.1 Objetivo

Interpretar fotografias de notas fiscais e retornar dados estruturados para a conferência e o cadastro da compra.

### 5.2 Fluxo

1. Usuário seleciona uma imagem.
2. Backend valida o arquivo.
3. Imagem é armazenada localmente.
4. Backend envia a imagem ao provedor de IA.
5. IA identifica os dados gerais da nota e os itens.
6. Backend valida a resposta.
7. Frontend apresenta os dados para conferência.
8. Usuário corrige eventuais erros.
9. Usuário confirma a compra.
10. Backend salva nota e itens em uma transação.
11. Sistema gera os registros de histórico de preços.

### 5.3 Contrato de retorno

A IA deverá retornar JSON estruturado semelhante a:

```json
{
  "store": {
    "name": "SUPERMERCADO EXEMPLO",
    "cnpj": "00000000000000"
  },
  "receipt": {
    "number": "123456",
    "series": "001",
    "accessKey": null,
    "issueDate": "2026-11-15T14:30:00",
    "totalProducts": 186.90,
    "discount": 0,
    "additionalCharges": 0,
    "total": 186.90
  },
  "items": [
    {
      "description": "ARROZ TIPO 1 5KG",
      "quantity": 1,
      "unit": "UN",
      "unitPrice": 28.90,
      "discount": 0,
      "totalPrice": 28.90
    }
  ]
}
```

O exemplo representa o contrato esperado, não uma extração real. Campos desconhecidos deverão ser nulos ou sinalizados como ausentes. A IA não poderá inventar informações.

### 5.4 Abstração do provedor

Criar uma interface:

```csharp
public interface IReceiptReader
{
    Task<ReceiptExtractionResult> ExtractAsync(
        Stream image,
        CancellationToken cancellationToken
    );
}
```

Implementar o provedor concreto separadamente. A escolha do fornecedor deverá ser configurável.

### 5.5 Erros e conferência

Tratar:
- Imagem ilegível ou sem nota fiscal.
- Falha, timeout ou indisponibilidade da API.
- JSON inválido.
- Dados incompletos.
- Valores inconsistentes.
- CNPJ inválido.
- Nota possivelmente duplicada.

A falha da leitura automática não poderá impedir o cadastro manual.

Nenhuma compra poderá ser confirmada automaticamente pela IA. A confirmação do usuário é obrigatória.

## 6. Modelagem do banco de dados

### 6.1 Entidades principais

**Usuario**
- Id
- Nome
- Email
- SenhaHash
- DataCriacao

**Supermercado**
- Id
- NomeFantasia
- RazaoSocial
- CNPJ
- Endereco
- Cidade
- UF
- DataCriacao

**Categoria**
- Id
- Nome
- Descricao

**Produto**
- Id
- Nome
- Marca
- CategoriaId
- CodigoBarras
- UnidadeMedida
- ConteudoEmbalagem
- DataCriacao

**ProdutoDescricao**
- Id
- ProdutoId
- Descricao
- Confirmada
- Origem

**NotaFiscal**
- Id
- SupermercadoId
- Numero
- Serie
- ChaveAcesso
- DataEmissao
- ValorProdutos
- ValorDescontos
- ValorAcrescimos
- ValorTotal
- FormaPagamento
- ImagemPath
- Status
- DataCriacao

**ItemNotaFiscal**
- Id
- NotaFiscalId
- ProdutoId
- DescricaoOriginal
- Quantidade
- Unidade
- ValorUnitario
- Desconto
- ValorTotal

**HistoricoPreco**
- Id
- ProdutoId
- ItemNotaFiscalId
- SupermercadoId
- DataRegistro
- Quantidade
- PrecoUnitario
- PrecoTotal
- PrecoNormalizado

**ProcessamentoIA**
- Id
- NotaFiscalId, quando já existir uma nota
- Provedor
- Modelo
- Status
- ResultadoJson
- Erro
- DataProcessamento

O processamento de uma imagem ainda não confirmada deverá possuir identificação própria, sem exigir uma NotaFiscal definitiva. O modelo poderá ser ajustado para contemplar um registro de importação ou rascunho.

### 6.2 Relacionamentos

- Um supermercado possui várias notas fiscais.
- Uma nota fiscal possui vários itens.
- Um produto pode aparecer em vários itens.
- Uma categoria pode possuir vários produtos.
- Um produto pode possuir várias descrições alternativas.
- Um item de nota pode gerar uma observação de preço.
- Uma nota pode possuir registros de processamento associados.

### 6.3 Persistência e integridade

- Utilizar migrations do EF Core.
- Criar índices para CNPJ, código de barras, chave de acesso, datas e chaves estrangeiras.
- Configurar chaves estrangeiras.
- Utilizar exclusão restritiva para entidades com histórico.
- Utilizar transações ao confirmar compras.
- Não sobrescrever observações históricas.
- Armazenar imagens fora do banco.
- Preservar imagens originais.

### 6.4 Precisão monetária

Não utilizar float ou double para valores monetários.

No domínio C#, utilizar decimal. Na persistência SQLite, adotar uma estratégia explícita com inteiros escalados, como centavos para valores totais e escala inteira definida para preços unitários e normalizados.

Implementar conversões explícitas e testadas. Evitar agregações SQLite que provoquem perda de precisão monetária. Os cálculos financeiros deverão preservar a precisão definida pelo domínio.

## 7. Regras de negócio

**RN01 — Registro:** uma compra só será registrada após confirmação do usuário.

**RN02 — Duplicidade:** verificar chave de acesso antes de cadastrar. Sem chave, alertar sobre possíveis duplicatas considerando estabelecimento, data, valor e demais dados disponíveis. A verificação auxiliar não deve bloquear automaticamente compras legítimas.

**RN03 — Histórico:** toda compra confirmada deverá gerar observações de preço para itens associados a produtos.

**RN04 — Edição:** alterações em compras deverão atualizar os dados derivados de forma consistente, sem perder a rastreabilidade.

**RN05 — Exclusão:** ao excluir uma nota, seus itens e observações históricas correspondentes deverão deixar de participar dos relatórios, respeitando a integridade referencial.

**RN06 — Produto não identificado:** permitir manter o item sem associação definitiva e associá-lo posteriormente.

**RN07 — Equivalência:** compartilhar histórico somente entre produtos compatíveis.

**RN08 — Preço normalizado:** comparar por kg, litro ou unidade somente quando quantidade e unidade da embalagem forem conhecidas.

**RN09 — Relatórios:** considerar somente compras confirmadas e válidas.

**RN10 — Consistência:** comparar soma dos itens, descontos e acréscimos com o total da nota. Sinalizar divergências, respeitando arredondamentos e encargos.

## 8. API REST

### Dashboard
- GET `/api/dashboard`
- GET `/api/dashboard/monthly`
- GET `/api/dashboard/stores`
- GET `/api/dashboard/categories`

### Notas fiscais
- GET `/api/receipts`
- GET `/api/receipts/{id}`
- POST `/api/receipts`
- POST `/api/receipts/upload`
- GET `/api/receipts/{id}/processing`
- PUT `/api/receipts/{id}`
- PUT `/api/receipts/{id}/confirm`
- DELETE `/api/receipts/{id}`

O fluxo de upload deverá retornar um identificador de importação/processamento. Os endpoints de status e conferência deverão utilizar esse identificador quando a nota ainda não estiver confirmada. Ajustar os caminhos conforme o modelo de rascunho adotado.

### Supermercados
- GET `/api/stores`
- GET `/api/stores/{id}`
- POST `/api/stores`
- PUT `/api/stores/{id}`
- DELETE `/api/stores/{id}`

### Produtos
- GET `/api/products`
- GET `/api/products/{id}`
- POST `/api/products`
- PUT `/api/products/{id}`
- DELETE `/api/products/{id}`
- GET `/api/products/{id}/prices`
- GET `/api/products/{id}/comparison`

### Categorias
- GET `/api/categories`
- POST `/api/categories`
- PUT `/api/categories/{id}`
- DELETE `/api/categories/{id}`

### Relatórios
- GET `/api/reports/monthly`
- GET `/api/reports/stores`
- GET `/api/reports/categories`
- GET `/api/reports/products`
- GET `/api/reports/export`

Todos os endpoints deverão utilizar DTOs, validação e tratamento padronizado de erros. Listagens deverão possuir paginação, filtros e ordenação quando aplicável.

## 9. Interface e experiência

### 9.1 Layout

- Sidebar no desktop.
- Cabeçalho.
- Conteúdo principal.
- Navegação adaptada para telas menores.
- Interface responsiva.

### 9.2 Menu

- Dashboard.
- Notas fiscais.
- Nova compra.
- Produtos.
- Histórico de preços.
- Supermercados.
- Categorias.
- Relatórios.
- Configurações.

### 9.3 Telas obrigatórias

1. Dashboard.
2. Listagem de notas fiscais.
3. Detalhes da nota.
4. Nova compra.
5. Conferência dos dados extraídos.
6. Cadastro de supermercados.
7. Cadastro de produtos.
8. Detalhes do produto.
9. Histórico de preços.
10. Comparação de preços.
11. Categorias.
12. Relatórios.
13. Configurações.

### 9.4 Diretrizes visuais

- Interface limpa e moderna.
- Priorizar legibilidade.
- Evitar excesso de informações.
- Utilizar cores para indicar estados.
- Exibir feedback em operações.
- Implementar estados vazios, carregamento e erro.
- Utilizar tabelas com paginação e filtros.
- Utilizar gráficos com legendas e informações claras.

## 10. Segurança e confiabilidade

- Não expor chaves de API no frontend.
- Não versionar arquivos de segredo.
- Validar tipo e tamanho de arquivos.
- Armazenar imagens em diretório privado.
- Configurar CORS apenas para origens necessárias.
- Utilizar HTTPS em ambientes publicados.
- Proteger a API caso seja disponibilizada fora do computador local.
- Realizar backup do banco e das imagens.
- Registrar erros de integração.
- Não armazenar senhas em texto puro.

Como haverá um único usuário, a autenticação deverá ser simples, sem multi-tenancy, organizações ou permissões complexas. Se a aplicação ficar restrita ao computador local, documentar claramente o modelo de acesso e os riscos de exposição da porta da API.

## 11. Testes

### Backend
- Serviços e regras de negócio.
- Persistência e relacionamentos.
- Cálculos financeiros.
- Histórico de preços.
- Detecção de duplicidade.
- Processamento de notas.
- Edição e exclusão.
- Relatórios.
- Falhas do provedor de IA.

### Frontend
- Componentes.
- Formulários.
- Validações.
- Serviços HTTP.
- Upload.
- Conferência de notas.
- Filtros.
- Dashboard.
- Histórico de preços.
- Estados de carregamento e erro.

### Integração ponta a ponta
1. Enviar imagem.
2. Processar nota.
3. Conferir informações.
4. Confirmar compra.
5. Verificar persistência.
6. Consultar dashboard.
7. Consultar histórico de produto.
8. Editar ou excluir compra.
9. Verificar consistência dos relatórios.

## 12. Critérios de aceitação

O projeto será considerado funcional quando:

- Frontend e backend executarem localmente.
- SQLite for criado por migrations.
- Compras puderem ser cadastradas manualmente.
- Fotografias puderem ser enviadas para leitura.
- A IA retornar dados estruturados em notas compatíveis.
- O usuário puder corrigir dados antes da confirmação.
- Compras confirmadas forem persistidas corretamente.
- Históricos de preços forem gerados.
- Dashboard apresentar dados corretos.
- For possível comparar preços.
- Relatórios possuírem filtros e exportações.
- Edição e exclusão preservarem consistência.
- Testes essenciais estiverem implementados e aprovados.
- Houver documentação de execução e backup.

---

# PARTE II — PLANEJAMENTO DE IMPLEMENTAÇÃO

## 13. Fases

### Fase 1 — Fundação
- Criar repositório e estrutura.
- Criar solução .NET.
- Criar aplicação Angular.
- Configurar Angular Material.
- Configurar SQLite e EF Core.
- Criar migrations iniciais.
- Configurar comunicação frontend/backend.
- Criar layout principal.

### Fase 2 — Cadastros
- Supermercados.
- Categorias.
- Produtos.
- Descrições alternativas.
- Listagens, formulários, edição e exclusão.

### Fase 3 — Compras
- Cadastro manual de notas.
- Cadastro de itens.
- Detalhes e listagem.
- Upload e armazenamento de imagens.
- Validações.
- Detecção de duplicidade.

### Fase 4 — Inteligência artificial
- Interface do provedor.
- Integração.
- Extração estruturada.
- Validação.
- Tela de conferência.
- Confirmação da compra.
- Tratamento de falhas e reprocessamento.

### Fase 5 — Histórico de preços
- Registros automáticos.
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

### Fase 7 — Qualidade e entrega
- Testes.
- Revisão financeira.
- Tratamento de erros.
- Backup.
- Segurança.
- Documentação.
- Preparação para execução e publicação.

## 14. Prioridades

Ordem de prioridade:

1. Estrutura e banco de dados.
2. Cadastros.
3. Compras manuais.
4. Integração com IA.
5. Histórico de preços.
6. Dashboard.
7. Relatórios.
8. Testes e acabamento.

A aplicação deverá permanecer executável ao final de cada fase. Não desenvolver todas as telas antes de integrar o backend. Não implementar IA antes de existir um fluxo manual funcional.

## 15. Diretrizes gerais

- Priorizar simplicidade e legibilidade.
- Evitar abstrações desnecessárias.
- Não criar funcionalidades fora do escopo sem justificativa.
- Não duplicar regras de negócio entre frontend e backend.
- O backend será responsável pela validação definitiva.
- Utilizar DTOs na comunicação.
- Utilizar migrations para alterações no banco.
- Manter imagens fora do SQLite.
- Não acoplar regras a um provedor específico de IA.
- Manter cadastro manual disponível.
- Nunca confirmar compras automaticamente.
- Preservar rastreabilidade do histórico de preços.
- Escrever testes para operações financeiras.
- Atualizar a documentação quando decisões técnicas mudarem.

**Esta especificação é a fonte principal de verdade do projeto. Toda implementação deverá respeitar suas regras e critérios de aceitação.**
