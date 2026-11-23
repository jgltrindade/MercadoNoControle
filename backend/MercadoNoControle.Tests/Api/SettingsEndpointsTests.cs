using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.DTOs.Settings;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.Data.Sqlite;

namespace MercadoNoControle.Tests.Api;

/// <summary>Configurações (especificação 4.8): informações, backup, exportação e imagens. Banco novo por teste.</summary>
public class SettingsEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<int> CreateConfirmedReceiptAsync(HttpClient client, string storeName, bool withImage = true)
    {
        var store = await client.PostAsJsonAsync("/api/stores", new { tradeName = storeName });
        var storeId = (await store.Content.ReadFromJsonAsync<StoreResponse>(Json))!.Id;
        var create = await client.PostAsJsonAsync("/api/receipts", new
        {
            storeId,
            issuedAt = "2026-11-15T14:30:00",
            productsTotal = 12.50m,
            total = 12.50m,
            items = new[] { new { description = "CAFE 500G", quantity = 1m, unit = "UN", unitPrice = 12.50m, total = 12.50m } }
        });
        var receiptId = (await create.Content.ReadFromJsonAsync<ReceiptResponse>(Json))!.Id;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/receipts/{receiptId}/confirm", new { })).StatusCode);
        if (withImage)
        {
            await AttachImageAsync(client, receiptId);
        }

        return receiptId;
    }

    private static async Task AttachImageAsync(HttpClient client, int receiptId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(FakeReceiptReader.JpegBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "nota.jpg");
        var response = await client.PostAsync($"/api/receipts/{receiptId}/image", form);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task System_info_reports_storage_counts_and_reader_without_exposing_the_key()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await CreateConfirmedReceiptAsync(client, "Loja Info", withImage: false);

        var response = await client.GetAsync("/api/settings/system");
        var raw = await response.Content.ReadAsStringAsync();
        var info = JsonSerializer.Deserialize<SystemInfoResponse>(raw, Json)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(factory.DatabaseFile, info.DatabaseFile);
        Assert.True(info.DatabaseSizeBytes > 0);
        Assert.Equal(1, info.Counts.Stores);
        Assert.Equal(10, info.Counts.Categories);
        Assert.Equal(1, info.Counts.ConfirmedReceipts);
        Assert.Equal(0, info.Counts.DraftReceipts);
        Assert.False(info.AuthenticationEnabled);
        Assert.False(string.IsNullOrWhiteSpace(info.Reader.Provider));
        Assert.DoesNotContain("apiKey", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Backup_contains_a_consistent_database_copy_images_and_manifest()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await CreateConfirmedReceiptAsync(client, "Loja Backup");

        var response = await client.GetAsync("/api/settings/backup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches(@"^mercado-no-controle-backup-\d{8}-\d{4}\.zip$", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));

        var directory = Directory.CreateTempSubdirectory("mnc-backup-test");
        try
        {
            await using (var stream = await response.Content.ReadAsStreamAsync())
            {
                await ZipFile.ExtractToDirectoryAsync(stream, directory.FullName);
            }

            Assert.True(File.Exists(Path.Combine(directory.FullName, "manifest.json")));
            var images = Directory.GetFiles(Path.Combine(directory.FullName, "uploads"), "*.jpg", SearchOption.AllDirectories);
            Assert.Single(images);
            Assert.Equal(FakeReceiptReader.JpegBytes, await File.ReadAllBytesAsync(images[0]));

            // A cópia do banco abre sozinha e contém os dados.
            var databaseCopy = Path.Combine(directory.FullName, "database", "mercado-no-controle.db");
            await using (var connection = new SqliteConnection($"Data Source={databaseCopy};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT TradeName FROM Stores; ";
                Assert.Equal("Loja Backup", await command.ExecuteScalarAsync());
                command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ImagePath IS NOT NULL";
                Assert.Equal(1L, await command.ExecuteScalarAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Data_export_has_one_csv_per_entity()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await CreateConfirmedReceiptAsync(client, "Loja; Exportação", withImage: false);

        var response = await client.GetAsync("/api/settings/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(
            ["categorias.csv", "compras.csv", "historico-precos.csv", "itens.csv", "produtos.csv", "supermercados.csv"],
            zip.Entries.Select(e => e.FullName).Order().ToArray());

        string Read(string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        var receipts = Read("compras.csv");
        Assert.StartsWith("Id;Emissão;Supermercado", receipts, StringComparison.Ordinal);
        Assert.Contains("15/11/2026 14:30;\"Loja; Exportação\"", receipts, StringComparison.Ordinal);
        Assert.Contains("Confirmada", receipts, StringComparison.Ordinal);
        Assert.Contains(";12,50;", receipts, StringComparison.Ordinal);
        Assert.Contains("CAFE 500G", Read("itens.csv"), StringComparison.Ordinal);
        Assert.Contains("Laticínios", Read("categorias.csv"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Orphan_images_are_listed_and_deleted_without_touching_used_images()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var kept = await CreateConfirmedReceiptAsync(client, "Loja Mantida");
        var unlinked = await CreateConfirmedReceiptAsync(client, "Loja Sem Imagem");
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/receipts/{unlinked}/image")).StatusCode);
        var deleted = await CreateConfirmedReceiptAsync(client, "Loja Excluida");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/receipts/{deleted}")).StatusCode);

        var summary = await client.GetFromJsonAsync<ImagesSummaryResponse>("/api/settings/images", Json);
        Assert.Equal(3, summary!.TotalFiles);
        Assert.Equal(1, summary.ReferencedFiles);
        Assert.Equal(2, summary.Orphans.Count);
        Assert.Equal(0, summary.MissingFiles);
        Assert.Equal(2L * FakeReceiptReader.JpegBytes.Length, summary.OrphanBytes);

        var result = await (await client.DeleteAsync("/api/settings/images/orphans")).Content.ReadFromJsonAsync<DeleteOrphansResponse>(Json);
        Assert.Equal(2, result!.DeletedFiles);

        var after = await client.GetFromJsonAsync<ImagesSummaryResponse>("/api/settings/images", Json);
        Assert.Equal(1, after!.TotalFiles);
        Assert.Empty(after.Orphans);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/receipts/{kept}/image")).StatusCode);
    }
}
