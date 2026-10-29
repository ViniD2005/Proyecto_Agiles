using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EduMonitor.Common;
using EduMonitor.Identity.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EduMonitor.Identity.Api.Services;

public interface ITokenService
{
    (string Token, DateTime ExpiraEn) Generar(Usuario usuario, string rol);
}

/// <summary>Emite el JWT con el rol y el tiempo de expiración (HU-04, criterio 3).</summary>
public class TokenService(IOptions<JwtOptions> options, TimeProvider? clock = null) : ITokenService
{
    private readonly JwtOptions _opt = options.Value;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public (string Token, DateTime ExpiraEn) Generar(Usuario usuario, string rol)
    {
        var ahora = _clock.GetUtcNow().UtcDateTime;
        var expira = ahora.AddMinutes(_opt.ExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email.ToLowerInvariant()),
            new("name", usuario.Nombre),
            new("role", rol),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: ahora,
            expires: expira,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
