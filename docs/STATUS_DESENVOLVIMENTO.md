# Status de Desenvolvimento — Mercado no Controle

**Última atualização:** 2026-11-23
**Fase atual:** Fase 2 — Cadastros ✅ concluída
**Próxima etapa:** Fase 3 — Compras

---

## 1. Fases

| Fase | Descrição | Situação |
|---|---|---|
| 1 | Fundação | ✅ Concluída |
| 2 | Cadastros (supermercados, categorias, produtos, descrições alternativas) | ✅ Concluída |
| 3 | Compras (cadastro manual, itens, upload de imagens, duplicidade) | ⏳ Próxima |
| 4 | Inteligência artificial (`IReceiptReader`, conferência, confirmação) | Pendente |
| 5 | Histórico de preços (registro, normalização, comparação, gráficos) | Pendente |
| 6 | Dashboard e relatórios (indicadores, filtros, exportações) | Pendente |
| 7 | Qualidade e entrega (autenticação, backup, segurança, publicação) | Pendente |

## 2. Ambiente verificado

| Ferramenta | Versão |
|---|---|
| .NET SDK | 10.0.404 |
| EF Core / dotnet-ef (ferramenta local) | 10.0.15 |
| Node.js / npm | 26.13.1 / 11.20.0 |
| Angular / Angular Material / CLI | 22.3.0 |
| Vitest | 5.x (runner `@angular/build:unit-test`) |
| xUnit | 2.9.3 |

## 3. Funcionalidades implementadas

### Fase 1 — Fundação

**Backend (`backend/`)**
- Solução `MercadoNoControle.slnx` com `MercadoNoControle.Api` e `MercadoNoControle.Tests`.
- Modelo de domínio completo da especificação (seção 6): `Store`, `Category`, `Product`, `ProductAlias`, `Receipt`, `ReceiptItem`, `PriceObservation`, `ReceiptImport`.
- `AppDbContext` com configurações por entidade (`Data/Configurations`): FKs, índices, unicidades e comportamento de exclusão.
- Persistência monetária por inteiros escalados (`ScaledDecimal`, `ScaledDecimalConverter`).
- Migration `InitialCreate` com as 10 categorias iniciais (seed), aplicada automaticamente na inicialização (`DatabaseInitializer`).
- Caminhos do banco e das imagens configuráveis (`StorageOptions`), fora do código-fonte (`data/`, `uploads/`).
- Tratamento global de exceções com ProblemDetails (`GlobalExceptionHandler`) e exceções de domínio (`NotFoundException` → 404, `ConflictException` → 409, `BusinessRuleException` → 422).
- Enums como texto no JSON e no banco; CORS restrito; HTTPS/HSTS e logs JSON fora de Development; OpenAPI + Swagger UI em Development.
- `GET /api/health`.

**Frontend (`frontend/mercado-no-controle/`)**
- Angular 22 standalone e zoneless, SCSS, Angular Material (tema M3 verde), locale `pt-BR`, moeda BRL.
- Layout principal: sidebar no desktop, menu deslizante em telas pequenas, cabeçalho com título da página e indicador de status da API.
- Menu completo (9.2) com rotas *lazy* por funcionalidade; telas ainda não implementadas exibem a fase prevista.

### Fase 2 — Cadastros

**Backend**
- Validadores (`Validators/`): CNPJ numérico **e alfanumérico** (IN RFB 2.229/2024) com dígitos verificadores; GTIN-8/12/13/14 com dígito verificador; UF; limite de casas decimais (`DecimalPlaces`).
- `TextNormalizer`: limpeza de textos e forma canônica de descrições (sem acento, maiúsculas, espaços simples).
- Paginação, busca e ordenação padronizadas (`PageQuery`, `PagedResult<T>`, `QueryExtensions.ApplySort`) com desempate estável e escape de curingas do `LIKE`.
- **Categorias** — `GET/POST /api/categories`, `GET/PUT/DELETE /api/categories/{id}`: nome único sem diferenciar maiúsculas; exclusão somente sem produtos (409).
- **Supermercados** — `GET/POST /api/stores`, `GET/PUT/DELETE /api/stores/{id}`: CNPJ normalizado e único (aceita máscara); busca por nome, razão social, cidade ou CNPJ; filtro por UF; indicadores (quantidade de compras, total gasto, última compra) calculados **somente com compras confirmadas**; exclusão bloqueada se houver compras (409). `FindByCnpjAsync` pronto para a identificação na importação (Fase 4).
- **Produtos** — `GET/POST /api/products`, `GET/PUT/DELETE /api/products/{id}`: categoria validada (422), código de barras único (409), conteúdo da embalagem com até 4 casas; busca por nome, marca, código e descrições alternativas; filtros por categoria e situação; exclusão bloqueada se houver itens de compra ou histórico (409, com sugestão de inativar).
- **Descrições alternativas** — `POST /api/products/{id}/aliases`, `PUT /api/products/{id}/aliases/{aliasId}/confirm`, `DELETE /api/products/{id}/aliases/{aliasId}`: descrição original preservada, comparação pela forma normalizada, duplicata equivalente no mesmo produto rejeitada (409); a mesma descrição pode pertencer a produtos diferentes; descrições manuais nascem confirmadas.
- Erros de validação retornam os nomes JSON dos campos (camelCase), para o frontend associá-los aos controles.

**Frontend**
- Base compartilhada: `PagedResult`/`toHttpParams`, `NotificationService` (snackbar), `ConfirmService` (diálogo de confirmação), `PageToolbar`, `CnpjPipe`, helpers de formulário (`fieldErrorMessage`, `applyServerErrors`, validadores de formato), `latestValue` (mantém a tabela durante recarregamentos), paginador em português.
- Listagens com `rxResource` (signals): estados de carregamento, vazio (com e sem filtros) e erro com "Tentar novamente".
- **Categorias**: tabela, criação/edição em diálogo, exclusão com confirmação (desabilitada quando há produtos).
- **Supermercados**: busca com *debounce*, filtro por UF, ordenação e paginação no servidor, formulário em diálogo com validação local de formato e exibição dos erros do backend nos campos (CNPJ inválido, CNPJ duplicado).
- **Produtos**: busca, filtros por categoria e situação, ordenação e paginação no servidor, formulário em diálogo; após criar, abre os detalhes.
- **Detalhes do produto** (`/products/:id`): dados do cadastro, edição/exclusão, gestão de descrições alternativas (adicionar, confirmar sugestão, remover), estado "não encontrado"; espaço reservado para o histórico de preços (Fase 5).

## 4. Testes executados (2026-11-23)

| Suíte | Resultado |
|---|---|
| Backend — `dotnet test` | **120 aprovados**, 0 falhas |
| Frontend — `ng test` (Vitest) | **76 aprovados**, 0 falhas |
| Build de produção Angular | OK (inicial ≈ 777 kB, ≈ 164 kB transferidos) |
| Verificação manual no navegador | Ver abaixo |

**Backend — cobertura acumulada**
- Fase 1: conversões escaladas, persistência com as migrations reais, integridade referencial, cascata, health check, CORS.
- Fase 2: validadores (CNPJ numérico/alfanumérico, GTIN, UF, casas decimais), normalização de texto; serviços de categorias, supermercados e produtos (normalização, unicidade, busca, filtros, ordenação, paginação, curingas literais, indicadores só com compras confirmadas e somas exatas, bloqueio de exclusão, descrições alternativas); endpoints HTTP de ponta a ponta (201/204/400/404/409/422, erros por campo em camelCase, parâmetros de paginação inválidos, ordenação desconhecida, enum inválido).

**Frontend — cobertura acumulada**
- Fase 1: roteamento e títulos, `HealthService`, `toApiError`, indicador da API, layout, página provisória.
- Fase 2: serviços HTTP (URLs, métodos, corpo e query string), helpers de formulário e utilitários, página de categorias (carregamento, tabela, vazio, erro + nova tentativa, exclusão com confirmação), página de supermercados (parâmetros, formatação de CNPJ/moeda/data, *debounce*, filtro, ordenação, paginação, erro), formulário de supermercado (obrigatórios, formato de CNPJ, nulos, erros do backend nos campos, 409, edição), formulário de produto (padrões, casas decimais, código de barras, payload, 409, erro geral), detalhes do produto (dados, descrições, adicionar, duplicidade, confirmar, 404).

**Verificação manual (navegador)**: listagem de categorias; cadastro de supermercado com CNPJ inválido (erro do backend exibido no campo) e depois válido; cadastro de produto com categoria, unidade e conteúdo, com redirecionamento aos detalhes; inclusão de descrição alternativa e rejeição de equivalente com acento/caixa diferentes; paginador em português; layout a 375 px. Os registros criados na verificação foram excluídos do banco local.

## 5. Decisões técnicas

| # | Decisão | Justificativa |
|---|---|---|
| D1 | **Código em inglês**, interface em português. Mapeamento: Supermercado→`Store`, Categoria→`Category`, Produto→`Product`, ProdutoDescricao→`ProductAlias`, NotaFiscal→`Receipt`, ItemNotaFiscal→`ReceiptItem`, HistoricoPreco→`PriceObservation`, ProcessamentoIA→`ReceiptImport`. | Consistência com as rotas da API e a estrutura de pastas da especificação. |
| D2 | **Valores numéricos como inteiros escalados (Int64)** no SQLite. Escalas: dinheiro = 2; preço unitário = 4; quantidade = 4; preço normalizado = 4; conteúdo da embalagem = 4 (`Models/DecimalPrecision.cs`). | Especificação 6.4; `REAL` perde precisão. |
| D3 | **O conversor não arredonda silenciosamente**: valor com mais casas que a escala gera erro. Valores calculados devem ser arredondados com `DecimalPrecision.Round` (meio para longe do zero). Entradas da API são validadas antes (`DecimalPlaces`). | Evita perda de precisão invisível. |
| D4 | **Agregações**: `SUM` em SQL é exato (inteiros). **Não usar `AVG` do SQLite**: calcular soma e contagem e dividir em `decimal`. | Especificação 6.4. Validado em teste. |
| D5 | **`ReceiptImport`** com `Guid` próprio e estados `Pending → Processing → AwaitingReview / Failed → Confirmed / Discarded`; `ReceiptId` opcional; importação preservada se a nota for excluída. | Especificação 6.1 e seção 8. |
| D6 | **Rotas de importação** (Fase 4): `POST /api/receipts/upload` → `importId`; `GET /api/receipts/imports/{importId}`; `PUT /api/receipts/imports/{importId}/confirm`; `GET /api/receipts/{id}/processing` lista as importações de uma nota confirmada. | A especificação pede o ajuste ao modelo de rascunho. |
| D7 | **Status da nota**: `Draft` e `Confirmed`; somente `Confirmed` participa de relatórios, dashboard, histórico e dos indicadores de supermercado (RN01, RN09). | Simplicidade. |
| D8 | **Exclusão restritiva** (supermercado com notas, produto com itens/histórico, categoria com produtos), verificada no serviço (409 com mensagem clara) e garantida pela FK `RESTRICT`. Cascata: nota → itens → observação; produto → descrições. | Especificação 6.3 e RN05. |
| D9 | **Unicidade**: CNPJ, chave de acesso e código de barras únicos quando informados; categoria única sem diferenciar maiúsculas (`NOCASE`); descrição alternativa única por produto (forma normalizada). | RN02, 4.3, 4.4. |
| D10 | **CNPJ opcional** no supermercado (ex.: feiras). | Não bloquear compras legítimas. |
| D11 | **`ReceiptItem.Unit` como texto livre**; `Product.Unit` usa o enum da especificação. | Preservar o texto da nota. |
| D12 | **Entidade `Usuario` adiada para a Fase 7**, com a autenticação. Até lá, a API deve rodar apenas em `localhost`. | Evita tabela sem uso; risco documentado no README. |
| D13 | **Datas**: `IssuedAt` no horário local da nota; `CreatedAt`/`UpdatedAt`/`ProcessedAt` em UTC; sem `DateTimeOffset`. | Consultas por período no SQLite. |
| D14 | **Swagger**: OpenAPI nativo + `Swashbuckle.AspNetCore.SwaggerUI`. | Padrão do .NET 10. |
| D15 | **Pacotes EF Core e `Mvc.Testing` fixados em 10.0.x** (a 11.0 exige .NET 11). | .NET 10 LTS. |
| D16 | **Frontend chama `/api` relativo**, com proxy no desenvolvimento. | Sem URLs fixas. |
| D17 | **Apache ECharts** será instalado na Fase 5. | Não carregar dependência sem uso. |
| D18 | **Validação com DataAnnotations** (atributos próprios em `Validators/`) + regras no Service. **FluentValidation não foi adotado**: os atributos cobrem os casos com menos código. | A especificação o trata como opcional. |
| D19 | **CNPJ alfanumérico suportado** (vigente desde jul/2026): 12 caracteres `[0-9A-Z]` + 2 dígitos verificadores, valor de cada caractere = ASCII − 48. Armazenado sem máscara, em maiúsculas. | Novos CNPJs já podem ser alfanuméricos. |
| D20 | **Erros por campo em camelCase** (`SystemTextJsonValidationMetadataProvider`); regras de negócio usam as mesmas chaves (ex.: `categoryId`, `sortBy`). | Associação direta aos controles do formulário. |
| D21 | **Códigos HTTP**: 400 formato/entrada inválida; 404 inexistente; 409 duplicidade ou recurso em uso; 422 regra de negócio (categoria inexistente, ordenação inválida). | Respostas previsíveis para o frontend. |
| D22 | **Paginação**: `page` (inicia em 1), `pageSize` (padrão 20, máx. 100), `search`, `sortBy`, `sortDirection` (`asc`/`desc`). Categorias não são paginadas (lista pequena e usada em seletores). | Especificação seção 8. |
| D23 | **`[Range]` decimal com cultura invariante** (`ParseLimitsInInvariantCulture`). | Corrige erro 500 encontrado nos testes: em máquinas pt-BR, "0.0001" não era interpretado. |
| D24 | **Frontend: `rxResource`** (API pública no Angular 22) para dados de leitura; mutações por `subscribe` nos serviços; formulários reativos; sem NgRx. | Signals conforme a especificação, sem estado global. |
| D25 | **Validação no frontend só de formato** (obrigatório, tamanho, máscara de CNPJ, dígitos, casas decimais). Dígitos verificadores e unicidade ficam no backend, cujos erros são exibidos nos campos. | Não duplicar regras de negócio (seção 15). |

## 6. Funcionalidades pendentes

- Fase 3: cadastro manual de compras (nota + itens), associação de itens a produtos (por descrição alternativa e escolha manual), validação dos totais (RN10), detecção de duplicidade (RN02), upload e armazenamento de imagens, listagem/detalhes/edição/exclusão de notas, histórico de compras do supermercado.
- Fases 4 a 7: conforme a tabela da seção 1.

## 7. Problemas conhecidos

- Nenhum problema crítico.
- A busca textual usa `LIKE` do SQLite, que ignora maiúsculas/minúsculas apenas em ASCII: "laticinios" não encontra "Laticínios" pelo nome (a busca por descrições alternativas já ignora acentos). Avaliar uma coluna normalizada de busca se isso incomodar no uso.
- A ordenação alfabética do SQLite é binária: nomes iniciados por letra acentuada (ex.: "Água") aparecem depois do "Z".
- O bundle inicial cresceu para ≈ 777 kB (≈ 164 kB transferidos) com os componentes Material compartilhados; ainda abaixo do limite de aviso (1 MB). Revisar na Fase 7.
- Fontes e ícones vêm do Google Fonts; sem internet, a interface usa fontes de reserva.
- A API ainda não tem autenticação (D12).

## 8. Próxima etapa — Fase 3 (Compras)

1. Backend: DTOs de nota e itens; `ReceiptService` com criação manual transacional, validação de totais (RN10, com tolerância de arredondamento), alerta de duplicidade (chave de acesso bloqueia; sem chave, aviso por supermercado + data + valor), edição e exclusão consistentes (RN04, RN05), sugestão de produto por descrição alternativa; upload de imagem (tipo/tamanho) para `uploads/`; endpoints `/api/receipts`; testes.
2. Frontend: listagem de notas com filtros; tela "Nova compra" (cadastro manual com itens, totais calculados e divergências sinalizadas); detalhes da nota com imagem; edição e exclusão; testes.
