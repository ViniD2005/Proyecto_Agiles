using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace EduMonitor.Common;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configura la validación de JWT (emitido por MS-A) y las políticas por rol.
    /// Se usa en el Gateway y en cada microservicio (defensa en profundidad).
    /// </summary>
    public static IServiceCollection AddEduMonitorAuth(this IServiceCollection services, IConfiguration config)
    {
        var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key debe configurarse por variable de entorno y tener al menos 32 caracteres.");

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    RoleClaimType = "role",
                    NameClaimType = "email"
                };
            });

        services.AddAuthorization(o =>
        {
            o.AddPolicy(Policies.Autenticado, p => p.RequireAuthenticatedUser());
            o.AddPolicy(Policies.SoloDocente, p => p.RequireRole(Roles.Docente));
            o.AddPolicy(Policies.SoloAdministrador, p => p.RequireRole(Roles.AdministradorTecnico));
        });

        return services;
    }

    /// <summary>Swagger con soporte para enviar el JWT (botón Authorize).</summary>
    public static IServiceCollection AddEduMonitorSwagger(this IServiceCollection services, string title)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = title, Version = "v1" });
            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Pegue el token JWT obtenido en POST /api/auth/login",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };
            c.AddSecurityDefinition("Bearer", scheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement { { scheme, Array.Empty<string>() } });
        });
        return services;
    }
}

/// <summary>Atajos para leer los claims del usuario autenticado.</summary>
public static class ClaimsPrincipalExtensions
{
    public static string? GetEmail(this ClaimsPrincipal user) =>
        user.FindFirst("email")?.Value?.Trim().ToLowerInvariant();

    public static string? GetUserId(this ClaimsPrincipal user) => user.FindFirst("sub")?.Value;

    public static string? GetRole(this ClaimsPrincipal user) => user.FindFirst("role")?.Value;
}
