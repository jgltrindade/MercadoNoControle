using System.Globalization;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Products;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services.Demo;

public sealed record DemoDataResult(int Stores, int Products, int ConfirmedReceipts, int DraftReceipts, bool Skipped);

/// <summary>
/// Gera dados fictícios para demonstração e testes pessoais: supermercados, produtos e cerca de seis
/// meses de compras com preços que variam por loja e sobem ao longo do tempo. Tudo passa pelos
/// serviços da aplicação (mesmas regras do cadastro manual), então histórico de preços, preço
/// normalizado, descrições aprendidas, dashboard e relatórios ficam coerentes.
/// Os supermercados levam <see cref="Marker"/> nas observações, o que permite removê-los depois.
/// </summary>
public sealed class DemoDataSeeder(
    AppDbContext db,
    IStoreService storeService,
    IProductService productService,
    Receipts.IReceiptService receiptService,
    TimeProvider timeProvider,
    ILogger<DemoDataSeeder> logger)
{
    public const string Marker = "Dados fictícios de demonstração (gerados com --seed-demo; remova com --remove-demo).";

    private static readonly string[] PaymentMethods = ["Cartão de débito", "Cartão de crédito", "Pix", "Dinheiro"];

    private sealed record DemoStore(string TradeName, string LegalName, string CnpjBase, string Address, string City, decimal PriceFactor, bool ProduceOnly = false);

    /// <param name="Bulk">Vendido a granel (preço por kg; quantidade fracionada).</param>
    /// <param name="MonthlyIncrease">Aumento mensal aproximado do preço (ex.: 0,02 = 2% ao mês).</param>
    private sealed record DemoProduct(
        string Name,
        string? Brand,
        string Category,
        MeasurementUnit Unit,
        decimal? PackageContent,
        string ReceiptDescription,
        decimal BasePrice,
        decimal MonthlyIncrease,
        bool Bulk = false);

    private static readonly DemoStore[] Stores =
    [
        new("Supermercado Bom Preço", "Bom Preço Comércio de Alimentos Ltda", "481920370001", "Av. Norte Miguel Arraes, 1500", "Recife", 1.00m),
        new("Atacarejo Nordeste", "Nordeste Atacado e Varejo S.A.", "573016420001", "BR-101 Sul, km 82", "Jaboatão dos Guararapes", 0.92m),
        new("Mercadinho São José", "J. S. Oliveira Mercearia ME", "290874150001", "Rua São José, 210", "Olinda", 1.09m),
        new("Hortifruti Casa Amarela", "Casa Amarela Hortifrutigranjeiros Ltda", "615203880001", "Estrada do Arraial, 3300", "Recife", 0.95m, ProduceOnly: true)
    ];

    private static readonly DemoProduct[] Products =
    [
        new("Arroz Tipo 1 5kg", "Camil", "Mercearia", MeasurementUnit.KG, 5m, "ARROZ T1 CAMIL 5KG", 27.90m, 0.020m),
        new("Feijão Carioca 1kg", "Kicaldo", "Mercearia", MeasurementUnit.KG, 1m, "FEIJAO CARIOCA KICALDO 1KG", 8.49m, 0.012m),
        new("Café Torrado e Moído 500g", "Pilão", "Mercearia", MeasurementUnit.G, 500m, "CAFE PILAO TRADICIONAL 500G", 18.90m, 0.035m),
        new("Açúcar Cristal 1kg", "União", "Mercearia", MeasurementUnit.KG, 1m, "ACUCAR CRISTAL UNIAO 1KG", 4.79m, 0.008m),
        new("Óleo de Soja 900ml", "Soya", "Mercearia", MeasurementUnit.ML, 900m, "OLEO SOJA SOYA 900ML", 7.49m, 0.015m),
        new("Macarrão Espaguete 500g", "Vitarella", "Mercearia", MeasurementUnit.G, 500m, "MAC ESPAGUETE VITARELLA 500G", 4.29m, 0.005m),
        new("Molho de Tomate 300g", "Quero", "Mercearia", MeasurementUnit.G, 300m, "MOLHO TOMATE QUERO 300G", 2.99m, 0.006m),
        new("Banana Prata", null, "Hortifruti", MeasurementUnit.KG, null, "BANANA PRATA KG", 5.99m, 0.010m, Bulk: true),
        new("Tomate", null, "Hortifruti", MeasurementUnit.KG, null, "TOMATE KG", 6.49m, 0.025m, Bulk: true),
        new("Batata Inglesa", null, "Hortifruti", MeasurementUnit.KG, null, "BATATA INGLESA KG", 5.49m, 0.010m, Bulk: true),
        new("Cebola", null, "Hortifruti", MeasurementUnit.KG, null, "CEBOLA KG", 4.99m, 0.018m, Bulk: true),
        new("Maçã Gala", null, "Hortifruti", MeasurementUnit.KG, null, "MACA GALA KG", 9.90m, 0.008m, Bulk: true),
        new("Peito de Frango", null, "Carnes", MeasurementUnit.KG, null, "PEITO FRANGO RESF KG", 17.90m, 0.012m, Bulk: true),
        new("Carne Moída", null, "Carnes", MeasurementUnit.KG, null, "CARNE MOIDA 1A KG", 34.90m, 0.015m, Bulk: true),
        new("Linguiça Toscana", "Sadia", "Carnes", MeasurementUnit.KG, null, "LINGUICA TOSCANA SADIA KG", 22.90m, 0.010m, Bulk: true),
        new("Leite Integral 1L", "Italac", "Laticínios", MeasurementUnit.L, 1m, "LEITE INT ITALAC 1L", 5.29m, 0.018m),
        new("Queijo Mussarela", null, "Laticínios", MeasurementUnit.KG, null, "QUEIJO MUSSARELA FATIADO KG", 49.90m, 0.010m, Bulk: true),
        new("Iogurte Natural 170g", "Nestlé", "Laticínios", MeasurementUnit.G, 170m, "IOGURTE NAT NESTLE 170G", 3.49m, 0.006m),
        new("Manteiga com Sal 200g", "Aviação", "Laticínios", MeasurementUnit.G, 200m, "MANTEIGA AVIACAO C/SAL 200G", 13.90m, 0.012m),
        new("Refrigerante Cola 2L", "Coca-Cola", "Bebidas", MeasurementUnit.L, 2m, "REFRIG COCA COLA 2L", 10.99m, 0.008m),
        new("Suco de Laranja 1L", "Del Valle", "Bebidas", MeasurementUnit.L, 1m, "SUCO DEL VALLE LARANJA 1L", 8.99m, 0.006m),
        new("Água Mineral 1,5L", "Indaiá", "Bebidas", MeasurementUnit.L, 1.5m, "AGUA MINERAL INDAIA 1,5L", 2.49m, 0.004m),
        new("Creme Dental 90g", "Colgate", "Higiene pessoal", MeasurementUnit.G, 90m, "CR DENTAL COLGATE 90G", 4.99m, 0.005m),
        new("Sabonete 85g", "Dove", "Higiene pessoal", MeasurementUnit.G, 85m, "SABONETE DOVE ORIGINAL 85G", 3.99m, 0.005m),
        new("Papel Higiênico 12 rolos", "Neve", "Higiene pessoal", MeasurementUnit.UN, 12m, "PAPEL HIG NEVE FD 12UN", 21.90m, 0.008m),
        new("Sabão em Pó 1,6kg", "Omo", "Limpeza", MeasurementUnit.KG, 1.6m, "SABAO PO OMO 1,6KG", 24.90m, 0.010m),
        new("Detergente 500ml", "Ypê", "Limpeza", MeasurementUnit.ML, 500m, "DETERGENTE YPE NEUTRO 500ML", 2.69m, 0.004m),
        new("Água Sanitária 1L", "Qboa", "Limpeza", MeasurementUnit.L, 1m, "AGUA SANITARIA QBOA 1L", 4.49m, 0.004m),
        new("Pizza Congelada Mussarela 460g", "Sadia", "Congelados", MeasurementUnit.G, 460m, "PIZZA SADIA MUSSARELA 460G", 19.90m, 0.008m),
        new("Pão Francês", null, "Padaria", MeasurementUnit.KG, null, "PAO FRANCES KG", 15.90m, 0.010m, Bulk: true),
        new("Pão de Forma 500g", "Pullman", "Padaria", MeasurementUnit.G, 500m, "PAO FORMA PULLMAN 500G", 8.99m, 0.008m)
    ];

    /// <summary>Itens comuns em notas que não correspondem a um produto (para a associação posterior).</summary>
    private static readonly (string Description, decimal Price)[] LooseItems =
    [
        ("SACOLA PLASTICA REUTILIZ", 0.20m),
        ("BOMBOM SORTIDO UN", 1.50m),
        ("GELO 2KG", 6.00m)
    ];

    public async Task<DemoDataResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Stores.AnyAsync(s => s.Notes == Marker, cancellationToken))
        {
            logger.LogWarning("Os dados fictícios já foram gerados. Para gerar de novo, remova-os antes com --remove-demo.");
            return new DemoDataResult(0, 0, 0, 0, Skipped: true);
        }

        var random = new Random(2026);
        var storeIds = new List<(DemoStore Store, int Id)>();
        foreach (var store in Stores)
        {
            var created = await storeService.CreateAsync(new StoreRequest
            {
                TradeName = store.TradeName,
                LegalName = store.LegalName,
                Cnpj = WithCheckDigits(store.CnpjBase),
                Address = store.Address,
                City = store.City,
                State = "PE",
                Notes = Marker
            }, cancellationToken);
            storeIds.Add((store, created.Id));
        }

        var (productIds, createdProducts) = await EnsureProductsAsync(cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
        var receiptNumbers = storeIds.ToDictionary(s => s.Id, _ => random.Next(20000, 90000));
        var confirmed = 0;
        ReceiptRequest? lastRequest = null;

        for (var day = start; day < today; day = day.AddDays(1))
        {
            // Compra grande aos sábados (alternando supermercados, com o atacarejo no início do mês),
            // compras pequenas às quartas no mercadinho ou no hortifruti.
            (DemoStore Store, int Id) target;
            int itemCount;
            if (day.DayOfWeek == DayOfWeek.Saturday)
            {
                target = day.Day <= 7 ? storeIds[1] : storeIds[day.DayOfYear / 7 % 2 == 0 ? 0 : 1];
                itemCount = random.Next(10, 19);
            }
            else if (day.DayOfWeek == DayOfWeek.Wednesday)
            {
                target = random.Next(2) == 0 ? storeIds[2] : storeIds[3];
                itemCount = random.Next(3, 7);
            }
            else
            {
                continue;
            }

            var monthsElapsed = (day.DayNumber - start.DayNumber) / 30.4m;
            var request = BuildReceipt(random, target.Store, target.Id, day, monthsElapsed, itemCount, productIds, receiptNumbers);
            var receipt = await receiptService.CreateAsync(request, cancellationToken);
            await receiptService.ConfirmAsync(receipt.Id, new ConfirmReceiptRequest { AcknowledgeWarnings = true }, cancellationToken);
            confirmed++;
            lastRequest = request;
        }

        // Um rascunho (compra registrada e ainda não confirmada), que não entra nos relatórios.
        var draftDay = today.AddDays(-1);
        var draft = BuildReceipt(random, storeIds[2].Store, storeIds[2].Id, draftDay, (draftDay.DayNumber - start.DayNumber) / 30.4m, 4, productIds, receiptNumbers);
        await receiptService.CreateAsync(draft, cancellationToken);

        logger.LogInformation(
            "Dados fictícios gerados: {Stores} supermercados, {Products} produtos novos, {Receipts} compras confirmadas e 1 rascunho (de {Start:dd/MM/yyyy} até {End:dd/MM/yyyy}).",
            storeIds.Count, createdProducts, confirmed, start, lastRequest?.IssuedAt ?? draftDay.ToDateTime(TimeOnly.MinValue));
        return new DemoDataResult(storeIds.Count, createdProducts, confirmed, 1, Skipped: false);
    }

    /// <summary>
    /// Remove as compras dos supermercados fictícios (com itens e histórico), os próprios supermercados
    /// e os produtos fictícios que ficaram sem compras. Dados cadastrados pelo usuário são preservados.
    /// </summary>
    public async Task<DemoDataResult> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var demoStores = await db.Stores.Where(s => s.Notes == Marker).Select(s => new { s.Id, s.CreatedAt }).ToListAsync(cancellationToken);
        var storeIds = demoStores.Select(s => s.Id).ToList();
        // Produtos que já existiam antes da geração (cadastrados pelo usuário com o mesmo nome) são preservados.
        DateTime? seededAt = demoStores.Count > 0 ? demoStores.Min(s => s.CreatedAt) : null;
        var receiptIds = await db.Receipts.Where(r => storeIds.Contains(r.StoreId)).Select(r => r.Id).ToListAsync(cancellationToken);
        foreach (var receiptId in receiptIds)
        {
            await receiptService.DeleteAsync(receiptId, cancellationToken);
        }

        foreach (var storeId in storeIds)
        {
            await storeService.DeleteAsync(storeId, cancellationToken);
        }

        var names = Products.Select(p => p.Name).ToList();
        var unusedDemoProducts = seededAt is null
            ? []
            : await db.Products
                .Where(p => names.Contains(p.Name) && p.CreatedAt >= seededAt && !p.ReceiptItems.Any() && !p.PriceObservations.Any())
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
        foreach (var productId in unusedDemoProducts)
        {
            await productService.DeleteAsync(productId, cancellationToken);
        }

        logger.LogInformation(
            "Dados fictícios removidos: {Stores} supermercados, {Receipts} compras e {Products} produtos.",
            storeIds.Count, receiptIds.Count, unusedDemoProducts.Count);
        return new DemoDataResult(storeIds.Count, unusedDemoProducts.Count, receiptIds.Count, 0, Skipped: false);
    }

    /// <summary>Usa produtos já cadastrados com o mesmo nome; cria os que faltam.</summary>
    private async Task<(Dictionary<string, int> Ids, int Created)> EnsureProductsAsync(CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existing = (await db.Products.AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var ids = new Dictionary<string, int>();
        var created = 0;
        foreach (var product in Products)
        {
            if (existing.TryGetValue(product.Name, out var id))
            {
                ids[product.Name] = id;
                continue;
            }

            var response = await productService.CreateAsync(new ProductRequest
            {
                Name = product.Name,
                Brand = product.Brand,
                CategoryId = categories.TryGetValue(product.Category, out var categoryId) ? categoryId : null,
                Unit = product.Unit,
                PackageContent = product.PackageContent
            }, cancellationToken);
            ids[product.Name] = response.Id;
            created++;
        }

        return (ids, created);
    }

    private static ReceiptRequest BuildReceipt(
        Random random,
        DemoStore store,
        int storeId,
        DateOnly day,
        decimal monthsElapsed,
        int itemCount,
        Dictionary<string, int> productIds,
        Dictionary<int, int> receiptNumbers)
    {
        var catalog = store.ProduceOnly ? Products.Where(p => p.Category is "Hortifruti" or "Padaria").ToArray() : Products;
        var chosen = catalog.OrderBy(_ => random.Next()).Take(Math.Min(itemCount, catalog.Length)).ToList();

        var items = new List<ReceiptItemRequest>();
        foreach (var product in chosen)
        {
            var noise = 1m + (decimal)(random.NextDouble() * 0.06 - 0.03);
            var price = Money(product.BasePrice * store.PriceFactor * Growth(product.MonthlyIncrease, monthsElapsed) * noise);
            var quantity = product.Bulk
                ? Math.Round(0.250m + (decimal)random.NextDouble() * 1.5m, 3)
                : random.Next(10) < 7 ? 1m : random.Next(2, 4);
            var gross = Money(quantity * price);
            // Promoções ocasionais (desconto no item).
            var discount = !product.Bulk && random.Next(10) == 0 ? Money(gross * (0.05m + random.Next(0, 11) / 100m)) : 0m;
            items.Add(new ReceiptItemRequest
            {
                Description = product.ReceiptDescription,
                ProductId = productIds[product.Name],
                Quantity = quantity,
                Unit = product.Bulk ? "KG" : "UN",
                UnitPrice = price,
                Discount = discount,
                Total = gross - discount
            });
        }

        // De vez em quando, um item sem produto associado (aparece em "Itens sem produto").
        if (!store.ProduceOnly && random.Next(4) == 0)
        {
            var (description, price) = LooseItems[random.Next(LooseItems.Length)];
            var quantity = random.Next(1, 4);
            items.Add(new ReceiptItemRequest
            {
                Description = description,
                Quantity = quantity,
                Unit = "UN",
                UnitPrice = price,
                Total = Money(quantity * price)
            });
        }

        var productsTotal = items.Sum(i => i.Total + i.Discount);
        var discountTotal = items.Sum(i => i.Discount);
        receiptNumbers[storeId]++;
        return new ReceiptRequest
        {
            StoreId = storeId,
            Number = receiptNumbers[storeId].ToString(CultureInfo.InvariantCulture),
            Series = "1",
            IssuedAt = day.ToDateTime(new TimeOnly(random.Next(8, 20), random.Next(0, 60))),
            ProductsTotal = productsTotal,
            DiscountTotal = discountTotal,
            AdditionalCharges = 0m,
            Total = productsTotal - discountTotal,
            PaymentMethod = PaymentMethods[random.Next(PaymentMethods.Length)],
            Items = items
        };
    }

    private static decimal Growth(decimal monthlyIncrease, decimal months)
    {
        var factor = 1m;
        var whole = (int)months;
        for (var i = 0; i < whole; i++)
        {
            factor *= 1m + monthlyIncrease;
        }

        return factor * (1m + monthlyIncrease * (months - whole));
    }

    private static decimal Money(decimal value) => DecimalPrecision.Round(value, DecimalPrecision.Money);

    /// <summary>CNPJ fictício válido: 12 dígitos-base + dígitos verificadores calculados.</summary>
    public static string WithCheckDigits(string baseDigits)
    {
        static int Digit(string digits, int[] weights)
        {
            var sum = digits.Select((c, i) => (c - '0') * weights[i]).Sum();
            var rest = sum % 11;
            return rest < 2 ? 0 : 11 - rest;
        }

        var first = Digit(baseDigits, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var withFirst = baseDigits + first.ToString(CultureInfo.InvariantCulture);
        var second = Digit(withFirst, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return withFirst + second.ToString(CultureInfo.InvariantCulture);
    }
}
