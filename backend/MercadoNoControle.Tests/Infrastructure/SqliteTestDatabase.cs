using MercadoNoControle.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Tests.Infrastructure;

/// <summary>
/// Banco SQLite em memória com o esquema criado pelas migrations reais.
/// A conexão permanece aberta durante o teste; cada <see cref="CreateContext"/>
/// devolve um contexto novo (sem entidades rastreadas) sobre o mesmo banco.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public static SqliteTestDatabase Create()
    {
        var database = new SqliteTestDatabase();
        using var context = database.CreateContext();
        context.Database.Migrate();
        return database;
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    public SqliteConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();
}
