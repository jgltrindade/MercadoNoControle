using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Reports;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Analytics;
using Microsoft.EntityFrameworkCore;
using T = MercadoNoControle.Api.DTOs.Reports.ReportColumnType;

namespace MercadoNoControle.Api.Services.Settings;

/// <summary>Arquivo gerado em disco temporário; é apagado ao fechar o fluxo de leitura.</summary>
public sealed record GeneratedArchive(Stream Content, string FileName);

public interface IBackupService
{
    /// <summary>Backup completo e restaurável: cópia consistente do banco SQLite + imagens + manifesto.</summary>
    Task<GeneratedArchive> CreateBackupAsync(CancellationToken cancellationToken);

    /// <summary>Exportação dos dados em CSV (formato aberto, para planilhas ou outras ferramentas).</summary>
    Task<GeneratedArchive> ExportDataAsync(CancellationToken cancellationToken);
}

public sealed class BackupService(
    AppDbContext db,
    StoragePaths paths,
    TimeProvider timeProvider,
    ILogger<BackupService> logger) : IBackupService
{
    public const string DatabaseEntry = "database/mercado-no-controle.db";
    public const string UploadsPrefix = "uploads/";
    public const string ManifestEntry = "manifest.json";

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<GeneratedArchive> CreateBackupAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var snapshot = Path.Combine(Path.GetTempPath(), $"mnc-snapshot-{Guid.NewGuid():N}.db");
        try
        {
            // VACUUM INTO gera uma cópia consistente mesmo com a aplicação em uso (sem copiar o arquivo aberto).
            await db.Database.ExecuteSqlAsync($"VACUUM INTO {snapshot}", cancellationToken);

            var archive = CreateTempFile();
            var imageCount = 0;
            await using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            {
                await zip.CreateEntryFromFileAsync(snapshot, DatabaseEntry, CompressionLevel.Optimal, cancellationToken);

                var uploadsRoot = Path.GetFullPath(paths.UploadsDirectory);
                if (Directory.Exists(uploadsRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(uploadsRoot, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(uploadsRoot, file).Replace('\\', '/');
                        // Imagens já são comprimidas (JPEG/PNG/WebP).
                        await zip.CreateEntryFromFileAsync(file, UploadsPrefix + relative, CompressionLevel.NoCompression, cancellationToken);
                        imageCount++;
                    }
                }

                var manifest = new
                {
                    application = "Mercado no Controle",
                    version = SettingsService.Version,
                    createdAt = now.UtcDateTime,
                    database = DatabaseEntry,
                    uploads = UploadsPrefix,
                    imageFiles = imageCount,
                    restore = "Pare a aplicação, substitua o arquivo do banco (Storage:DatabasePath) pelo arquivo em 'database/' e o conteúdo do diretório de imagens (Storage:UploadsPath) pelo de 'uploads/', e inicie novamente. Detalhes no README."
                };
                var entry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                await using var manifestStream = await entry.OpenAsync(cancellationToken);
                await JsonSerializer.SerializeAsync(manifestStream, manifest, ManifestJson, cancellationToken);
            }

            archive.Position = 0;
            logger.LogInformation("Backup gerado: banco + {Images} arquivo(s) de imagem ({Bytes} bytes)", imageCount, archive.Length);
            return new GeneratedArchive(archive, $"mercado-no-controle-backup-{now.ToLocalTime():yyyyMMdd-HHmm}.zip");
        }
        finally
        {
            File.Delete(snapshot);
        }
    }

    public async Task<GeneratedArchive> ExportDataAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tables = new[]
        {
            ("supermercados.csv", await StoresAsync(cancellationToken)),
            ("categorias.csv", await CategoriesAsync(cancellationToken)),
            ("produtos.csv", await ProductsAsync(cancellationToken)),
            ("compras.csv", await ReceiptsAsync(cancellationToken)),
            ("itens.csv", await ItemsAsync(cancellationToken)),
            ("historico-precos.csv", await PricesAsync(cancellationToken))
        };

        var archive = CreateTempFile();
        await using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, table) in tables)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var stream = await entry.OpenAsync(cancellationToken);
                await stream.WriteAsync(ReportExporter.ToCsv(table), cancellationToken);
            }
        }

        archive.Position = 0;
        return new GeneratedArchive(archive, $"mercado-no-controle-dados-{now.ToLocalTime():yyyyMMdd-HHmm}.zip");
    }

    private static FileStream CreateTempFile() => new(
        Path.Combine(Path.GetTempPath(), $"mnc-archive-{Guid.NewGuid():N}.zip"),
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        81920,
        FileOptions.DeleteOnClose | FileOptions.Asynchronous);

    private ReportTable Table(string key, IReadOnlyList<ReportColumn> columns, IEnumerable<object?[]> rows) =>
        new(key, key, string.Empty, null, timeProvider.GetUtcNow().UtcDateTime, columns, rows.Select(r => new ReportRow(r)).ToList());

    private static ReportColumn Column(string key, string header, T type = T.Text) => new(key, header, type);

    private static string DateTimeText(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private async Task<ReportTable> StoresAsync(CancellationToken cancellationToken)
    {
        var stores = await db.Stores.AsNoTracking().OrderBy(s => s.Id).ToListAsync(cancellationToken);
        return Table("supermercados",
            [Column("id", "Id", T.Integer), Column("tradeName", "Nome fantasia"), Column("legalName", "Razão social"), Column("cnpj", "CNPJ"),
             Column("address", "Endereço"), Column("city", "Cidade"), Column("state", "UF"), Column("notes", "Observações")],
            stores.Select(s => new object?[] { s.Id, s.TradeName, s.LegalName, s.Cnpj, s.Address, s.City, s.State, s.Notes }));
    }

    private async Task<ReportTable> CategoriesAsync(CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync(cancellationToken);
        return Table("categorias",
            [Column("id", "Id", T.Integer), Column("name", "Nome"), Column("description", "Descrição")],
            categories.Select(c => new object?[] { c.Id, c.Name, c.Description }));
    }

    private async Task<ReportTable> ProductsAsync(CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.Brand, Category = p.Category != null ? p.Category.Name : null, p.Barcode, p.Unit, p.PackageContent, p.Status })
            .ToListAsync(cancellationToken);
        return Table("produtos",
            [Column("id", "Id", T.Integer), Column("name", "Nome"), Column("brand", "Marca"), Column("category", "Categoria"),
             Column("barcode", "Código de barras"), Column("unit", "Unidade"), Column("packageContent", "Conteúdo da embalagem", T.Number), Column("status", "Situação")],
            products.Select(p => new object?[] { p.Id, p.Name, p.Brand, p.Category, p.Barcode, p.Unit.ToString(), p.PackageContent, p.Status.ToString() }));
    }

    private async Task<ReportTable> ReceiptsAsync(CancellationToken cancellationToken)
    {
        var receipts = await db.Receipts.AsNoTracking()
            .OrderBy(r => r.IssuedAt).ThenBy(r => r.Id)
            .Select(r => new
            {
                r.Id, r.IssuedAt, Store = r.Store.TradeName, r.Number, r.Series, r.AccessKey, r.Status,
                r.ProductsTotal, r.DiscountTotal, r.AdditionalCharges, r.Total, r.PaymentMethod, Items = r.Items.Count
            })
            .ToListAsync(cancellationToken);
        return Table("compras",
            [Column("id", "Id", T.Integer), Column("issuedAt", "Emissão"), Column("store", "Supermercado"), Column("number", "Número"),
             Column("series", "Série"), Column("accessKey", "Chave de acesso"), Column("status", "Situação"), Column("items", "Itens", T.Integer),
             Column("productsTotal", "Valor dos produtos", T.Money), Column("discountTotal", "Descontos", T.Money),
             Column("additionalCharges", "Acréscimos", T.Money), Column("total", "Total", T.Money), Column("paymentMethod", "Pagamento")],
            receipts.Select(r => new object?[]
            {
                r.Id, DateTimeText(r.IssuedAt), r.Store, r.Number, r.Series, r.AccessKey,
                r.Status == ReceiptStatus.Confirmed ? "Confirmada" : "Rascunho", r.Items,
                r.ProductsTotal, r.DiscountTotal, r.AdditionalCharges, r.Total, r.PaymentMethod
            }));
    }

    private async Task<ReportTable> ItemsAsync(CancellationToken cancellationToken)
    {
        var items = await db.ReceiptItems.AsNoTracking()
            .OrderBy(i => i.ReceiptId).ThenBy(i => i.LineNumber)
            .Select(i => new
            {
                i.ReceiptId, i.LineNumber, i.OriginalDescription, i.ProductId,
                Product = i.Product != null ? i.Product.Name : null,
                Category = i.Product != null && i.Product.Category != null ? i.Product.Category.Name : null,
                i.Quantity, i.Unit, i.UnitPrice, i.Discount, i.Total
            })
            .ToListAsync(cancellationToken);
        return Table("itens",
            [Column("receiptId", "Compra", T.Integer), Column("line", "Linha", T.Integer), Column("description", "Descrição na nota"),
             Column("productId", "Id do produto", T.Integer), Column("product", "Produto"), Column("category", "Categoria"),
             Column("quantity", "Quantidade", T.Number), Column("unit", "Unidade"), Column("unitPrice", "Preço unitário", T.Number),
             Column("discount", "Desconto", T.Money), Column("total", "Total", T.Money)],
            items.Select(i => new object?[]
            {
                i.ReceiptId, i.LineNumber, i.OriginalDescription, i.ProductId, i.Product, i.Category,
                i.Quantity, i.Unit, i.UnitPrice, i.Discount, i.Total
            }));
    }

    private async Task<ReportTable> PricesAsync(CancellationToken cancellationToken)
    {
        var prices = await db.PriceObservations.AsNoTracking()
            .OrderBy(p => p.ObservedAt).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.ObservedAt, Product = p.Product.Name, Store = p.Store.TradeName, p.ReceiptItem.ReceiptId,
                p.Quantity, p.UnitPrice, p.TotalPrice, p.NormalizedPrice, p.NormalizedUnit
            })
            .ToListAsync(cancellationToken);
        return Table("historico-precos",
            [Column("observedAt", "Data"), Column("product", "Produto"), Column("store", "Supermercado"), Column("receiptId", "Compra", T.Integer),
             Column("quantity", "Quantidade", T.Number), Column("unitPrice", "Preço unitário", T.Number),
             Column("totalPrice", "Total", T.Money), Column("normalizedPrice", "Preço normalizado", T.Number), Column("normalizedUnit", "Unidade normalizada")],
            prices.Select(p => new object?[]
            {
                DateTimeText(p.ObservedAt), p.Product, p.Store, p.ReceiptId, p.Quantity, p.UnitPrice, p.TotalPrice, p.NormalizedPrice, p.NormalizedUnit?.ToString()
            }));
    }
}
