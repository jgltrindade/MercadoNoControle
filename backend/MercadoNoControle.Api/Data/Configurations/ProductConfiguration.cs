using MercadoNoControle.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MercadoNoControle.Api.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Brand).HasMaxLength(100);
        builder.Property(p => p.Barcode).HasMaxLength(14);
        builder.Property(p => p.Unit).HasConversion<string>().HasMaxLength(3);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PackageContent).HasScaledDecimal(DecimalPrecision.PackageContent);

        builder.HasIndex(p => p.Name);
        builder.HasIndex(p => p.Barcode).IsUnique();

        // Categoria com produtos não pode ser excluída (exclusão restritiva).
        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductAliasConfiguration : IEntityTypeConfiguration<ProductAlias>
{
    public void Configure(EntityTypeBuilder<ProductAlias> builder)
    {
        builder.ToTable("ProductAliases");
        builder.Property(a => a.Description).HasMaxLength(250).IsRequired();
        builder.Property(a => a.NormalizedDescription).HasMaxLength(250).IsRequired();
        builder.Property(a => a.Source).HasConversion<string>().HasMaxLength(20);

        // A mesma descrição pode, em lojas diferentes, corresponder a produtos distintos;
        // por isso a unicidade é por produto, e a busca usa o índice da descrição.
        builder.HasIndex(a => new { a.ProductId, a.NormalizedDescription }).IsUnique();
        builder.HasIndex(a => a.NormalizedDescription);

        builder.HasOne(a => a.Product)
            .WithMany(p => p.Aliases)
            .HasForeignKey(a => a.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
