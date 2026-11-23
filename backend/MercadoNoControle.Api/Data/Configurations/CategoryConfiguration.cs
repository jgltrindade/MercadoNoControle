using MercadoNoControle.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MercadoNoControle.Api.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    /// <summary>Categorias iniciais definidas na especificação (4.6).</summary>
    public static readonly IReadOnlyList<string> InitialCategories =
    [
        "Mercearia",
        "Hortifruti",
        "Carnes",
        "Laticínios",
        "Bebidas",
        "Higiene pessoal",
        "Limpeza",
        "Congelados",
        "Padaria",
        "Outros"
    ];

    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired().UseCollation("NOCASE");
        builder.Property(c => c.Description).HasMaxLength(250);
        builder.HasIndex(c => c.Name).IsUnique();

        builder.HasData(InitialCategories.Select((name, index) => new Category { Id = index + 1, Name = name }));
    }
}
