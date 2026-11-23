using System.ComponentModel.DataAnnotations;

namespace MercadoNoControle.Api.DTOs.Auth;

public sealed record UserResponse(int Id, string Name, string Email, DateTime? LastLoginAt);

/// <summary>Situação da autenticação para o frontend decidir entre cadastro inicial, login ou aplicação.</summary>
public sealed record AuthStatusResponse(bool Enabled, bool SetupRequired, bool Authenticated, UserResponse? User);

public sealed class SetupRequest
{
    [Required(ErrorMessage = "Informe o nome.")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [MaxLength(254)]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    [MinLength(8, ErrorMessage = "A senha deve ter ao menos 8 caracteres.")]
    [MaxLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Mantém a sessão após fechar o navegador.</summary>
    public bool RememberMe { get; set; }
}

public sealed class ChangePasswordRequest
{
    [Required(ErrorMessage = "Informe a senha atual.")]
    [MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a nova senha.")]
    [MinLength(8, ErrorMessage = "A senha deve ter ao menos 8 caracteres.")]
    [MaxLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class UpdateProfileRequest
{
    [Required(ErrorMessage = "Informe o nome.")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [MaxLength(254)]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string Email { get; set; } = string.Empty;
}
