using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace MercadoNoControle.Tests.Infrastructure;

/// <summary>
/// Sobe a API real com banco SQLite e diretório de imagens temporários e isolados.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), "mnc-tests", Guid.NewGuid().ToString("N"));

    public string DatabaseFile => Path.Combine(_rootDirectory, "test.db");

    public string UploadsDirectory => Path.Combine(_rootDirectory, "uploads");

    protected virtual bool AuthenticationEnabled => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:DatabasePath", DatabaseFile);
        builder.UseSetting("Storage:UploadsPath", UploadsDirectory);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");
        // Os testes de cada recurso não dependem de login; AuthEndpointsTests cobre a autenticação.
        builder.UseSetting("Authentication:Enabled", AuthenticationEnabled ? "true" : "false");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Diretório temporário; falha na limpeza não invalida o teste.
        }
    }
}
