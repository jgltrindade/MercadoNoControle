using System.ComponentModel.DataAnnotations;

namespace MercadoNoControle.Api.DTOs.Categories;

public sealed record CategoryResponse(int Id, string Name, string? Description, int ProductCount);

public sealed class CategoryRequest
{
    [Required(ErrorMessage = "Informe o nome.")]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Description { get; set; }
}
