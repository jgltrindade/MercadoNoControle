using MercadoNoControle.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MercadoNoControle.Api.Data.Configurations;

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("Receipts");
        builder.Property(r => r.Number).HasMaxLength(20);
        builder.Property(r => r.Series).HasMaxLength(10);
        builder.Property(r => r.AccessKey).HasMaxLength(44);
        builder.Property(r => r.PaymentMethod).HasMaxLength(60);
        builder.Property(r => r.ImagePath).HasMaxLength(260);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(r => r.ProductsTotal).HasScaledDecimal(DecimalPrecision.Money);
        builder.Property(r => r.DiscountTotal).HasScaledDecimal(DecimalPrecision.Money);
        builder.Property(r => r.AdditionalCharges).HasScaledDecimal(DecimalPrecision.Money);
        builder.Property(r => r.Total).HasScaledDecimal(DecimalPrecision.Money);

        // RN02: a chave de acesso identifica a nota de forma única quando disponível.
        builder.HasIndex(r => r.AccessKey).IsUnique();
        builder.HasIndex(r => r.IssuedAt);
        builder.HasIndex(r => new { r.Status, r.IssuedAt });
        builder.HasIndex(r => new { r.StoreId, r.IssuedAt });

        // Supermercado com notas não pode ser excluído (exclusão restritiva).
        builder.HasOne(r => r.Store)
            .WithMany(s => s.Receipts)
            .HasForeignKey(r => r.StoreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReceiptItemConfiguration : IEntityTypeConfiguration<ReceiptItem>
{
    public void Configure(EntityTypeBuilder<ReceiptItem> builder)
    {
        builder.ToTable("ReceiptItems");
        builder.Property(i => i.OriginalDescription).HasMaxLength(250).IsRequired();
        builder.Property(i => i.Unit).HasMaxLength(10);

        builder.Property(i => i.Quantity).HasScaledDecimal(DecimalPrecision.Quantity);
        builder.Property(i => i.UnitPrice).HasScaledDecimal(DecimalPrecision.UnitPrice);
        builder.Property(i => i.Discount).HasScaledDecimal(DecimalPrecision.Money);
        builder.Property(i => i.Total).HasScaledDecimal(DecimalPrecision.Money);

        builder.HasIndex(i => new { i.ReceiptId, i.LineNumber });

        // RN05: itens acompanham a nota na exclusão.
        builder.HasOne(i => i.Receipt)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        // Produto referenciado por itens não pode ser excluído (exclusão restritiva).
        builder.HasOne(i => i.Product)
            .WithMany(p => p.ReceiptItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
