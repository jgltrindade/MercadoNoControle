using System.Text.Json.Serialization;
using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.Services;
using MercadoNoControle.Api.Services.Ai;
using MercadoNoControle.Api.Services.Analytics;
using MercadoNoControle.Api.Services.Imports;
using MercadoNoControle.Api.Services.PriceHistory;
using MercadoNoControle.Api.Services.Receipts;
using MercadoNoControle.Api.Services.Settings;
using MercadoNoControle.Api.Services.Storage;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Logs estruturados (JSON) fora do ambiente de desenvolvimento.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole();
}

// Armazenamento: banco SQLite e imagens fora do código-fonte, com caminhos configuráveis.
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
var storagePaths = StoragePaths.Resolve(storageOptions, builder.Environment.ContentRootPath);
storagePaths.EnsureDirectories();
builder.Services.AddSingleton(storagePaths);

// Autenticação de usuário único, limites de requisição e proteção das chaves de sessão.
builder.AddAppSecurity(storagePaths);

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(storagePaths.ConnectionString));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISystemStatusService, SystemStatusService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IStoreService, StoreService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddScoped<PriceObservationRecorder>();
builder.Services.AddScoped<IPriceHistoryService, PriceHistoryService>();
builder.Services.AddScoped<AnalyticsData>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<MercadoNoControle.Api.Services.Demo.DemoDataSeeder>();

// QuestPDF: licença Community (uso pessoal / receita anual inferior a US$ 1 milhão).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.Configure<ImageUploadOptions>(builder.Configuration.GetSection(ImageUploadOptions.SectionName));
builder.Services.AddSingleton<IReceiptImageStorage, ReceiptImageStorage>();

// Leitura de notas por IA: provedor escolhido por configuração; sem chave, o cadastro manual continua disponível.
builder.Services.Configure<ReceiptReaderOptions>(builder.Configuration.GetSection(ReceiptReaderOptions.SectionName));
builder.Services.AddSingleton<IReceiptReader>(ReceiptReaderFactory.Create);
builder.Services.AddSingleton<ReceiptImportQueue>();
builder.Services.AddScoped<ReceiptImportProcessor>();
builder.Services.AddScoped<ReceiptDraftBuilder>();
builder.Services.AddScoped<IReceiptImportService, ReceiptImportService>();
builder.Services.AddHostedService<ReceiptImportWorker>();

builder.Services
    .AddControllers(options =>
        // Erros de validação usam os nomes JSON (camelCase) das propriedades.
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddOpenApi();

// CORS somente para as origens configuradas (frontend Angular).
const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

await app.MigrateDatabaseAsync();

// Dados fictícios para demonstração (executa e encerra, sem subir o servidor):
//   dotnet run --project MercadoNoControle.Api -- --seed-demo   |   -- --remove-demo
if (args.Contains("--seed-demo") || args.Contains("--remove-demo"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<MercadoNoControle.Api.Services.Demo.DemoDataSeeder>();
    if (args.Contains("--remove-demo"))
    {
        await seeder.RemoveAsync();
    }
    else
    {
        await seeder.SeedAsync();
    }

    return;
}

if (app.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Mercado no Controle API v1");
        options.DocumentTitle = "Mercado no Controle API";
    });
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Publicação: a interface Angular compilada (Spa:RootPath, padrão wwwroot) é servida pela própria
// API, na mesma origem. Sem a pasta (desenvolvimento), o Angular roda separado com proxy.
var spaRoot = Path.GetFullPath(app.Configuration["Spa:RootPath"] ?? "wwwroot", app.Environment.ContentRootPath);
var serveSpa = File.Exists(Path.Combine(spaRoot, "index.html"));
var spaFiles = serveSpa ? new PhysicalFileProvider(spaRoot) : null;
var spaFileOptions = new StaticFileOptions
{
    FileProvider = spaFiles,
    OnPrepareResponse = context =>
    {
        // index.html sempre revalidado; demais arquivos têm hash no nome e podem ficar em cache.
        context.Context.Response.Headers.CacheControl = context.File.Name == "index.html"
            ? "no-cache"
            : "public, max-age=31536000, immutable";
    }
};
if (serveSpa)
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = spaFiles });
    app.UseStaticFiles(spaFileOptions);
}

// Roteamento depois dos arquivos estáticos: do contrário a rota de fallback da interface seria
// escolhida antes e os arquivos (JS/CSS) seriam respondidos com o index.html.
app.UseRouting();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

if (serveSpa)
{
    // Rotas do Angular (ex.: /receipts/10) devolvem index.html; rotas /api desconhecidas continuam 404.
    app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html", spaFileOptions).AllowAnonymous();
}

app.Logger.LogInformation("Banco de dados: {DatabaseFile}", storagePaths.DatabaseFile);
app.Logger.LogInformation("Diretório de imagens: {UploadsDirectory}", storagePaths.UploadsDirectory);

await app.RunAsync();

/// <summary>Exposto para testes de integração com WebApplicationFactory.</summary>
public partial class Program;
