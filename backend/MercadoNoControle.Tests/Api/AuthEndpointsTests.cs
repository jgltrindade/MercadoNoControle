using System.Net;
using System.Net.Http.Json;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Auth;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Auth;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MercadoNoControle.Tests.Api;

/// <summary>
/// Autenticação de usuário único (especificação, seção 10). Cada teste usa uma instalação nova,
/// porque o cadastro inicial só pode acontecer uma vez.
/// </summary>
public class AuthEndpointsTests
{
    private const string Password = "senha-forte-123";

    private static object SetupBody(string email = "eu@example.com") =>
        new { name = "Pessoa Teste", email, password = Password };

    private static async Task<HttpClient> SetupAsync(AuthApiFactory factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/setup", SetupBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    [Fact]
    public async Task First_access_requires_setup_and_protects_the_api()
    {
        using var factory = new AuthApiFactory();
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<AuthStatusResponse>("/api/auth/status");
        Assert.True(status!.Enabled);
        Assert.True(status.SetupRequired);
        Assert.False(status.Authenticated);

        var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var categories = await client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.Unauthorized, categories.StatusCode);
        Assert.Equal("application/problem+json", categories.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Setup_creates_the_user_with_a_hashed_password_and_starts_a_session()
    {
        using var factory = new AuthApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/setup", SetupBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("mnc.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories")).StatusCode);
        var status = await client.GetFromJsonAsync<AuthStatusResponse>("/api/auth/status");
        Assert.True(status!.Authenticated);
        Assert.False(status.SetupRequired);
        Assert.Equal("eu@example.com", status.User!.Email);

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync();
        Assert.DoesNotContain(Password, user.PasswordHash, StringComparison.Ordinal);
        Assert.True(user.PasswordHash.Length > 40);

        var again = await factory.CreateClient().PostAsJsonAsync("/api/auth/setup", SetupBody("outro@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Setup_is_refused_from_another_computer_by_default()
    {
        using var factory = new AuthApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthApiFactory.RemoteIpHeader, "192.168.0.50");

        var response = await client.PostAsJsonAsync("/api/auth/setup", SetupBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var status = await factory.CreateClient().GetFromJsonAsync<AuthStatusResponse>("/api/auth/status");
        Assert.True(status!.SetupRequired);
    }

    [Fact]
    public async Task Setup_validates_the_password()
    {
        using var factory = new AuthApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/setup", new { name = "X", email = "x@example.com", password = "curta" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.True(problem!.Errors.ContainsKey("password"));
    }

    [Fact]
    public async Task Logout_and_login()
    {
        using var factory = new AuthApiFactory();
        var client = await SetupAsync(factory);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "eu@example.com", password = "errada-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var problem = await wrong.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("E-mail ou senha incorretos.", problem!.Detail);

        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "ninguem@example.com", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);

        // E-mail sem diferenciar maiúsculas.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "EU@example.com", password = Password, rememberMe = true });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories")).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_ends_other_sessions()
    {
        using var factory = new AuthApiFactory();
        var client = await SetupAsync(factory);
        var otherDevice = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await otherDevice.PostAsJsonAsync("/api/auth/login", new { email = "eu@example.com", password = Password })).StatusCode);

        var wrongCurrent = await client.PutAsJsonAsync("/api/auth/password", new { currentPassword = "errada-123", newPassword = "nova-senha-456" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongCurrent.StatusCode);
        var problem = await wrongCurrent.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.True(problem!.Errors.ContainsKey("currentPassword"));

        // Garante um instante de alteração diferente do cadastro.
        await Task.Delay(20);
        var changed = await client.PutAsJsonAsync("/api/auth/password", new { currentPassword = Password, newPassword = "nova-senha-456" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/categories")).StatusCode);

        var oldPassword = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "eu@example.com", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        var newPassword = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "eu@example.com", password = "nova-senha-456" });
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task Profile_can_be_updated()
    {
        using var factory = new AuthApiFactory();
        var client = await SetupAsync(factory);

        var response = await client.PutAsJsonAsync("/api/auth/profile", new { name = "Novo Nome", email = "novo@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await client.GetFromJsonAsync<AuthStatusResponse>("/api/auth/status");
        Assert.Equal("Novo Nome", status!.User!.Name);
        Assert.Equal("novo@example.com", status.User.Email);
    }

    [Fact]
    public async Task Repeated_login_attempts_are_limited()
    {
        using var factory = new AuthApiFactory();
        await SetupAsync(factory);
        var attacker = factory.CreateClient();
        attacker.DefaultRequestHeaders.Add(AuthApiFactory.RemoteIpHeader, "10.0.0.9");

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var response = await attacker.PostAsJsonAsync("/api/auth/login", new { email = "eu@example.com", password = $"tentativa-{attempt}" });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(10, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);

        // Outro endereço não é afetado.
        var owner = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "eu@example.com", password = Password });
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        using var factory = new AuthApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }
}

/// <summary>Recuperação de acesso: remover o usuário não afeta os demais dados.</summary>
public class AuthResetTests
{
    [Fact]
    public async Task Reset_removes_only_the_user()
    {
        using var database = SqliteTestDatabase.Create();
        await using (var context = database.CreateContext())
        {
            context.Users.Add(new AppUser { Name = "Pessoa", Email = "eu@example.com", PasswordHash = "hash" });
            context.Stores.Add(new Store { TradeName = "Loja Mantida" });
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            Assert.Equal(1, await AuthService.ResetUsersAsync(context));
        }

        await using var check = database.CreateContext();
        Assert.False(await check.Users.AnyAsync());
        Assert.Equal("Loja Mantida", (await check.Stores.SingleAsync()).TradeName);
    }
}
