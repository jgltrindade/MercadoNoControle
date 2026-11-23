using MercadoNoControle.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MercadoNoControle.Api.Data.Configurations;

public class PriceObservationConfiguration : IEntityTypeConfiguration<PriceObservation>
{
    public void Configure(EntityTypeBuilder<PriceObservation> builder)
    {
        builder.ToTable("PriceObservations");
        builder.Property(o => o.Quantity).HasScaledDecimal(DecimalPrecision.Quantity);
        builder.Property(o => o.UnitPrice).HasScaledDecimal(DecimalPrecision.UnitPrice);
        builder.Property(o => o.TotalPrice).HasScaledDecimal(DecimalPrecision.Money);
        builder.Property(o => o.NormalizedPrice).HasScaledDecimal(DecimalPrecision.NormalizedPrice);
        builder.Property(o => o.NormalizedUnit).HasConversion<string>().HasMaxLength(10);

        builder.HasIndex(o => new { o.ProductId, o.ObservedAt });
        builder.HasIndex(o => new { o.StoreId, o.ObservedAt });

        // Um item gera no máximo uma observação; RN05: a observação sai junto com o item.
        builder.HasOne(o => o.ReceiptItem)
            .WithOne(i => i.PriceObservation)
            .HasForeignKey<PriceObservation>(o => o.ReceiptItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.Product)
            .WithMany(p => p.PriceObservations)
            .HasForeignKey(o => o.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Store)
            .WithMany()
            .HasForeignKey(o => o.StoreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
