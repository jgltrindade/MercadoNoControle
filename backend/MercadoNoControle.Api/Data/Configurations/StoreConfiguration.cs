using MercadoNoControle.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MercadoNoControle.Api.Data.Configurations;

public class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("Stores");
        builder.Property(s => s.TradeName).HasMaxLength(150).IsRequired();
        builder.Property(s => s.LegalName).HasMaxLength(200);
        builder.Property(s => s.Cnpj).HasMaxLength(14);
        builder.Property(s => s.Address).HasMaxLength(250);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.State).HasMaxLength(2);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        // CNPJ identifica o estabelecimento; NULLs são permitidos e não conflitam no SQLite.
        builder.HasIndex(s => s.Cnpj).IsUnique();
        builder.HasIndex(s => s.TradeName);
    }
}
