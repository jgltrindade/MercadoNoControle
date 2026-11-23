using System.Text.Json;
using MercadoNoControle.Api.Services.Ai;

namespace MercadoNoControle.Tests.Infrastructure;

/// <summary>
/// Leitor de notas simulado — existe somente no projeto de testes. Devolve uma extração
/// pré-definida (fixture) ou lança a exceção configurada.
/// </summary>
public sealed class FakeReceiptReader : IReceiptReader
{
    public ReceiptExtraction? Extraction { get; set; } = SampleExtraction();

    public Exception? Exception { get; set; }

    public int Calls { get; private set; }

    public long LastImageLength { get; private set; }

    public string ProviderName => "Fake";

    public string? ModelName => "fake-model";

    public bool IsAvailable => true;

    public async Task<ReceiptExtractionResult> ExtractAsync(Stream image, CancellationToken cancellationToken)
    {
        Calls++;
        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, cancellationToken);
        LastImageLength = buffer.Length;

        if (Exception is not null)
        {
            throw Exception;
        }

        var json = JsonSerializer.Serialize(Extraction, JsonSerializerOptions.Web);
        return new ReceiptExtractionResult(Extraction!, json, ProviderName, ModelName);
    }

    /// <summary>Nota fictícia consistente: 2 itens, desconto de 1,00 no arroz, total 36,53.</summary>
    public static ReceiptExtraction SampleExtraction(string cnpj = "11.222.333/0001-81") => new()
    {
        IsReceipt = true,
        Store = new ExtractedStore("MERCADO TESTE LTDA", cnpj),
        Receipt = new ExtractedReceiptData(
            Number: "123456",
            Series: "1",
            AccessKey: "35261111222333000181650010000123451000012345",
            IssueDate: "2026-11-15T14:30:00",
            TotalProducts: 37.53m,
            Discount: 1.00m,
            AdditionalCharges: 0m,
            Total: 36.53m,
            PaymentMethod: "Cartao de Debito"),
        Items =
        [
            new ExtractedItem("BANANA PRATA KG", 1.235m, "KG", 6.99m, 0m, 8.63m),
            new ExtractedItem("ARROZ T1 5KG", 1m, "UN", 28.90m, 1.00m, 28.90m)
        ]
    };

    /// <summary>Bytes mínimos de um JPEG válido para os testes de upload.</summary>
    public static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 1, 2, 3, 4, 5, 6, 7, 8];
}
