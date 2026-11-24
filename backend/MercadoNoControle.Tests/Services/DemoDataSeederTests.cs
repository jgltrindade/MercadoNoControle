using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Analytics;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Analytics;
using MercadoNoControle.Api.Services.Demo;
using MercadoNoControle.Api.Validators;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MercadoNoControle.Tests.Services;

/// <summary>Dados fictícios: gerados pelos serviços reais, coerentes e removíveis sem afetar dados do usuário.</summary>
public class DemoDataSeederTests
{
    [Fact]
    public async Task Seeds_consistent_demo_data_once_and_removes_it_keeping_user_data()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();

        // Cadastro do próprio usuário com o mesmo nome de um produto fictício: deve ser reaproveitado e mantido.
        db.Products.Add(new Product { Name = "Leite Integral 1L", Unit = MeasurementUnit.L, PackageContent = 1m, CreatedAt = DateTime.UtcNow });
        db.Stores.Add(new Store { TradeName = "Minha Loja", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var seeder = services.GetRequiredService<DemoDataSeeder>();
        var result = await seeder.SeedAsync();

        Assert.False(result.Skipped);
        Assert.Equal(4, result.Stores);
        Assert.True(result.ConfirmedReceipts >= 40, $"Compras geradas: {result.ConfirmedReceipts}");
        Assert.Equal(1, await db.Products.CountAsync(p => p.Name == "Leite Integral 1L"));
        Assert.Equal(1, await db.Receipts.CountAsync(r => r.Status == ReceiptStatus.Draft));
        Assert.All(await db.Stores.Where(s => s.Notes == DemoDataSeeder.Marker).Select(s => s.Cnpj).ToListAsync(),
            cnpj => Assert.True(CnpjValidator.IsValid(cnpj)));

        // Coerência: histórico para cada item associado, itens sem produto, totais = soma dos itens.
        var items = await db.ReceiptItems.Where(i => i.Receipt.Status == ReceiptStatus.Confirmed).ToListAsync();
        Assert.Equal(items.Count(i => i.ProductId != null), await db.PriceObservations.CountAsync());
        Assert.Contains(items, i => i.ProductId == null);
        var receipts = await db.Receipts.Include(r => r.Items).ToListAsync();
        Assert.All(receipts, r => Assert.Equal(r.Total, r.Items.Sum(i => i.Total)));

        // Preços sobem ao longo do tempo (café: maior aumento mensal).
        // Compara no mesmo supermercado (os preços variam entre lojas) e sem desconto (preço unitário).
        var coffee = (await db.PriceObservations.Where(o => o.Product.Name == "Café Torrado e Moído 500g").ToListAsync())
            .GroupBy(o => o.StoreId)
            .Select(g => g.OrderBy(o => o.ObservedAt).ToList())
            .Where(list => (list[^1].ObservedAt - list[0].ObservedAt).TotalDays >= 90)
            .ToList();
        Assert.NotEmpty(coffee);
        Assert.All(coffee, list => Assert.True(list[^1].UnitPrice > list[0].UnitPrice));

        // Dashboard reflete as compras confirmadas (o rascunho fica de fora).
        var summary = await services.GetRequiredService<IDashboardService>()
            .GetSummaryAsync(new AnalyticsQuery { From = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)), To = DateOnly.FromDateTime(DateTime.Today) }, CancellationToken.None);
        Assert.Equal(result.ConfirmedReceipts, summary.PurchaseCount);
        Assert.Equal(receipts.Where(r => r.Status == ReceiptStatus.Confirmed).Sum(r => r.Total), summary.TotalSpent);

        // Uma segunda execução não duplica.
        Assert.True((await seeder.SeedAsync()).Skipped);

        await seeder.RemoveAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(["Minha Loja"], await db.Stores.Select(s => s.TradeName).ToListAsync());
        Assert.Equal(["Leite Integral 1L"], await db.Products.Select(p => p.Name).ToListAsync());
        Assert.False(await db.Receipts.AnyAsync());
        Assert.False(await db.PriceObservations.AnyAsync());
    }
}
