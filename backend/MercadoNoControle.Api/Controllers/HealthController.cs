using MercadoNoControle.Api.DTOs;
using MercadoNoControle.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Controllers;

[ApiController]
[Route("api/health")]
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public class HealthController(ISystemStatusService systemStatusService) : ControllerBase
{
    /// <summary>Verifica se a API e o banco de dados estão disponíveis.</summary>
    [HttpGet]
    [ProducesResponseType<SystemStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SystemStatusResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SystemStatusResponse>> Get(CancellationToken cancellationToken)
    {
        var status = await systemStatusService.GetStatusAsync(cancellationToken);
        return status.DatabaseAvailable ? Ok(status) : StatusCode(StatusCodes.Status503ServiceUnavailable, status);
    }
}
