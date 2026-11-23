namespace MercadoNoControle.Api.Models;

/// <summary>
/// Usuário único da aplicação (especificação: Usuario). A senha é guardada apenas como hash
/// (PBKDF2 com salt, via <c>PasswordHasher</c> do ASP.NET Core).
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Alterar a senha encerra as sessões abertas antes da alteração.</summary>
    public DateTime PasswordChangedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }
}
