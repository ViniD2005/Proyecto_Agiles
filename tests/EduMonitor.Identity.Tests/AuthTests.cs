using System.IdentityModel.Tokens.Jwt;
using EduMonitor.Common;
using EduMonitor.Identity.Api.Contracts;
using EduMonitor.Identity.Api.Data;
using EduMonitor.Identity.Api.Domain;
using EduMonitor.Identity.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace EduMonitor.Identity.Tests;

/// <summary>T4.6 - Pruebas unitarias de login, token y rol (HU-04).</summary>
public class AuthTests
{
    private const string Clave = "clave-de-pruebas-con-mas-de-32-caracteres!!";
    private static readonly JwtOptions Opciones = new() { Key = Clave, Issuer = "test-iss", Audience = "test-aud", ExpirationMinutes = 60 };

    private static IdentityDbContext CrearDb()
    {
        var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.AddRange(new Rol { Id = 1, Nombre = Roles.Docente }, new Rol { Id = 2, Nombre = Roles.AdministradorTecnico });
        db.Usuarios.Add(new Usuario
        {
            Id = 10, Nombre = "Docente Prueba", Email = "docente@uta.edu.ec", RolId = 1,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Correcta#1", workFactor: 4)
        });
        db.SaveChanges();
        return db;
    }

    private static AuthService CrearServicio(IdentityDbContext db) =>
        new(db, new TokenService(Options.Create(Opciones)));

    [Fact]
    public async Task Login_ConCredencialesCorrectas_DevuelveTokenConRolDocente()
    {
        using var db = CrearDb();
        var r = await CrearServicio(db).LoginAsync(new LoginRequest("docente@uta.edu.ec", "Correcta#1"), "127.0.0.1");

        Assert.NotNull(r);
        Assert.Equal(Roles.Docente, r!.Usuario.Rol);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(r.Token);
        Assert.Equal(Roles.Docente, jwt.Claims.First(c => c.Type == "role").Value);
        Assert.Equal("docente@uta.edu.ec", jwt.Claims.First(c => c.Type == "email").Value);
    }

    [Fact]
    public async Task Login_ConContrasenaIncorrecta_DevuelveNullYRegistraAuditoria()
    {
        using var db = CrearDb();
        var r = await CrearServicio(db).LoginAsync(new LoginRequest("docente@uta.edu.ec", "mala"), null);

        Assert.Null(r);
        Assert.Contains(db.Auditoria, a => a.Accion == "LOGIN_FALLIDO");
    }

    [Fact]
    public async Task Login_UsuarioInexistente_DevuelveNull()
    {
        using var db = CrearDb();
        var r = await CrearServicio(db).LoginAsync(new LoginRequest("nadie@uta.edu.ec", "Correcta#1"), null);
        Assert.Null(r);
    }

    [Fact]
    public async Task Login_EsInsensibleAMayusculasEnElCorreo()
    {
        using var db = CrearDb();
        var r = await CrearServicio(db).LoginAsync(new LoginRequest("  DOCENTE@UTA.EDU.EC ", "Correcta#1"), null);
        Assert.NotNull(r);
    }

    [Fact]
    public void Token_TieneExpiracionEmisorYAudienciaConfigurados()
    {
        var svc = new TokenService(Options.Create(Opciones));
        var (token, expira) = svc.Generar(new Usuario { Id = 1, Nombre = "X", Email = "x@uta.edu.ec" }, Roles.AdministradorTecnico);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("test-iss", jwt.Issuer);
        Assert.Contains("test-aud", jwt.Audiences);
        Assert.InRange((expira - DateTime.UtcNow).TotalMinutes, 59, 61);
        Assert.Equal(Roles.AdministradorTecnico, jwt.Claims.First(c => c.Type == "role").Value);
    }

    [Theory]
    [InlineData("", "x", false)]
    [InlineData("no-es-correo", "x", false)]
    [InlineData("docente@uta.edu.ec", "", false)]
    [InlineData("docente@uta.edu.ec", "Clave#1", true)]
    public void Validador_DeLogin(string email, string pwd, bool esValido)
    {
        var res = new LoginRequestValidator().Validate(new LoginRequest(email, pwd));
        Assert.Equal(esValido, res.IsValid);
    }
}
