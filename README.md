# Mercado no Controle

Aplicação web pessoal para controle de despesas de supermercado e acompanhamento histórico de preços, com leitura de notas fiscais assistida por IA.

- Especificação (fonte de verdade): [docs/ESPECIFICACAO.md](docs/ESPECIFICACAO.md)
- Andamento e decisões técnicas: [docs/STATUS_DESENVOLVIMENTO.md](docs/STATUS_DESENVOLVIMENTO.md)

## Stack

| Camada | Tecnologias |
|---|---|
| Frontend | Angular 22, TypeScript, Angular Material, SCSS, Signals (`rxResource`), Vitest |
| Backend | ASP.NET Core Web API (.NET 10), EF Core 10, Swagger/OpenAPI, xUnit |
| Banco | SQLite (arquivo local em `data/`) |

## Estrutura

```text
MercadoNoControle/
├── backend/
│   ├── MercadoNoControle.Api/      # API REST (Controllers, Services, Models, DTOs, Data, Validators, Configuration)
│   └── MercadoNoControle.Tests/    # Testes xUnit (unidade, persistência e integração)
├── frontend/mercado-no-controle/   # SPA Angular (core, shared, features)
├── data/                           # Banco SQLite (gerado, não versionado)
├── uploads/                        # Imagens das notas (geradas, não versionadas)
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

   Swagger: <http://localhost:5080/swagger> (somente em Development).

2. **Frontend** (porta 4200). As chamadas a `/api` são encaminhadas ao backend pelo proxy de desenvolvimento (`proxy.conf.json`).

   ```powershell
   cd frontend/mercado-no-controle
   npm install
   npm start
   ```

   Acesse <http://localhost:4200>.

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

Caminhos relativos são resolvidos a partir do diretório do projeto da API. Chaves de API (provedor de IA, Fase 4) serão configuradas somente no backend, por variável de ambiente ou *user-secrets* — nunca no Angular nem em arquivos versionados.

## Migrations

```powershell
cd backend
dotnet tool restore
dotnet ef migrations add NomeDaMigration --project MercadoNoControle.Api --output-dir Data/Migrations
```

## Modelo de acesso

Até a Fase 7 a API não possui autenticação e deve ser executada apenas no computador local (escuta em `localhost`). Não exponha a porta 5080 na rede. A autenticação simples de usuário único está prevista para a Fase 7, antes de qualquer publicação.
