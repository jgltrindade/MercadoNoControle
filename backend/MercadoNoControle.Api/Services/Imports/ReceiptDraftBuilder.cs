using System.Globalization;
using System.Text.Json;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Imports;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Ai;
using MercadoNoControle.Api.Services.Receipts;
using MercadoNoControle.Api.Validators;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services.Imports;

/// <summary>
/// Valida o resultado da IA e monta o rascunho para conferência (especificação 5.2, passo 6).
/// Nunca inventa dados: valores ilegíveis permanecem nulos e valores inválidos são descartados
/// com um alerta que mostra o que foi lido, para o usuário corrigir.
/// </summary>
public sealed class ReceiptDraftBuilder(AppDbContext db)
{
    public const string InvalidCnpj = "EXTRACTED_CNPJ_INVALID";
    public const string StoreNotFound = "STORE_NOT_REGISTERED";
    public const string InvalidAccessKey = "EXTRACTED_ACCESS_KEY_INVALID";
    public const string InvalidDate = "EXTRACTED_DATE_INVALID";
    public const string MissingValues = "EXTRACTED_VALUES_MISSING";
    public const string ExcessPrecision = "EXTRACTED_PRECISION";

    private static readonly string[] DateFormats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd"];

    public async Task<ReceiptDraft?> BuildAsync(string? resultJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return null;
        }

        ReceiptExtraction? extraction;
        try
        {
            extraction = JsonSerializer.Deserialize<ReceiptExtraction>(resultJson, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }

        return extraction is { IsReceipt: true } ? await BuildAsync(extraction, cancellationToken) : null;
    }

    public async Task<ReceiptDraft> BuildAsync(ReceiptExtraction extraction, CancellationToken cancellationToken)
    {
        var warnings = new List<ReceiptWarning>();
        var data = extraction.Receipt;

        var (storeId, storeName, cnpj) = await MatchStoreAsync(extraction.Store, warnings, cancellationToken);
        var accessKey = ValidAccessKey(data?.AccessKey, warnings);
        var issuedAt = ParseDate(data?.IssueDate, warnings);
        var items = await BuildItemsAsync(extraction.Items, cancellationToken);

        AddPrecisionWarnings(data, items, warnings);
        AddConsistencyWarnings(data, items, cnpj, accessKey, warnings);

        return new ReceiptDraft(
            storeId,
            storeName,
            TextNormalizer.Clean(extraction.Store?.Name),
            cnpj ?? TextNormalizer.Clean(extraction.Store?.Cnpj),
            TextNormalizer.Clean(data?.Number),
            TextNormalizer.Clean(data?.Series),
            accessKey,
            issuedAt,
            data?.TotalProducts,
            data?.Discount,
            data?.AdditionalCharges,
            data?.Total,
            TextNormalizer.Clean(data?.PaymentMethod),
            items,
            warnings);
    }

    private async Task<(int? Id, string? Name, string? Cnpj)> MatchStoreAsync(
        ExtractedStore? store, List<ReceiptWarning> warnings, CancellationToken cancellationToken)
    {
        var raw = TextNormalizer.Clean(store?.Cnpj);
        if (raw is null)
        {
            warnings.Add(new ReceiptWarning(StoreNotFound, "O CNPJ do supermercado não foi identificado. Selecione o supermercado."));
            return (null, null, null);
        }

        if (!CnpjValidator.IsValid(raw))
        {
            warnings.Add(new ReceiptWarning(InvalidCnpj, $"O CNPJ lido (\"{raw}\") é inválido. Confira e selecione o supermercado."));
            return (null, null, null);
        }

        var cnpj = CnpjValidator.Normalize(raw);
        var match = await db.Stores.AsNoTracking()
            .Where(s => s.Cnpj == cnpj)
            .Select(s => new { s.Id, s.TradeName })
            .FirstOrDefaultAsync(cancellationToken);
        if (match is null)
        {
            warnings.Add(new ReceiptWarning(StoreNotFound, "Supermercado ainda não cadastrado. Cadastre-o com os dados lidos da nota."));
            return (null, null, cnpj);
        }

        return (match.Id, match.TradeName, cnpj);
    }

    private static string? ValidAccessKey(string? raw, List<ReceiptWarning> warnings)
    {
        var value = TextNormalizer.Clean(raw);
        if (value is null)
        {
            return null;
        }

        if (AccessKeyValidator.IsValid(value))
        {
            return AccessKeyValidator.Normalize(value);
        }

        warnings.Add(new ReceiptWarning(InvalidAccessKey, $"A chave de acesso lida (\"{value}\") é inválida e não foi preenchida. Digite-a se necessário."));
        return null;
    }

    private static DateTime? ParseDate(string? raw, List<ReceiptWarning> warnings)
    {
        var value = TextNormalizer.Clean(raw);
        if (value is null)
        {
            warnings.Add(new ReceiptWarning(InvalidDate, "A data de emissão não foi identificada. Informe-a."));
            return null;
        }

        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        warnings.Add(new ReceiptWarning(InvalidDate, $"A data lida (\"{value}\") é inválida. Informe a data de emissão."));
        return null;
    }

    private async Task<List<DraftItem>> BuildItemsAsync(IReadOnlyList<ExtractedItem> extracted, CancellationToken cancellationToken)
    {
        var normalized = extracted
            .Select(i => TextNormalizer.Clean(i.Description) is { } d ? TextNormalizer.NormalizeDescription(d) : null)
            .ToList();
        var keys = normalized.Where(n => n is not null).Distinct().ToList();

        var aliases = keys.Count == 0
            ? []
            : await db.ProductAliases.AsNoTracking()
                .Where(a => keys.Contains(a.NormalizedDescription))
                .Select(a => new { a.NormalizedDescription, a.ProductId, a.Product.Name, a.Confirmed, a.Product.Status })
                .ToListAsync(cancellationToken);

        var items = new List<DraftItem>(extracted.Count);
        for (var index = 0; index < extracted.Count; index++)
        {
            var item = extracted[index];
            var matches = aliases.Where(a => a.NormalizedDescription == normalized[index]).ToList();
            var confirmed = matches.Where(a => a.Confirmed && a.Status == ProductStatus.Active).ToList();
            var suggestion = confirmed.Count == 1 ? confirmed[0] : null;

            // O total do item é líquido do desconto do item; a IA transcreve o valor bruto da linha.
            decimal? total = item.TotalPrice is { } gross ? gross - (item.Discount ?? 0m) : null;

            items.Add(new DraftItem(
                index + 1,
                TextNormalizer.Clean(item.Description) ?? string.Empty,
                item.Quantity,
                TextNormalizer.Clean(item.Unit)?.ToUpperInvariant(),
                item.UnitPrice,
                item.Discount,
                total,
                suggestion?.ProductId,
                suggestion?.Name,
                matches.Count));
        }

        return items;
    }

    private static void AddPrecisionWarnings(ExtractedReceiptData? data, List<DraftItem> items, List<ReceiptWarning> warnings)
    {
        decimal?[] money = [data?.TotalProducts, data?.Discount, data?.AdditionalCharges, data?.Total,
            .. items.SelectMany(i => new[] { i.Discount, i.Total })];
        decimal?[] fourPlaces = [.. items.SelectMany(i => new[] { i.Quantity, i.UnitPrice })];

        if (money.Any(v => v is { } m && Math.Round(m, DecimalPrecision.Money) != m)
            || fourPlaces.Any(v => v is { } f && Math.Round(f, DecimalPrecision.UnitPrice) != f))
        {
            warnings.Add(new ReceiptWarning(ExcessPrecision, "Alguns valores lidos têm mais casas decimais que o permitido. Confira-os."));
        }
    }

    private static void AddConsistencyWarnings(
        ExtractedReceiptData? data, List<DraftItem> items, string? cnpj, string? accessKey, List<ReceiptWarning> warnings)
    {
        var complete = data is { TotalProducts: not null, Discount: not null, AdditionalCharges: not null, Total: not null }
                       && items.All(i => i is { Quantity: not null, UnitPrice: not null, Total: not null });
        if (!complete)
        {
            warnings.Add(new ReceiptWarning(MissingValues, "Alguns valores não foram lidos. Preencha os campos vazios antes de confirmar."));
            return;
        }

        var receipt = new Receipt
        {
            ProductsTotal = data!.TotalProducts!.Value,
            DiscountTotal = data.Discount!.Value,
            AdditionalCharges = data.AdditionalCharges!.Value,
            Total = data.Total!.Value,
            AccessKey = accessKey,
            Items = items.Select(i => new ReceiptItem
            {
                LineNumber = i.LineNumber,
                OriginalDescription = i.Description,
                Quantity = i.Quantity!.Value,
                UnitPrice = i.UnitPrice!.Value,
                Discount = i.Discount ?? 0m,
                Total = i.Total!.Value
            }).ToList()
        };

        warnings.AddRange(ReceiptConsistency.Check(receipt, cnpj));
    }
}
