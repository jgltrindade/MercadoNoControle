using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.DTOs.Auth;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Auth;
using MercadoNoControle.Api.Services.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Api.Controllers;

/// <summary>Cadastro inicial, login e sessão do usuário único.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, IOptions<AuthOptions> options) : ControllerBase
{
    /// <summary>Indica se a autenticação está ativa, se falta o cadastro inicial e se há sessão.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public Task<AuthStatusResponse> Status(CancellationToken cancellationToken) =>
        authService.GetStatusAsync(User, cancellationToken);

    /// <summary>
    /// Cadastra o usuário único no primeiro acesso e já inicia a sessão. Por padrão, aceito apenas a
    /// partir do próprio computador (<c>Authentication:AllowRemoteSetup</c>).
    /// </summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityConfiguration.AuthRateLimitPolicy)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Setup(SetupRequest request, CancellationToken cancellationToken)
    {
        if (!options.Value.AllowRemoteSetup && !SecurityConfiguration.IsLocalRequest(HttpContext))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Cadastro inicial indisponível",
                detail: "O cadastro inicial só pode ser feito no computador onde a aplicação está instalada.");
        }

        var user = await authService.SetupAsync(request, cancellationToken);
        await SignInAsync(user, persistent: true);
        return AuthService.ToResponse(user);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityConfiguration.AuthRateLimitPolicy)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await authService.LoginAsync(request, cancellationToken);
        await SignInAsync(user, request.RememberMe);
        return AuthService.ToResponse(user);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>Troca a senha. Outras sessões abertas são encerradas; a atual continua ativa.</summary>
    [HttpPut("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await authService.ChangePasswordAsync(CurrentUserId(), request, cancellationToken);
        await SignInAsync(user, persistent: true);
        return NoContent();
    }

    [HttpPut("profile")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<UserResponse> UpdateProfile(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        authService.UpdateProfileAsync(CurrentUserId(), request, cancellationToken);

    private int CurrentUserId() =>
        AuthService.UserIdOf(User)
        ?? throw (options.Value.Enabled
            ? new UnauthorizedException("Sessão expirada. Entre novamente.")
            : new BusinessRuleException("A autenticação está desativada nesta instalação."));

    private Task SignInAsync(AppUser user, bool persistent)
    {
        if (!options.Value.Enabled)
        {
            return Task.CompletedTask;
        }

        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthService.CreatePrincipal(user),
            new AuthenticationProperties { IsPersistent = persistent });
    }
}
