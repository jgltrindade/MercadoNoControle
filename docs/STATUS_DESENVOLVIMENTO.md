# Status de Desenvolvimento — Mercado no Controle

**Última atualização:** 2026-11-23
**Fase atual:** Fase 7 — Qualidade e entrega ✅ concluída (todas as fases do planejamento concluídas)
**Próxima etapa:** uso real e validações pendentes (seção 8)
**Pendências registradas:** validar a leitura real por IA com uma chave configurada; construir a imagem Docker em um ambiente com o Docker em execução (seção 7).

---

## 1. Fases

| Fase | Descrição | Situação |
|---|---|---|
| 1 | Fundação | ✅ Concluída |
| 2 | Cadastros (supermercados, categorias, produtos, descrições alternativas) | ✅ Concluída |
| 3 | Compras (cadastro manual, itens, upload de imagens, duplicidade) | ✅ Concluída |
| 4 | Inteligência artificial (`IReceiptReader`, conferência, confirmação) | ✅ Concluída |
| 5 | Histórico de preços (registro, normalização, comparação, gráficos) | ✅ Concluída |
| 6 | Dashboard e relatórios (indicadores, filtros, exportações) | ✅ Concluída |
| 7 | Qualidade e entrega (autenticação, backup, segurança, publicação) | ✅ Concluída |

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

### Fase 3 — Compras

**Backend**
- **Notas fiscais** — `GET/POST /api/receipts`, `GET/PUT/DELETE /api/receipts/{id}`, `PUT /api/receipts/{id}/confirm`:
  - Cadastro manual cria um **rascunho**; somente a confirmação explícita registra a compra (RN01).
  - Listagem paginada com busca (número, chave de acesso, supermercado), filtros por supermercado, situação e período (`from`/`to`, inclusive) e ordenação (padrão: mais recentes primeiro).
  - Itens incluídos, alterados (pelo `id`) e removidos na mesma requisição; a descrição original da nota é preservada.
  - Validação de referências (supermercado e produtos existentes → 422; item de outra nota → 422).
- **Consistência dos valores (RN10)** — `ReceiptConsistency`: total do item, soma dos itens × valor dos produtos, descontos dos itens × desconto da nota, total × produtos − descontos + acréscimos (tolerância de R$ 0,01), e CNPJ da chave de acesso × CNPJ do supermercado. Os alertas acompanham os detalhes da nota.
- **Duplicidade (RN02)** — chave de acesso validada (44 caracteres, dígito verificador, aceita CNPJ alfanumérico) e única: bloqueia (409 com `existingReceiptId`). Sem chave, notas confirmadas do mesmo supermercado com mesmo número e série, ou mesma data e mesmo total, são **possíveis duplicatas** — só alertam.
- **Confirmação** — transacional; com alertas, responde 409 com `warnings` e `possibleDuplicates` até o usuário confirmar com `acknowledgeWarnings: true`. Exige ao menos um item.
- **Histórico de preços (RN03, RN04, RN05)** — `PriceObservationRecorder` mantém uma observação por item confirmado e associado a produto; editar uma compra confirmada atualiza apenas as observações dela; excluir remove itens e observações em cascata. O preço normalizado fica para a Fase 5.
- **Associação de itens** — ao confirmar, cada associação item → produto vira descrição alternativa confirmada (origem: nota fiscal). `GET /api/products/match?description=` devolve os produtos associados a uma descrição (base da sugestão automática).
- **Imagens** — `POST/GET/DELETE /api/receipts/{id}/image`: JPEG, PNG ou WebP identificados pela assinatura do arquivo (não pelo nome/cabeçalho), até 10 MB (configurável em `Uploads:MaxImageSizeBytes`), nome gerado pelo servidor em `uploads/receipts/AAAA/MM/`, proteção contra caminhos fora do diretório, `X-Content-Type-Options: nosniff`.
- `ConflictException` passou a levar extensões no ProblemDetails.
- Datas de auditoria (`CreatedAt`, `UpdatedAt`, `ProcessedAt`) voltam do SQLite marcadas como UTC (sufixo "Z" no JSON).

**Frontend**
- `SearchPicker`: seletor com busca no servidor, usado como controle de formulário, com opção "cadastrar novo".
- **Notas fiscais** (`/receipts`): busca, filtro por supermercado (também via `?storeId=`, a partir de "Ver compras" em Supermercados), período (datepicker em pt-BR), situação, ordenação e paginação; indicação de itens sem produto e de imagem anexada; exclusão com confirmação.
- **Nova compra / edição** (`/receipts/new`, `/receipts/:id/edit`):
  - Dados da nota, itens com total calculado (quantidade × preço − desconto) e totais calculados a partir dos itens (desligável para digitar os valores impressos).
  - Resumo com divergências em tempo real.
  - Sugestão automática do produto pela descrição (somente com associação única e confirmada; ambiguidade é sinalizada).
  - Cadastro rápido de supermercado/produto sem sair da tela; imagem opcional.
  - "Salvar rascunho" ou "Salvar e confirmar compra"; os alertas do backend aparecem em diálogo antes de confirmar. Erros do backend aparecem nos campos, inclusive nos itens.
- **Detalhes da compra** (`/receipts/:id`): cabeçalho, situação, alertas e possíveis duplicatas (com links), itens com produto ou "não identificado", totais, imagem (anexar, substituir, remover) e ações de editar, confirmar e excluir.
- Adaptador de datas pt-BR (`dd/MM/aaaa` digitado corretamente).

### Fase 4 — Inteligência artificial

**Backend**
- **`IReceiptReader`** (`Services/Ai`): contrato da especificação (5.4) mais nome do provedor/modelo e disponibilidade. `ReceiptExtraction` segue o contrato de retorno 5.3 (`store`, `receipt`, `items`, com campos nulos quando não lidos) e acrescenta `isReceipt`/`unreadableReason`.
- **`ClaudeReceiptReader`** — SDK oficial `Anthropic` 13.12.0, modelo `claude-opus-5-5` (configurável), imagem em base64 e **saída estruturada** (`output_config.format` com esquema JSON estrito); esforço `medium`; tempo limite configurável (180 s); 2 novas tentativas automáticas do SDK para 429/5xx; fallback do lado do servidor para recusas (`server-side-fallback-2026-07-01`, modo `default`). Erros do SDK traduzidos em categorias (`NotConfigured`, `InvalidImage`, `Unavailable`, `Timeout`, `InvalidResponse`, `Refused`) com mensagens em português; `stop_reason` `refusal`/`max_tokens` e JSON inválido tratados.
- **Prompt** (`ReceiptExtractionPrompt`): transcrever somente o legível, nunca inventar, números como impressos, ignorar itens cancelados, `totalPrice` bruto da linha e desconto separado (o líquido é calculado no backend, em `decimal`).
- **Sem chave** (`UnavailableReceiptReader`): a leitura informa a indisponibilidade; upload, conferência e cadastro manual continuam funcionando. Escolha do provedor por configuração (`ReceiptReader:Provider`).
- **Importações** (`Services/Imports`):
  - `POST /api/receipts/upload` valida e armazena a imagem, cria a `ReceiptImport` (`Pending`) e responde 202 com o identificador.
  - A leitura roda em segundo plano (`Channel` + `BackgroundService`, uma por vez); na inicialização, leituras interrompidas voltam para a fila.
  - Estados: `Pending → Processing → AwaitingReview | Failed`; depois `Confirmed` ou `Discarded`. Registra provedor, modelo, JSON retornado, erro, tentativas e data.
- **Validação do resultado** (`ReceiptDraftBuilder`): CNPJ e chave de acesso validados (inválidos não são preenchidos e o alerta mostra o que foi lido), data no formato esperado, supermercado identificado pelo CNPJ, produtos sugeridos somente por descrição alternativa confirmada única, alertas de valores ausentes, casas decimais excedentes e consistência (RN10).
- **Endpoints**: `GET /api/receipts/imports/reader` (disponibilidade), `GET /api/receipts/imports` (em aberto), `GET /api/receipts/imports/{id}` (status + rascunho), `GET .../image`, `POST .../reprocess`, `PUT .../discard`, `PUT .../confirm` e `GET /api/receipts/{id}/processing`.
- **Confirmação** (`ReceiptService.CreateConfirmedFromImportAsync`): em uma única transação cria a nota já confirmada com a imagem da importação, itens, histórico de preços e descrições aprendidas, e vincula a importação. Com alertas e sem ciência → 409 e **nada é gravado**. Leitura com falha pode ser concluída manualmente com a mesma imagem.

**Frontend**
- **Nova compra**: cartão "Ler nota por foto" (JPEG/PNG/WebP até 5 MB, câmera no celular), aviso quando a leitura está indisponível e lista de leituras em aberto.
- **Conferência** (`/receipts/imports/:importId`):
  - Acompanha a leitura (consulta periódica sem sobreposição).
  - Ao concluir, mostra a imagem ao lado do formulário de compra pré-preenchido, com alertas da leitura, campos não lidos destacados, produtos sugeridos ("confira") e ambiguidades sinalizadas. Oferece cadastrar o supermercado lido da nota já pré-preenchido.
  - Confirma pelo mesmo diálogo de alertas da Fase 3.
  - Em falha: "Tentar ler novamente", "Cadastrar manualmente com esta imagem" ou "Descartar".
- O formulário de compra passou a usar *container queries* (adapta-se à largura disponível, inclusive na coluna da conferência).

### Fase 5 — Histórico de preços

**Backend**
- **Preço normalizado (RN08)** — `PriceNormalizer`: preço por kg, litro ou unidade = total pago pelo item ÷ medida comprada.
  - Item vendido por peso/volume (unidade da nota KG/G/L/ML e variações como "GR", "LT"): a quantidade já é a medida.
  - Item vendido por embalagem: medida = quantidade × conteúdo da embalagem do produto (G→kg, ML→L, DZ→12 un).
  - Não normaliza com conteúdo desconhecido, unidades incompatíveis ou produtos em CX/PCT. Arredondamento explícito a 4 casas.
- **Registro automático** — `PriceObservationRecorder` grava o preço normalizado ao confirmar/editar compras. Alterar unidade ou conteúdo de um produto recalcula só o histórico dele, na mesma transação. Na inicialização, um recálculo idempotente atualiza observações antigas (D30).
- **`GET /api/products/{id}/prices`** (`from`, `to`, `storeId`): compras do produto com preço pago efetivo (total ÷ quantidade, considera descontos) e normalizado. Indicadores calculados em `decimal`: último, menor, maior (com data e loja), médio, variação % entre a primeira e a última compra do período e, quando todas as compras têm a mesma unidade-base, último/menor/médio normalizados.
- **`GET /api/products/{id}/comparison`**: por supermercado, último/menor/médio preço e diferença % para o mais barato; e, para referência, produtos **da mesma categoria** com preço normalizado na **mesma unidade-base** (não compartilham histórico — RN07).
- **Associação posterior de itens (RN06)**:
  - `GET /api/price-history/unassigned-items` agrupa itens de compras confirmadas sem produto pela descrição normalizada (ocorrências, total, lojas, sugestão quando há uma descrição alternativa confirmada única).
  - `POST /api/price-history/assign` associa todos os itens do grupo ao produto em uma transação, gera o histórico e registra a descrição alternativa.

**Frontend**
- Apache ECharts 6 + ngx-echarts 22, carregados sob demanda somente com os módulos usados (linha, barra, grade, legenda, tooltip): ≈ 160 kB transferidos, apenas quando um gráfico aparece.
- **Histórico de preços** (`/price-history?productId=`):
  - Busca de produto e período (3, 6, 12 meses ou todo).
  - Indicadores e evolução do preço em linha, uma série por supermercado, com alternância "Preço pago" / "Por kg/L/un" quando todas as compras são normalizáveis.
  - Comparação entre supermercados em barras com tabela, produtos da mesma categoria e tabela das compras com links para as notas.
  - Seção "Itens sem produto" com diálogo de associação (com cadastro rápido de produto).
- **Detalhes do produto**: resumo do histórico (indicadores) e link para a evolução completa.
- Gráficos seguem as diretrizes de visualização: paleta categórica validada para daltonismo em ordem fixa, cor atrelada à loja (não ao ranking), eixo único, linhas de 2 px com marcadores ≥ 8 px, legenda quando há mais de uma série e rótulo direto até 4 séries, rótulo apenas no menor preço das barras, tooltip, descrição acessível e tabela com os mesmos dados.

### Fase 6 — Dashboard e relatórios

**Backend**
- **`AnalyticsData`** — leitura única das compras confirmadas (RN09) usada pelo dashboard e pelos relatórios, para que os dois partam dos mesmos dados.
  - Período padrão: últimos 12 meses (do 1º dia do mês até hoje). Início depois do fim → 422.
  - Filtros: período (`from`/`to`, inclusivos), supermercado e categoria. Com filtro de categoria, o valor de cada compra é a soma dos itens da categoria (D51).
  - Somas e médias em `decimal`; meses sem compra aparecem zerados.
- **Dashboard** — `GET /api/dashboard`: total e quantidade de compras no período, ticket médio, maior compra (com link para a nota), supermercado com maior gasto, total do mês atual e do ano atual. Também `/monthly`, `/stores`, `/categories` e `/products?top=` (participação %).
- **Relatórios** — `GET /api/reports/{monthly|stores|categories|products|history|comparison}` devolvem uma tabela genérica (`ReportTable`: título, texto dos filtros, notas, colunas tipadas e linhas, com linhas de total).
  - Mensal: subtotais por ano e "Total do período" quando há mais de um ano.
  - Por supermercado: compras, total, ticket médio e participação.
  - Por categoria: total e participação, incluindo "Itens sem produto associado" e "Ajustes da nota" (D52).
  - Produtos: ranking por maior gasto, mais comprados ou maior aumento de preço (preço pago na primeira e na última compra do período; mínimo de duas compras).
  - Histórico de compras do período.
  - Comparação entre períodos por categoria: padrão é o período imediatamente anterior, com a mesma duração.
- **Exportações** — `GET /api/reports/export?report=&format=csv|xlsx|pdf` exporta exatamente a mesma tabela exibida na tela, com os mesmos filtros. Nome do arquivo: `mercado-no-controle-{relatório}-{aaaaMMdd}`.
  - CSV com `;` e vírgula decimal, UTF-8 com BOM (D53).
  - Excel (ClosedXML): título, filtros, notas, cabeçalho congelado e formatos numéricos.
  - PDF (QuestPDF): paisagem acima de 5 colunas, rodapé com data de geração e paginação.

**Frontend**
- **Filtros compartilhados** (`AnalyticsFilters`): período pré-definido (este mês, 3/6/12 meses, este ano, ano anterior) ou personalizado; supermercado por busca; categoria; "Limpar filtros". O estado fica na URL, então links e recarregamentos preservam os filtros.
- **Dashboard** (`/dashboard`):
  - Indicadores, com o total do período em destaque.
  - Gráficos: evolução mensal (colunas), distribuição por supermercado, por categoria e produtos com maior gasto (barras horizontais ordenadas), e comparativo mês anterior × mês atual por categoria (barras agrupadas com legenda).
  - Cada gráfico tem link "Ver relatório", que abre a tabela correspondente com os mesmos filtros.
  - Estados de vazio e de erro com nova tentativa.
- **Relatórios** (`/reports`): seletor de relatório e de ranking, mesmos filtros, tabela formatada por tipo de coluna (moeda, %, datas pt-BR, linhas de total em negrito) e botões CSV, Excel e PDF.
- Paleta, eixo único e rótulos seletivos seguem as mesmas regras da Fase 5. Rótulos dos eixos sem ",00" nas marcas inteiras, e marcas sobrepostas são ocultadas.

### Fase 7 — Qualidade e entrega

**Segurança e acesso (backend)**
- **Usuário único** (`AppUser`, tabela `Users`, migration `AddUsers`): nome, e-mail (único, sem diferenciar maiúsculas), hash da senha (PBKDF2 com salt do `PasswordHasher` do ASP.NET Core), datas de criação, troca de senha e último acesso.
- **`/api/auth`**:
  - `status` e `login` (com "manter conectado").
  - `setup`: cadastro inicial, que inicia a sessão; aceito só a partir do próprio computador, salvo `AllowRemoteSetup`.
  - `logout`, `password` (exige a senha atual e encerra as outras sessões) e `profile`.
- **Sessão por cookie** `HttpOnly` + `SameSite=Strict`, 30 dias com renovação. A sessão é validada a cada requisição contra a data da última troca de senha. As chaves de proteção ficam em `data/keys`, protegidas por DPAPI no Windows.
- **Toda a API exige sessão** (política padrão), exceto `health` e `auth`. Respostas 401/403 em ProblemDetails (sem redirecionamento).
- **Limite de tentativas:** 10 por 5 minutos por IP em login e cadastro (429).
- **Cabeçalhos de segurança:**
  - `Content-Security-Policy`: `default-src 'none'` na API; política própria para a interface publicada.
  - `X-Frame-Options`, `nosniff`, `Referrer-Policy`, `Permissions-Policy` e `Cross-Origin-Opener-Policy`.
- **Limite de 2 MB por requisição** (envios de imagem mantêm 10 MB).
- **Suporte a proxy reverso** (`ReverseProxy:Enabled`, cabeçalhos `X-Forwarded-*`).
- **Recuperação de acesso** (`Authentication:ResetUser`): remove apenas o usuário, para refazer o cadastro inicial.

**Configurações, backup e imagens (backend)**
- `GET /api/settings/system`: versão, ambiente, autenticação, caminho e tamanho do banco, diretório de imagens, contagem de registros e leitor de IA (provedor, modelo, esforço, tempo máximo e motivo da indisponibilidade). A chave nunca é exposta.
- `GET /api/settings/backup`: `.zip` com uma cópia consistente do banco (`VACUUM INTO`), todas as imagens e um `manifest.json`. É gerado em arquivo temporário apagado ao fim do envio.
- `GET /api/settings/export`: `.zip` com CSVs de supermercados, categorias, produtos, compras, itens e histórico de preços (mesmo formato dos relatórios, D53).
- `GET /api/settings/images` e `DELETE /api/settings/images/orphans`: imagens armazenadas, em uso, órfãs e referências a arquivos ausentes.
  - A exclusão recalcula a lista no servidor e remove pastas vazias.
  - Imagens de leituras em andamento (pendente, processando, aguardando conferência, falha) nunca são órfãs.

**Publicação**
- A API serve a interface compilada (`Spa:RootPath`, padrão `wwwroot`) na mesma origem.
  - Rotas do Angular devolvem `index.html` (`no-cache`); arquivos com hash recebem cache de 1 ano (`immutable`).
  - Rotas `/api` desconhecidas continuam 404.
- `appsettings.Production.json`: banco e imagens dentro da pasta publicada (recomendado sobrescrever por variáveis de ambiente) e CORS vazio (mesma origem).
- `scripts/publish.ps1`: compila o Angular em produção, executa `dotnet publish` e copia a interface para `wwwroot`.
- `Dockerfile` (multiestágio, usuário sem privilégios, volumes `/data` e `/uploads`) e `docker-compose.yml` (porta só em `127.0.0.1`).
- Build Angular sem *critical CSS inline*: o script inline gerado seria bloqueado pela CSP (`script-src 'self'`) e a página ficaria sem estilos (D61).

**Frontend**
- **Login / primeiro acesso** (`/login`, fora do layout principal):
  - Login com "manter conectado" e mostrar/ocultar senha.
  - Cadastro inicial com confirmação de senha.
  - Volta para a página pedida (`returnUrl`, somente caminhos internos).
- **Guardas de rota e interceptador:**
  - Sem sessão, a navegação vai para o login.
  - Com a autenticação desligada, a aplicação abre direto.
  - Com a API fora do ar, as telas mostram o erro de conexão.
  - Um 401 durante o uso leva ao login, preservando a tela atual.
- **Menu da conta** no cabeçalho (nome, e-mail, Configurações, Sair).
- **Configurações** (`/settings`), especificação 4.8:
  - Conta: dados e troca de senha.
  - Preferências de exibição: período padrão do dashboard/relatórios e itens por página, guardados no navegador.
  - Leitura por IA: situação e orientação para configurar a chave.
  - Backup e exportação, com instruções de restauração.
  - Imagens das notas: totais, órfãs e exclusão com confirmação.
  - Sobre a instalação, com link para Categorias.
- Página provisória removida; utilitário compartilhado de download (`shared/utils/download.ts`).

**Revisão financeira**
- Nenhum `double`/`float` em valores: só a contagem de páginas usa `double`.
- Todas as divisões arredondam explicitamente com `DecimalPrecision.Round` (meio para longe do zero) e tratam divisor zero (participação, ticket médio, variação, médias, preço normalizado).
- Nenhum `AVG` do SQLite (D4).
- No frontend, cálculos monetários só pré-preenchem campos. O backend valida as casas decimais e é a fonte de verdade (D26).

## 4. Testes executados (2026-11-23)

| Suíte | Resultado |
|---|---|
| Backend — `dotnet test` | **260 aprovados**, 0 falhas |
| Frontend — `ng test` (Vitest) | **195 aprovados**, 0 falhas |
| Build de produção Angular | OK (inicial ≈ 847 kB, ≈ 177 kB transferidos; ECharts em chunk sob demanda ≈ 160 kB; telas em chunks próprios) |
| Publicação (`scripts/publish.ps1`) | OK — aplicação publicada executada em modo Production e verificada no navegador |
| Imagem Docker | **Não construída** — o Docker está instalado, mas o serviço não estava em execução neste ambiente |
| Verificação manual no navegador | Ver abaixo |
| Chamada real à API da Anthropic | **Não executada** — não há chave configurada neste ambiente (seção 7) |

**Backend — cobertura acumulada**
- Fase 1: conversões escaladas, persistência com as migrations reais, integridade referencial, cascata, health check, CORS.
- Fase 2: validadores (CNPJ numérico/alfanumérico, GTIN, UF, casas decimais), normalização de texto; serviços de categorias, supermercados e produtos (normalização, unicidade, busca, filtros, ordenação, paginação, curingas literais, indicadores só com compras confirmadas e somas exatas, bloqueio de exclusão, descrições alternativas); endpoints HTTP de ponta a ponta (201/204/400/404/409/422, erros por campo em camelCase, parâmetros de paginação inválidos, ordenação desconhecida, enum inválido).
- Fase 3: chave de acesso (formato, DV, CNPJ); consistência dos valores (todos os alertas, tolerância de truncamento, desconto geral e acréscimos); serviço de compras (rascunho sem histórico, referências inválidas, chave duplicada, confirmação exige itens, alertas exigem ciência, possíveis duplicatas sem bloqueio e só no mesmo supermercado, histórico gerado só para itens associados, descrições aprendidas, edição preservando ids e ressincronizando o histórico, associação posterior, exclusão removendo histórico, indicadores do supermercado, filtros/período/ordenação, imagem); armazenamento de imagens (assinatura, vazio, tamanho, fluxo maior que o declarado, caminho fora do diretório); endpoints (fluxo completo com 409 de alertas, erros por item, upload multipart, rejeição de arquivo falso, imagem servida com `nosniff`); datas de auditoria em UTC.
- Fase 4 (com leitor simulado, isolado no projeto de testes): esquema da saída estruturada (objetos estritos, nomes do contrato), interpretação da resposta (decimais exatos, recusa, `max_tokens`, vazio, JSON inválido), limite de 5 MB e formato antes de chamar a API, escolha do leitor por configuração; rascunho (supermercado por CNPJ, não cadastrado, CNPJ/chave/data inválidos sem preenchimento, valores ausentes, inconsistências, total líquido do item, sugestão única × ambígua, JSON ilegível); importações (upload e fila, leitura bem-sucedida sem registrar compra, cada tipo de falha, erro inesperado sem vazar detalhes, imagem que não é nota, processamento só de pendentes, reprocessamento, confirmação transacional com imagem e vínculo, alertas sem gravação, conclusão manual após falha, estados que impedem confirmar, descarte, status do leitor); ponta a ponta HTTP (enviar → processar em segundo plano → conferir → confirmar → compra com imagem e `processing`; sem leitor configurado; arquivo falso; importação inexistente).
- Fase 5: normalização (embalado por unidade, granel em KG/G/L/ML e variações, dúzia, pacote com conteúdo; conteúdo desconhecido, CX/PCT, unidades incompatíveis e quantidade inválida sem normalizar), preço efetivo, gravação na confirmação, recálculo restrito ao produto alterado, recálculo idempotente na inicialização, indicadores exatos (média de 3 preços, variação, menor/maior com loja e data, normalizados), filtros de período e loja, produto sem compras, período inválido, comparação por loja com diferença % e produtos comparáveis só da mesma categoria/unidade, agrupamento de itens sem produto (acentos/caixa, rascunhos excluídos, sugestão única), associação posterior com histórico e descrição alternativa; ponta a ponta HTTP (compra → histórico R$/kg → comparação → associação posterior → histórico do novo produto; 404/422/400).
- Fase 6:
  - Período padrão e período inválido; somente compras confirmadas.
  - Indicadores exatos (total, ticket médio, maior compra, supermercado com maior gasto, mês e ano atuais) e meses sem compra zerados.
  - Filtros de supermercado e de categoria (soma só dos itens da categoria).
  - Distribuição por categoria fechando com o total, incluindo itens sem produto e ajustes da nota.
  - Rankings de produtos (gasto, quantidade, aumento de preço) e comparação com o período anterior.
  - Consistência: dashboard = relatório mensal = soma das compras.
  - Exportações: CSV com BOM, `;` e vírgula decimal; Excel legível pelo ClosedXML; PDF válido.
  - Ponta a ponta HTTP: dashboard, relatórios, exportações com cabeçalhos e nome do arquivo, 422 e 400.
- Fase 7:
  - Autenticação:
    - Primeiro acesso exige cadastro e protege a API (401 em ProblemDetails; `health` aberto).
    - Cadastro grava hash (não a senha) e inicia sessão com cookie `HttpOnly`/`SameSite=Strict`; segundo cadastro → 409.
    - Cadastro a partir de outro IP → 403; senha curta → 400.
    - Logout; login errado/inexistente → 401 com mensagem genérica; e-mail sem diferenciar maiúsculas.
    - Troca de senha: senha atual errada → 422 no campo; outras sessões encerradas; senha antiga recusada.
    - Edição do perfil; 11ª tentativa de login → 429, sem afetar outro IP.
    - Cabeçalhos de segurança; recuperação removendo só o usuário.
  - Configurações:
    - Informações da instalação, sem a chave.
    - Backup extraído: o banco abre sozinho com os dados; imagem idêntica; manifesto.
    - CSVs por entidade (aspas com `;`, vírgula decimal, data e hora).
    - Imagens órfãs (imagem removida, nota excluída) listadas e excluídas sem afetar a imagem em uso.
  - Publicação:
    - Interface servida com cache correto e CSP.
    - Rotas do Angular → `index.html`; `/api` desconhecida → 404.
  - **Roteiro ponta a ponta da especificação (seção 11), com login ativo e leitor simulado:** enviar imagem → processar → conferir (associação de produtos) → confirmar → persistência → dashboard → histórico (R$ 27,90; R$ 5,58/kg) → editar (dashboard, histórico e relatórios mensal/supermercado/categoria atualizados para R$ 38,53) → excluir (tudo zerado).

**Frontend — cobertura acumulada**
- Fase 1: roteamento e títulos, `HealthService`, `toApiError`, indicador da API, layout, página provisória.
- Fase 2: serviços HTTP (URLs, métodos, corpo e query string), helpers de formulário e utilitários, página de categorias (carregamento, tabela, vazio, erro + nova tentativa, exclusão com confirmação), página de supermercados (parâmetros, formatação de CNPJ/moeda/data, *debounce*, filtro, ordenação, paginação, erro), formulário de supermercado (obrigatórios, formato de CNPJ, nulos, erros do backend nos campos, 409, edição), formulário de produto (padrões, casas decimais, código de barras, payload, 409, erro geral), detalhes do produto (dados, descrições, adicionar, duplicidade, confirmar, 404).
- Fase 3: arredondamento e total do item, mapeamento de caminhos de erro, formato da chave, adaptador de datas, serviço HTTP de compras (inclusive upload), fluxo de confirmação com alertas (aceitar, recusar, outros erros), `SearchPicker` (busca, seleção, limpeza ao digitar, cadastrar novo, desabilitado), listagem (ordenação padrão, filtro inicial por supermercado, situação, período, vazio, erro, exclusão), formulário (cálculos, divergência, sugestão única e ambígua, validação, data/hora sem fuso, rascunho, confirmação aceita e recusada com atualização na nova tentativa, erros nos itens, chave duplicada, imagem inválida e envio após salvar, edição preservando totais impressos, 404), detalhes (dados, alertas, itens não identificados, confirmação, remoção de imagem, 404).
- Fase 4: serviço de importações, cartão de envio (upload e navegação, limite de 5 MB, formato, leitor indisponível, leituras em aberto), página de conferência (consulta periódica até concluir e parada, formulário pré-preenchido, falha com nova tentativa/cadastro manual/descarte, compra registrada, 404), formulário em modo conferência (pré-preenchimento com totais impressos e campos vazios, cadastro do supermercado lido, confirmação pela importação, recusa dos alertas, cadastro manual sem leitura, descarte), pré-preenchimento do cadastro de supermercado, `confirmWith`.
- Fase 5: opções dos gráficos como funções puras (cor por loja em ordem fixa sem ciclar, séries por loja, legenda/rótulos diretos, métrica normalizada, barras ordenadas com rótulo só no menor), períodos, indicadores, página de histórico (vazio, carregamento por `?productId=`, gráfico com descrição acessível, comparação, alternativas, compras, troca de métrica, refetch ao mudar o período, produto sem compras, recarga após associação), diálogo de associação.
- Fase 6:
  - Construtores de gráficos: ordenação, rótulo seletivo, eixo único, paleta em ordem fixa e cor "outras" a partir da 9ª série.
  - Períodos pré-definidos e leitura/escrita dos filtros na URL, com valores inválidos voltando ao padrão.
  - Rótulos de mês e alinhamento do comparativo mês anterior × mês atual.
  - Serviço: query string e nome do arquivo exportado, com reserva.
  - Formatação das células por tipo.
  - Dashboard: indicadores, gráficos e links com filtros, vazio, troca de filtros e erro.
  - Relatórios: tabela com linha de total, relatório/ranking pela URL e sincronização, exportação com os mesmos filtros, erro na exportação.
- Fase 7:
  - `AuthService`: status carregado uma vez, login, logout, senha e perfil.
  - Guardas: redirecionamento com `returnUrl`; sessão ativa, autenticação desligada e API fora do ar abrem a aplicação; login bloqueado com sessão ativa.
  - Interceptador: 401 → login, exceto nas rotas de autenticação.
  - `safeReturnUrl` contra redirecionamento aberto.
  - Página de login: entrar e voltar à página pedida, credenciais erradas, cadastro com validação de senha e confirmação, recusa do cadastro remoto.
  - Rotas: sem sessão → login com `returnUrl`; primeiro acesso; autenticação desligada.
  - Menu da conta e saída.
  - Preferências (persistência, valores inválidos, leitura inicial) e período padrão aplicado aos filtros.
  - Nome de arquivo do `Content-Disposition`, tamanho legível, serviço de configurações.
  - Página de configurações: informações, IA, imagens, conta oculta sem autenticação, preferências, exclusão de órfãs com confirmação e recarga, backup, troca de senha e perfil.

**Verificação manual (navegador)**
- Fase 2: listagem de categorias; cadastro de supermercado com CNPJ inválido (erro do backend exibido no campo) e depois válido; cadastro de produto com redirecionamento aos detalhes; inclusão de descrição alternativa e rejeição de equivalente com acento/caixa diferentes; paginador em português; layout a 375 px.
- Fase 3: nova compra com seletor de supermercado por busca; descrição "leite int 1l" associada automaticamente ao produto; total do item e totais da nota calculados; total divergente digitado → aviso em tempo real → diálogo com o alerta do backend → confirmação; detalhes com alertas, item não identificado e imagem anexada; listagem filtrada por `?storeId=`; edição carregando a compra confirmada. Corrigido durante a verificação: horários de auditoria exibidos 3 h adiantados (datas UTC sem "Z").
- Fase 4 (sem chave configurada): Nova compra informa que a leitura está indisponível; imagem enviada → leitura falha com a mensagem de configuração → "Cadastrar manualmente com esta imagem" → compra confirmada com a imagem e vínculo em `processing`. Corrigidos durante a verificação: texto duplicado no aviso, formulário espremido na coluna da conferência (passou a usar *container queries*) e totais automáticos desligados no cadastro manual após falha.
- Fase 5: compras de teste em duas lojas → histórico com indicadores (médio R$ 5,35; variação +25,05%), gráfico de linhas por loja com legenda e rótulos, barras com o menor preço rotulado, tabela de comparação ("Mais barato", +9,11%), produto da mesma categoria por litro, item sem produto associado pelo diálogo (descrição aprendida aparece no produto), resumo nos detalhes do produto.
- Fase 6:
  - Dados de teste: 5 compras confirmadas em 2 lojas, de agosto a novembro.
  - Dashboard: total de R$ 237,65, 5 compras, ticket médio de R$ 47,53, maior compra de R$ 51,57 e Mercado Teste A com maior gasto. Evolução mensal com 12 meses e os meses sem compra zerados; distribuições, produtos e comparativo entre meses corretos.
  - Relatórios: mensal com subtotal anual; produtos por maior aumento (Arroz +20,08%); comparação com o período anterior de mesma duração (+4,46%); troca de relatório pelo seletor, atualizando a URL.
  - Exportações baixadas pela API: CSV (BOM, `;`, vírgula decimal), xlsx válido e PDF conferido visualmente.
  - Corrigidos durante a verificação: rótulos do eixo de valores sobrepostos nos gráficos estreitos, e cor do indicador em destaque (verde-limão forte, trocado pelo contêiner secundário do tema).
- Fase 7 (aplicação **publicada** pelo script, em modo Production, com banco temporário):
  - Interface servida pela API com a CSP restritiva: estilos e fontes carregados, sem erros no console.
  - `/settings` sem sessão → login com `returnUrl` → primeiro acesso → cadastro → volta para Configurações.
  - Configurações exibindo conta, preferências, IA indisponível com o motivo, backup/exportação, imagens e instalação.
  - Backup e exportação gerados pela sessão (`.zip` válidos, nomes com data e hora).
  - Sair pelo menu da conta → link protegido para relatório → login. Senha errada mostra "E-mail ou senha incorretos."; a correta volta ao relatório pedido.
  - Período padrão "Este ano" aplicado ao abrir o dashboard.
  - Corrigidos durante a verificação:
    - Arquivos JS/CSS respondidos com o `index.html`: a rota de fallback era escolhida antes dos arquivos estáticos; agora `UseRouting` vem depois.
    - Script inline do *critical CSS* bloqueado pela CSP.
    - Tamanho do banco sem o arquivo `-wal`.
    - Acentos corrompidos na saída do script de publicação no Windows PowerShell 5.1 (arquivo salvo com BOM).
- Os registros e as imagens criados nas verificações foram excluídos do banco e da pasta de uploads locais.

## 5. Decisões técnicas

| # | Decisão | Justificativa |
|---|---|---|
| D1 | **Código em inglês**, interface em português. Mapeamento: Supermercado→`Store`, Categoria→`Category`, Produto→`Product`, ProdutoDescricao→`ProductAlias`, NotaFiscal→`Receipt`, ItemNotaFiscal→`ReceiptItem`, HistoricoPreco→`PriceObservation`, ProcessamentoIA→`ReceiptImport`. | Consistência com as rotas da API e a estrutura de pastas da especificação. |
| D2 | **Valores numéricos como inteiros escalados (Int64)** no SQLite. Escalas: dinheiro = 2; preço unitário = 4; quantidade = 4; preço normalizado = 4; conteúdo da embalagem = 4 (`Models/DecimalPrecision.cs`). | Especificação 6.4; `REAL` perde precisão. |
| D3 | **O conversor não arredonda silenciosamente**: valor com mais casas que a escala gera erro. Valores calculados devem ser arredondados com `DecimalPrecision.Round` (meio para longe do zero). Entradas da API são validadas antes (`DecimalPlaces`). | Evita perda de precisão invisível. |
| D4 | **Agregações**: `SUM` em SQL é exato (inteiros). **Não usar `AVG` do SQLite**: calcular soma e contagem e dividir em `decimal`. | Especificação 6.4. Validado em teste. |
| D5 | **`ReceiptImport`** com `Guid` próprio e estados `Pending → Processing → AwaitingReview / Failed → Confirmed / Discarded`; `ReceiptId` opcional; importação preservada se a nota for excluída. | Especificação 6.1 e seção 8. |
| D6 | **Rotas de importação**: `POST /api/receipts/upload` → 202 com o id; `GET /api/receipts/imports[/{id}]`; `GET .../image`; `POST .../reprocess`; `PUT .../discard`; `PUT .../confirm`; `GET /api/receipts/imports/reader`; `GET /api/receipts/{id}/processing`. | A especificação pede o ajuste ao modelo de rascunho. |
| D7 | **Status da nota**: `Draft` e `Confirmed`; somente `Confirmed` participa de relatórios, dashboard, histórico e dos indicadores de supermercado (RN01, RN09). | Simplicidade. |
| D8 | **Exclusão restritiva** (supermercado com notas, produto com itens/histórico, categoria com produtos), verificada no serviço (409 com mensagem clara) e garantida pela FK `RESTRICT`. Cascata: nota → itens → observação; produto → descrições. | Especificação 6.3 e RN05. |
| D9 | **Unicidade**: CNPJ, chave de acesso e código de barras únicos quando informados; categoria única sem diferenciar maiúsculas (`NOCASE`); descrição alternativa única por produto (forma normalizada). | RN02, 4.3, 4.4. |
| D10 | **CNPJ opcional** no supermercado (ex.: feiras). | Não bloquear compras legítimas. |
| D11 | **`ReceiptItem.Unit` como texto livre**; `Product.Unit` usa o enum da especificação. | Preservar o texto da nota. |
| D12 | **Entidade `Usuario` adiada para a Fase 7**, com a autenticação (implementada: D56). | Evita tabela sem uso. |
| D13 | **Datas**: `IssuedAt` no horário local da nota; `CreatedAt`/`UpdatedAt`/`ProcessedAt` em UTC; sem `DateTimeOffset`. | Consultas por período no SQLite. |
| D14 | **Swagger**: OpenAPI nativo + `Swashbuckle.AspNetCore.SwaggerUI`. | Padrão do .NET 10. |
| D15 | **Pacotes EF Core e `Mvc.Testing` fixados em 10.0.x** (a 11.0 exige .NET 11). | .NET 10 LTS. |
| D16 | **Frontend chama `/api` relativo**, com proxy no desenvolvimento. | Sem URLs fixas. |
| D17 | **Apache ECharts** instalado na Fase 5 (D48). | Não carregar dependência sem uso. |
| D18 | **Validação com DataAnnotations** (atributos próprios em `Validators/`) + regras no Service. **FluentValidation não foi adotado**: os atributos cobrem os casos com menos código. | A especificação o trata como opcional. |
| D19 | **CNPJ alfanumérico suportado** (vigente desde jul/2026): 12 caracteres `[0-9A-Z]` + 2 dígitos verificadores, valor de cada caractere = ASCII − 48. Armazenado sem máscara, em maiúsculas. | Novos CNPJs já podem ser alfanuméricos. |
| D20 | **Erros por campo em camelCase** (`SystemTextJsonValidationMetadataProvider`); regras de negócio usam as mesmas chaves (ex.: `categoryId`, `sortBy`). | Associação direta aos controles do formulário. |
| D21 | **Códigos HTTP**: 400 formato/entrada inválida; 404 inexistente; 409 duplicidade ou recurso em uso; 422 regra de negócio (categoria inexistente, ordenação inválida). | Respostas previsíveis para o frontend. |
| D22 | **Paginação**: `page` (inicia em 1), `pageSize` (padrão 20, máx. 100), `search`, `sortBy`, `sortDirection` (`asc`/`desc`). Categorias não são paginadas (lista pequena e usada em seletores). | Especificação seção 8. |
| D23 | **`[Range]` decimal com cultura invariante** (`ParseLimitsInInvariantCulture`). | Corrige erro 500 encontrado nos testes: em máquinas pt-BR, "0.0001" não era interpretado. |
| D24 | **Frontend: `rxResource`** (API pública no Angular 22) para dados de leitura; mutações por `subscribe` nos serviços; formulários reativos; sem NgRx. | Signals conforme a especificação, sem estado global. |
| D25 | **Validação no frontend só de formato** (obrigatório, tamanho, máscara de CNPJ, dígitos, casas decimais). Dígitos verificadores e unicidade ficam no backend, cujos erros são exibidos nos campos. | Não duplicar regras de negócio (seção 15). |
| D26 | **Valores da nota são os impressos.** O backend não recalcula nem corrige totais: compara e gera alertas (RN10). O frontend apenas pré-preenche valores calculados, que o usuário pode alterar. | Não inventar dados; a nota é a fonte. |
| D27 | **Fórmulas de consistência**: item = arred(qtd × preço, 2) − desconto; valor dos produtos = Σ (total + desconto dos itens); desconto da nota ≥ Σ descontos dos itens (permite desconto geral); total = produtos − descontos + acréscimos. Tolerância de R$ 0,01 (notas que truncam). | Estrutura usual de NFC-e (vProd, vDesc, vOutro, vNF). |
| D28 | **Alertas exigem ciência, não bloqueiam**: divergências e possíveis duplicatas retornam 409 na confirmação até `acknowledgeWarnings: true`. Somente a chave de acesso repetida bloqueia. | RN02 ("não bloquear compras legítimas") com decisão do usuário registrada. |
| D29 | **Fluxo manual = rascunho + confirmação** (`POST` e depois `PUT .../confirm`). Se o usuário recusar os alertas, a compra fica como rascunho, visível na listagem. | Endpoints da especificação; RN01. |
| D30 | **Histórico de preços gerado já na Fase 3**, sem preço normalizado. A Fase 5 adiciona a normalização e recalcula as observações existentes. | Mantém RN03/RN04/RN05 consistentes desde a primeira compra, sem migração de dados depois. |
| D31 | **Descrições aprendidas na confirmação**: associação item → produto feita pelo usuário vira descrição alternativa confirmada (origem `Receipt`). A sugestão automática no frontend só preenche quando há exatamente uma associação confirmada e ativa. | Associações ambíguas devem ser confirmadas pelo usuário (4.4). |
| D32 | **Imagens nunca são apagadas pela aplicação**: remover/substituir a imagem ou excluir a nota apenas desvincula. O gerenciamento de arquivos órfãos está em Configurações (D60). | "Preservar imagens originais" (6.3); evita perda acidental. |
| D33 | **Datas de auditoria lidas como UTC** por conversor no `AppDbContext` (`CreatedAt`, `UpdatedAt`, `ProcessedAt`). | Sem o "Z", o navegador exibia o horário UTC como local. |
| D34 | **Chave de acesso com CNPJ alfanumérico**: posições 7–20 aceitam `[0-9A-Z]`; DV por módulo 11 com valor = ASCII − 48 (mesma regra do CNPJ alfanumérico). | Notas de emitentes com CNPJ alfanumérico. |
| D35 | **Provedor: Anthropic (Claude), SDK oficial .NET**, modelo padrão `claude-opus-5-5`, esforço `medium`, configuráveis. Trocar de provedor = nova implementação de `IReceiptReader` + `ReceiptReaderFactory`. | Modelo multimodal atual com saída estruturada; SDK oficial em vez de HTTP manual. |
| D36 | **Saída estruturada com esquema estrito** (todos os campos obrigatórios, nulos via `anyOf`) em vez de *tool use*. | Garante JSON válido no formato do contrato; *tool use* forçado não é aceito pelo modelo. |
| D37 | **A IA só transcreve; o backend calcula e valida.** O total líquido do item (bruto − desconto) é calculado em `decimal`; CNPJ, chave, data e consistência são validados pelo backend; valores inválidos não são preenchidos. | "Não inventar dados" (5.3) e precisão monetária (6.4). |
| D38 | **Leitura assíncrona em processo** (`Channel` + `BackgroundService`, uma leitura por vez, recuperação na inicialização) e consulta periódica no frontend. | Leituras podem levar dezenas de segundos; evita tempo esgotado do HTTP sem infraestrutura de filas (seção 3 da especificação). |
| D39 | **Confirmação da importação cria a nota já confirmada em uma transação** (sem rascunho intermediário). Com alertas não aceitos, nada é gravado. | RN01 e "uma compra não poderá ficar parcialmente registrada". |
| D40 | **Fallback do lado do servidor para recusas** (`fallbacks: "default"`, beta `server-side-fallback-2026-07-01`) habilitado. Para desativar, remova `Betas`/`Fallbacks` em `ClaudeReceiptReader.BuildRequest`. | Recomendação do provedor para o modelo; recusas indevidas não interrompem a leitura. |
| D41 | **Limite de 5 MB para a leitura** (limite da API para imagens em base64), validado no frontend e no backend; o armazenamento continua aceitando até 10 MB. | Mensagem clara antes de gastar uma chamada. |
| D42 | **Chave só no backend**: `ReceiptReader:ApiKey` (user-secrets) ou `ANTHROPIC_API_KEY`; nunca em appsettings versionado nem no Angular. `UserSecretsId` adicionado ao projeto da API. | Especificação 2.4 e seção 10. |

| D43 | **Dois preços no histórico**: "preço pago" (total ÷ quantidade, com descontos) para a evolução do próprio produto; "preço normalizado" (por kg/L/un) para comparar embalagens e produtos. O preço unitário impresso continua guardado na observação. | O consumidor quer saber quanto pagou de fato; a normalização permite comparar tamanhos diferentes (4.5, RN08). |
| D44 | **Indicadores normalizados só quando todas as compras do período têm a mesma unidade-base**; caso contrário são omitidos. | Evita misturar valores comparáveis com não comparáveis. |
| D45 | **Produtos da mesma categoria são exibidos apenas como referência** (mesma unidade-base, último preço normalizado); nunca há fusão de históricos. | RN07 e 4.4: marcas/tamanhos diferentes não são equivalentes automaticamente. |
| D46 | **Recálculo do preço normalizado na inicialização** (idempotente) e ao alterar unidade/conteúdo do produto. | Mantém o histórico coerente com o cadastro e com regras futuras sem migração de dados. |
| D47 | **Associação posterior em lote por descrição normalizada** (`/api/price-history/assign`), em transação, com aprendizado da descrição. | RN06 com pouco esforço: a mesma descrição costuma se repetir em várias compras. |
| D48 | **ECharts modular e sob demanda** (`echarts/core` + módulos usados; `provideEchartsCore` com `import()` dinâmico). | Bundle inicial praticamente inalterado. |
| D49 | **Paleta categórica de referência das diretrizes de visualização** (8 cores em ordem fixa, validada: CVD ΔE ≥ 9,1 entre vizinhas). Três cores têm contraste < 3:1 com a superfície; a compensação é a tabela com os mesmos dados e a legenda. Tema claro apenas (o app ainda não tem modo escuro). | Gráficos legíveis para daltônicos e sem depender só da cor. |
| D50 | **Dashboard e relatórios partem da mesma leitura** (`AnalyticsData`) e os relatórios usam uma tabela genérica (`ReportTable`) que serve à tela e às três exportações. Período padrão: últimos 12 meses. | Os números da tela, do gráfico e do arquivo exportado não divergem; um novo relatório não exige código de exportação. |
| D51 | **Filtro de categoria soma apenas os itens da categoria** em cada compra (não o total da nota); a contagem considera as compras que têm ao menos um item da categoria. | "Quanto gastei com laticínios" é a soma dos laticínios, não das notas inteiras. |
| D52 | **Distribuição por categoria fecha com o total gasto**: itens sem produto aparecem como "Itens sem produto associado" e a diferença entre o total das notas e a soma dos itens (descontos e acréscimos gerais) como "Ajustes da nota". | A soma das categorias confere com o indicador de total; nada desaparece da análise. |
| D53 | **CSV no padrão do Excel em português**: separador `;`, vírgula decimal, UTF-8 com BOM, valores sem símbolo de moeda. | Abre corretamente com duplo clique no Excel pt-BR e continua legível por outras ferramentas. |
| D54 | **ClosedXML (MIT) para Excel e QuestPDF (licença Community) para PDF.** A licença é declarada em `Program.cs`; a Community é gratuita para uso pessoal e para organizações com receita anual abaixo de US$ 1 milhão. | Bibliotecas maduras e sem dependências nativas; nenhuma geração de arquivo no navegador. |
| D55 | **Filtros do dashboard e dos relatórios na URL** (`period`, `from`, `to`, `storeId`, `categoryId`, `report`, `ranking`). | Links "Ver relatório" levam a tabela com os mesmos filtros (a tabela é a alternativa acessível ao gráfico); recarregar ou compartilhar mantém a análise. |
| D56 | **Autenticação por cookie de sessão, ligada por padrão**, com usuário único (completa D12). Login por e-mail; sem recuperação por e-mail: a recuperação é feita no servidor (`Authentication:ResetUser`). | Seção 10: simples, sem multi-tenancy; o cookie `HttpOnly` não fica acessível a scripts (ao contrário de um token no `localStorage`). |
| D57 | **Cadastro inicial só a partir do próprio computador** (loopback), salvo `AllowRemoteSetup`. | Evita que alguém na rede "tome posse" da instalação antes do dono. |
| D58 | **Proteção contra CSRF por `SameSite=Strict`** + CORS restrito + corpo JSON, sem token antiforgery. | Navegadores atuais não enviam o cookie em requisições de outros sites; menos complexidade para um único usuário. |
| D59 | **Backup = cópia consistente do banco (`VACUUM INTO`) + imagens, em .zip; restauração manual documentada**, sem endpoint de restauração. A exportação em CSV é separada. | Substituir o banco com a aplicação em execução é arriscado; a restauração manual é rara e explícita. |
| D60 | **Imagens órfãs só são apagadas pelo usuário** (Configurações), com a lista recalculada no servidor; leituras em andamento protegem suas imagens. Completa D32. | Nada é apagado automaticamente; o usuário revisa antes. |
| D61 | **Interface publicada pela própria API** (mesma origem), com CSP `script-src 'self'` e *critical CSS* inline desativado no build. | Um processo só, sem CORS; a CSP impede scripts injetados. |
| D62 | **Preferências de exibição no navegador** (`localStorage`): período padrão e itens por página. | São preferências do dispositivo, não dados da aplicação; sem tabela nem endpoint. |
| D63 | **Docker opcional** (compose com porta só em `127.0.0.1` e cadastro remoto liberado por isso). | Especificação 2.5: Docker não é obrigatório para execução local. |

## 6. Funcionalidades pendentes

Todas as fases do planejamento (seção 13 da especificação) foram implementadas. Restam validações que dependem de recursos externos:

- Validar a leitura real com uma chave da Anthropic e fotos de notas reais (ajustar prompt/esforço se necessário).
- Construir e executar a imagem Docker em um ambiente com o serviço do Docker ativo.
- Publicação em servidor com HTTPS (proxy reverso) quando houver necessidade de acesso remoto.

## 7. Problemas conhecidos

- Nenhum problema crítico.
- A busca textual usa `LIKE` do SQLite, que ignora maiúsculas/minúsculas apenas em ASCII: "laticinios" não encontra "Laticínios" pelo nome (a busca por descrições alternativas já ignora acentos). Avaliar uma coluna normalizada de busca se isso incomodar no uso.
- A ordenação alfabética do SQLite é binária: nomes iniciados por letra acentuada (ex.: "Água") aparecem depois do "Z".
- O bundle inicial está em ≈ 847 kB (≈ 177 kB transferidos), abaixo do limite de aviso (1 MB); as telas e o ECharts são carregados sob demanda.
- Fontes e ícones vêm do Google Fonts; sem internet, a interface usa fontes de reserva.
- Imagens desvinculadas ou de notas excluídas permanecem em `uploads/` até o usuário excluí-las em Configurações (D60); a página de uma leitura antiga cuja imagem foi excluída mostra a imagem ausente.
- Os campos numéricos usam `<input type="number">`: o separador decimal exibido depende do navegador (ponto ou vírgula).
- **A leitura real pela API da Anthropic ainda não foi executada**: não há chave neste ambiente. A integração foi escrita com o SDK oficial e compila; o tratamento de respostas, erros e o fluxo completo foram testados com um leitor simulado. Com a chave configurada (README → "Leitura de notas por IA"), validar com fotos reais.
- Fotos HEIC (padrão do iPhone) não são aceitas; o usuário precisa enviar JPEG/PNG/WebP. Fotos acima de 5 MB precisam ser reduzidas antes do envio.
- A fila de leitura é em memória e processa uma imagem por vez; adequada ao uso pessoal.
- O histórico e os indicadores são calculados em memória a partir das observações do produto (sem paginação); adequado ao volume de uso pessoal.
- O recálculo do preço normalizado percorre todas as observações a cada inicialização; com volumes muito grandes, convém restringi-lo.
- A cor de cada loja no gráfico segue a ordem dos identificadores das lojas presentes no período exibido; ao mudar o período, uma loja pode trocar de cor se outra deixar de aparecer.
- Dashboard e relatórios agregam em memória as compras confirmadas do período (somas exatas em `decimal`); adequado ao volume de uso pessoal. Para volumes muito grandes, mover as somas para SQL sobre os inteiros escalados.
- Nos primeiros instantes após abrir o dashboard, enquanto o menu lateral termina de abrir, o gráfico mensal pode aparecer estreito; o ECharts se ajusta sozinho ao tamanho final.
- O PDF usa as fontes padrão do QuestPDF (Lato); nomes com caracteres fora do latim podem não ser exibidos.
- No Linux/Docker, as chaves de sessão em `data/keys` não são criptografadas em repouso (o ASP.NET Core registra um aviso); protegem apenas os cookies, e quem lê essa pasta já tem acesso ao banco.
- Em produção sem HTTPS (uso local), o ASP.NET Core registra o aviso "Failed to determine the https port for redirect"; é esperado. Com proxy HTTPS, habilite `ReverseProxy:Enabled`.
- A autenticação usa um único usuário, sem recuperação por e-mail; a recuperação exige acesso ao servidor (README → "Senha esquecida").
- Preferências de exibição valem por navegador (D62).

## 8. Próximos passos

O planejamento da especificação foi concluído (Fases 1 a 7) e todos os critérios de aceitação da seção 12 têm implementação e testes. A leitura por IA com dados reais é o único critério ainda não verificado, por falta de chave neste ambiente. Sugestões para o uso real:

1. Configurar a chave da Anthropic e validar a leitura com fotos de notas reais (diferentes supermercados, notas longas, fotos inclinadas), ajustando prompt e esforço se necessário.
2. Usar a aplicação no dia a dia e fazer backups periódicos fora do computador.
3. Construir a imagem Docker com o serviço ativo (`docker compose up -d --build`), se for usar contêiner.
4. Avaliar melhorias registradas na seção 7, conforme o incômodo no uso: busca sem acentos pelo nome, ordenação com acentos e campos numéricos com vírgula.
