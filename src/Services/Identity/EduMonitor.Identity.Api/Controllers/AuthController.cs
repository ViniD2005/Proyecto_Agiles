using EduMonitor.Common;
using EduMonitor.Identity.Api.Contracts;
using EduMonitor.Identity.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth, IValidator<LoginRequest> validator) : ControllerBase
{
    /// <summary>Autenticación del docente / administrador técnico. Devuelve el JWT.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await auth.LoginAsync(request, ip, ct);
        if (result is null)
            return Unauthorized(new ProblemDetails { Title = "Credenciales incorrectas", Detail = "Usuario o contraseña inválidos.", Status = 401 });

        return Ok(result);
    }

    /// <summary>Datos del usuario autenticado (leídos del token).</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.Autenticado)]
    public IActionResult Me() => Ok(new
    {
        id = User.GetUserId(),
        email = User.GetEmail(),
        nombre = User.FindFirst("name")?.Value,
        rol = User.GetRole()
    });
}
