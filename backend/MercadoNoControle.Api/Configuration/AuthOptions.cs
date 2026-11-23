namespace MercadoNoControle.Api.Configuration;

/// <summary>
/// Autenticação de usuário único (seção "Authentication").
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// Exige login para usar a API (padrão). Desativar apenas em testes automatizados ou em uso
    /// estritamente local e consciente dos riscos (README → "Modelo de acesso").
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Duração da sessão, renovada com o uso.</summary>
    public int SessionDays { get; set; } = 30;

    /// <summary>
    /// Permite criar o usuário inicial a partir de outro computador. Por padrão, o cadastro inicial
    /// só é aceito a partir do próprio computador (loopback), para que ninguém na rede o faça antes.
    /// </summary>
    public bool AllowRemoteSetup { get; set; }
}
