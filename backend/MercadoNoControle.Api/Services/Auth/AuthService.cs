using System.Globalization;
using System.Security.Claims;
using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Auth;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Exceptions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Api.Services.Auth;

public interface IAuthService
{
    Task<AuthStatusResponse> GetStatusAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    Task<AppUser> SetupAsync(SetupRequest request, CancellationToken cancellationToken);

    Task<AppUser> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AppUser> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken cancellationToken);

    Task<UserResponse> UpdateProfileAsync(int userId, UpdateProfileRequest request, CancellationToken cancellationToken);

    /// <summary>A sessão continua válida enquanto o usuário existir e a senha não tiver sido trocada depois dela.</summary>
    Task<bool> IsSessionValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

/// <summary>
/// Autenticação de usuário único (especificação, seção 10): sem perfis nem permissões; a senha é
/// guardada como hash PBKDF2 com salt. O primeiro acesso cadastra o usuário.
/// </summary>
public sealed class AuthService(
    AppDbContext db,
    IPasswordHasher<AppUser> passwordHasher,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider) : IAuthService
{
    public const string PasswordStampClaim = "mnc:pwd";
    public const string InvalidCredentialsMessage = "E-mail ou senha incorretos.";

    public async Task<AuthStatusResponse> GetStatusAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var setupRequired = !await db.Users.AnyAsync(cancellationToken);
        if (!options.Value.Enabled)
        {
            return new AuthStatusResponse(false, false, true, null);
        }

        var userId = UserIdOf(principal);
        var user = userId is null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return new AuthStatusResponse(true, setupRequired, user is not null, user is null ? null : ToResponse(user));
    }

    public async Task<AppUser> SetupAsync(SetupRequest request, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            throw new ConflictException("O usuário já foi cadastrado. Entre com o e-mail e a senha.");
        }

        var now = Now();
        var user = new AppUser
        {
            Name = request.Name.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = string.Empty,
            CreatedAt = now,
            PasswordChangedAt = now,
            LastLoginAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<AppUser> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        user.LastLoginAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<AppUser> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userId, cancellationToken);
        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            throw new BusinessRuleException(
                "A senha atual está incorreta.",
                new Dictionary<string, string[]> { ["currentPassword"] = ["A senha atual está incorreta."] });
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.PasswordChangedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<UserResponse> UpdateProfileAsync(int userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userId, cancellationToken);
        user.Name = request.Name.Trim();
        user.Email = request.Email.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(user);
    }

    public async Task<bool> IsSessionValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = UserIdOf(principal);
        var stamp = principal.FindFirstValue(PasswordStampClaim);
        if (userId is null || stamp is null)
        {
            return false;
        }

        var changedAt = await db.Users.Where(u => u.Id == userId).Select(u => (DateTime?)u.PasswordChangedAt).SingleOrDefaultAsync(cancellationToken);
        return changedAt is not null && StampOf(changedAt.Value) == stamp;
    }

    /// <summary>
    /// Recuperação de acesso (senha esquecida): remove o usuário para que o cadastro inicial possa ser
    /// refeito. Compras e demais dados não são afetados. Acionado por <c>Authentication:ResetUser</c>.
    /// </summary>
    public static Task<int> ResetUsersAsync(AppDbContext db, CancellationToken cancellationToken = default) =>
        db.Users.ExecuteDeleteAsync(cancellationToken);

    /// <summary>Identidade gravada no cookie de sessão.</summary>
    public static ClaimsPrincipal CreatePrincipal(AppUser user) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(PasswordStampClaim, StampOf(user.PasswordChangedAt))
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));

    public static int? UserIdOf(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    public static UserResponse ToResponse(AppUser user) => new(user.Id, user.Name, user.Email, user.LastLoginAt);

    private static string StampOf(DateTime passwordChangedAt) => passwordChangedAt.Ticks.ToString(CultureInfo.InvariantCulture);

    private async Task<AppUser> FindAsync(int userId, CancellationToken cancellationToken) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
        ?? throw new UnauthorizedException("Sessão expirada. Entre novamente.");

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
