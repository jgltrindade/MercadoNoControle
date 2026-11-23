# Mercado no Controle

Aplicação web pessoal para controle de despesas de supermercado e acompanhamento histórico de preços, com leitura de notas fiscais assistida por IA.

- Especificação (fonte de verdade): [docs/ESPECIFICACAO.md](docs/ESPECIFICACAO.md)
- Andamento e decisões técnicas: [docs/STATUS_DESENVOLVIMENTO.md](docs/STATUS_DESENVOLVIMENTO.md)

## Stack

| Camada | Tecnologias |
|---|---|
| Frontend | Angular 22, TypeScript, Angular Material, SCSS, Signals (`rxResource`), Apache ECharts, Vitest |
| Backend | ASP.NET Core Web API (.NET 10), EF Core 10, Swagger/OpenAPI, ClosedXML, QuestPDF, xUnit |
| IA | API da Anthropic (Claude) pelo SDK oficial .NET, atrás de `IReceiptReader` |
| Banco | SQLite (arquivo local em `data/`) |

## Estrutura

```text
MercadoNoControle/
├── backend/
│   ├── MercadoNoControle.Api/      # API REST (Controllers, Services, Models, DTOs, Data, Validators, Configuration)
│   └── MercadoNoControle.Tests/    # Testes xUnit (unidade, persistência e integração)
├── frontend/mercado-no-controle/   # SPA Angular (core, shared, features)
├── data/                           # Banco SQLite e chaves de sessão (gerados, não versionados)
├── uploads/                        # Imagens das notas (geradas, não versionadas)
├── scripts/publish.ps1             # Gera a versão publicável (API + interface)
├── Dockerfile, docker-compose.yml  # Execução opcional com Docker
└── docs/
```

## Pré-requisitos

- .NET SDK 10
- Node.js 22.22+, 24.15+ ou 26+ e npm

## Executando localmente

1. **Backend** (porta 5080). O banco é criado automaticamente na primeira execução, aplicando as migrations.

   ```powershell
   cd backend
   dotnet run --project MercadoNoControle.Api --launch-profile http
   ```

   Swagger: <http://localhost:5080/swagger> (somente em Development). Para testar rotas protegidas por ali, chame antes `POST /api/auth/login`: o cookie de sessão fica valendo para as chamadas seguintes.

2. **Frontend** (porta 4200). As chamadas a `/api` são encaminhadas ao backend pelo proxy de desenvolvimento (`proxy.conf.json`).

   ```powershell
   cd frontend/mercado-no-controle
   npm install
   npm start
   ```

   Acesse <http://localhost:4200>.

3. **Primeiro acesso.** A aplicação pede o cadastro do usuário (nome, e-mail e senha de no mínimo 8 caracteres) e já entra. Nos acessos seguintes, entre com o e-mail e a senha. Veja [Acesso e segurança](#acesso-e-segurança).

## Testes

```powershell
cd backend
dotnet test
```

```powershell
cd frontend/mercado-no-controle
npx ng test --watch=false
```

## Configuração

| Chave (`appsettings.json`) | Variável de ambiente | Padrão |
|---|---|---|
| `Storage:DatabasePath` | `Storage__DatabasePath` | `../../data/mercado-no-controle.db` |
| `Storage:UploadsPath` | `Storage__UploadsPath` | `../../uploads` |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0` | `http://localhost:4200` |
| `Uploads:MaxImageSizeBytes` | `Uploads__MaxImageSizeBytes` | `10485760` (10 MB) |
| `Authentication:Enabled` | `Authentication__Enabled` | `true` |
| `Authentication:SessionDays` | `Authentication__SessionDays` | `30` |
| `Authentication:AllowRemoteSetup` | `Authentication__AllowRemoteSetup` | `false` |
| `Authentication:ResetUser` | `Authentication__ResetUser` | `false` (ver [Senha esquecida](#senha-esquecida)) |
| `ReverseProxy:Enabled` | `ReverseProxy__Enabled` | `false` |
| `Spa:RootPath` | `Spa__RootPath` | `wwwroot` |

Caminhos relativos são resolvidos a partir do diretório do projeto da API (em desenvolvimento) ou da pasta publicada. Na publicação, `appsettings.Production.json` usa `data/` e `uploads/` dentro da pasta publicada.

## Leitura de notas por IA

A leitura automática usa a API da Anthropic (Claude, modelo `claude-opus-5-5` por padrão) por meio da interface `IReceiptReader`. **Sem chave configurada, o sistema funciona normalmente com o cadastro manual** e a tela "Nova compra" informa que a leitura está indisponível.

A chave fica **somente no backend** — nunca no Angular nem em arquivos versionados. Em desenvolvimento, use *user-secrets*:

```powershell
cd backend/MercadoNoControle.Api
dotnet user-secrets set "ReceiptReader:ApiKey" "<sua-chave>"
```

Alternativas: variável de ambiente `ANTHROPIC_API_KEY` ou `ReceiptReader__ApiKey`. Reinicie a API depois de configurar.

| Chave (`appsettings.json`) | Padrão | Descrição |
|---|---|---|
| `ReceiptReader:Provider` | `Anthropic` | `None` desativa a leitura automática |
| `ReceiptReader:Model` | `claude-opus-5-5` | Modelo usado na leitura |
| `ReceiptReader:Effort` | `medium` | `low`, `medium`, `high`, `xhigh` ou `max` |
| `ReceiptReader:TimeoutSeconds` | `180` | Tempo máximo de uma leitura |

Cada leitura é cobrada pela Anthropic conforme o uso de tokens. Imagens acima de 5 MB não são aceitas pela leitura automática.

## Migrations

```powershell
cd backend
dotnet tool restore
dotnet ef migrations add NomeDaMigration --project MercadoNoControle.Api --output-dir Data/Migrations
```

## Acesso e segurança

A aplicação tem **um único usuário**, sem perfis ou permissões. O modelo de acesso é:

- **Login obrigatório** (padrão). Toda a API, exceto `/api/health` e `/api/auth/*`, exige sessão.
- **Primeiro acesso:** o cadastro do usuário só é aceito a partir do próprio computador (`localhost`), para que ninguém na rede o faça antes. Para cadastrar de outro dispositivo, defina `Authentication:AllowRemoteSetup=true` apenas durante o cadastro.
- **Senha:** mínimo de 8 caracteres, guardada apenas como hash (PBKDF2 com salt). Trocar a senha (Configurações → Conta) encerra as outras sessões abertas.
- **Sessão:** cookie `HttpOnly` e `SameSite=Strict` (o navegador não o envia em requisições de outros sites), válido por 30 dias com renovação pelo uso. As chaves que protegem o cookie ficam em `data/keys` (no Windows, criptografadas pelo usuário do sistema).
- **Tentativas de login:** no máximo 10 a cada 5 minutos por endereço IP (HTTP 429 depois disso).
- **Outras proteções:**
  - Cabeçalhos de segurança: `Content-Security-Policy`, `X-Frame-Options`, `nosniff` e `Referrer-Policy`.
  - Requisições limitadas a 2 MB (10 MB nos envios de imagem); imagens validadas pelo conteúdo.
  - CORS restrito às origens configuradas.
  - Chave da IA somente no servidor; arquivos de segredo não são versionados.

**Uso local:** a API escuta apenas em `localhost`. Não exponha as portas 5080/4200 na rede.

**Acesso por outros dispositivos:** publique atrás de um proxy reverso com HTTPS (veja [Publicação](#publicação)). Sem HTTPS, a senha e o cookie trafegariam abertos.

`Authentication:Enabled=false` desliga o login: qualquer pessoa que alcance a porta terá acesso total. Use somente em testes automatizados ou em um computador de uso exclusivo, ciente do risco.

### Senha esquecida

Como não há envio de e-mail, a recuperação é feita no servidor e **não apaga compras nem outros dados**:

1. Pare a API.
2. Inicie uma vez com `Authentication__ResetUser=true`. O usuário é removido e um aviso aparece no log.

   ```powershell
   $env:Authentication__ResetUser = "true"
   dotnet run --project backend/MercadoNoControle.Api --launch-profile http
   ```

3. Acesse a aplicação e faça o cadastro inicial novamente.
4. Pare a API, remova a variável (`Remove-Item Env:Authentication__ResetUser`) e inicie normalmente.

## Backup e restauração

Em **Configurações → Backup e exportação**:

- **Baixar backup:** arquivo `.zip` com uma cópia consistente do banco (`database/mercado-no-controle.db`, gerada com `VACUUM INTO` mesmo com a aplicação em uso), todas as imagens (`uploads/`) e um `manifest.json`.
- **Exportar CSV:** supermercados, categorias, produtos, compras, itens e histórico de preços em CSV (`;`, vírgula decimal, UTF-8), para planilhas.

Guarde os backups fora do computador (nuvem, pen drive). Para **restaurar**:

1. Pare a aplicação.
2. Renomeie o banco atual e a pasta de imagens (ex.: `mercado-no-controle.db.antigo`), por segurança.
3. Extraia o `.zip`:
   - copie `database/mercado-no-controle.db` para o caminho de `Storage:DatabasePath` (padrão: `data/mercado-no-controle.db`) e apague arquivos `-wal`/`-shm` antigos que estejam ao lado;
   - copie o conteúdo de `uploads/` para `Storage:UploadsPath` (padrão: `uploads/`).
4. Inicie a aplicação. Migrations pendentes são aplicadas automaticamente, se o backup for de uma versão anterior.

O usuário e a senha fazem parte do banco: após restaurar, entre com as credenciais da época do backup.

**Imagens órfãs:** em Configurações → Imagens das notas, imagens que não pertencem a nenhuma nota nem a leitura em andamento (por exemplo, removidas ou de notas excluídas) podem ser revisadas e excluídas definitivamente. Faça um backup antes, se quiser guardá-las.

## Publicação

A versão publicada é **um único processo**: a API serve a interface Angular compilada na mesma origem, sem CORS nem proxy de desenvolvimento.

```powershell
./scripts/publish.ps1                       # gera ./publish
cd publish
$env:ASPNETCORE_URLS = "http://localhost:5080"
$env:Storage__DatabasePath = "D:/MercadoNoControle/dados/mercado-no-controle.db"   # recomendado: fora da pasta publicada
$env:Storage__UploadsPath = "D:/MercadoNoControle/uploads"
dotnet MercadoNoControle.Api.dll
```

Acesse <http://localhost:5080>. O script compila o Angular em produção, executa `dotnet publish` (Release) e copia a interface para `publish/wwwroot`. Opções: `-Output <pasta>` e `-SkipInstall` (não executa `npm ci`). Requer o runtime ASP.NET Core 10 no computador de destino.

- **Dados fora da pasta publicada:** assim, uma nova publicação não substitui o banco nem as imagens.
- **HTTPS:** para acesso fora do computador, use um proxy reverso (Caddy, Nginx, IIS) com certificado. Inicie a aplicação com `ReverseProxy__Enabled=true`, para que ela reconheça o HTTPS e o IP real do cliente (`X-Forwarded-*`); o cookie de sessão passa a ser marcado como `Secure`. Mantenha a porta da aplicação acessível apenas pelo proxy.
- **Logs:** fora de Development, em JSON no console (estruturados), prontos para um coletor ou para o Visualizador de Eventos/journald.

### Docker (opcional)

```powershell
docker compose up -d --build
```

O `docker-compose.yml` publica a porta somente em `127.0.0.1:8080` e guarda banco, chaves de sessão e imagens nos volumes `mnc-data` e `mnc-uploads`. A chave da IA pode ser passada pela variável `ANTHROPIC_API_KEY` (ou um arquivo `.env`, não versionado). Dentro do contêiner o navegador não aparece como "local"; por isso o compose permite o cadastro inicial remoto, que continua restrito a este computador pela porta em `127.0.0.1`.

Backup com Docker: use o botão de backup em Configurações. Para restaurar, copie os arquivos para os volumes com o contêiner parado (ex.: `docker run --rm -v mnc-data:/data -v ${PWD}:/backup alpine cp /backup/mercado-no-controle.db /data/`).
