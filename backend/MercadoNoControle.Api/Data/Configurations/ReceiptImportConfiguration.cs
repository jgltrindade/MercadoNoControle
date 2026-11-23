using MercadoNoControle.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MercadoNoControle.Api.Data.Configurations;

public class ReceiptImportConfiguration : IEntityTypeConfiguration<ReceiptImport>
{
    public void Configure(EntityTypeBuilder<ReceiptImport> builder)
    {
        builder.ToTable("ReceiptImports");
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.ImagePath).HasMaxLength(260).IsRequired();
        builder.Property(i => i.OriginalFileName).HasMaxLength(260);
        builder.Property(i => i.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(i => i.Provider).HasMaxLength(50);
        builder.Property(i => i.Model).HasMaxLength(100);
        builder.Property(i => i.Error).HasMaxLength(2000);

        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.CreatedAt);

        // A importação é preservada para rastreabilidade mesmo que a nota seja excluída.
        builder.HasOne(i => i.Receipt)
            .WithMany(r => r.Imports)
            .HasForeignKey(i => i.ReceiptId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
