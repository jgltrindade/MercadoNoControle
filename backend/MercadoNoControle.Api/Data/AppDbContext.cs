using MercadoNoControle.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MercadoNoControle.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Propriedades de auditoria gravadas em UTC. O SQLite não guarda o "Kind" da data;
    /// na leitura ele é restaurado como UTC para a API serializar com o sufixo "Z".
    /// Datas da compra (IssuedAt, ObservedAt) são horário local da nota e não entram aqui.
    /// </summary>
    private static readonly HashSet<string> UtcAuditProperties = ["CreatedAt", "UpdatedAt", "ProcessedAt", "PasswordChangedAt", "LastLoginAt"];

    private static readonly ValueConverter<DateTime, DateTime> UtcConverter =
        new(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductAlias> ProductAliases => Set<ProductAlias>();

    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();

    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();

    public DbSet<ReceiptImport> ReceiptImports => Set<ReceiptImport>();

    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            var isDateTime = property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?);
            if (isDateTime && UtcAuditProperties.Contains(property.Name))
            {
                property.SetValueConverter(UtcConverter);
            }
        }
    }
}
