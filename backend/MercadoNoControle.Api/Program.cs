using System.Text.Json.Serialization;
using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(storagePaths.ConnectionString));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISystemStatusService, SystemStatusService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IStoreService, StoreService>();
builder.Services.AddScoped<IProductService, ProductService>();

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

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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

app.UseCors(FrontendCorsPolicy);
app.MapControllers();

app.Logger.LogInformation("Banco de dados: {DatabaseFile}", storagePaths.DatabaseFile);
app.Logger.LogInformation("Diretório de imagens: {UploadsDirectory}", storagePaths.UploadsDirectory);

await app.RunAsync();

/// <summary>Exposto para testes de integração com WebApplicationFactory.</summary>
public partial class Program;
