using EduMonitor.Identity.Api.Contracts;
using EduMonitor.Identity.Api.Data;
using EduMonitor.Identity.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Identity.Api.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default);
}

public class AuthService(IdentityDbContext db, ITokenService tokens) : IAuthService
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var usuario = await db.Usuarios.Include(u => u.Rol)
                                       .FirstOrDefaultAsync(u => u.Email == email, ct);

        // Mismo mensaje para usuario inexistente o contraseña incorrecta (no revelar cuál falló)
        if (usuario is null || !usuario.Activo || !BCrypt.Net.BCrypt.Verify(request.Password, usuario.PasswordHash))
        {
            db.Auditoria.Add(new RegistroAuditoria { UsuarioEmail = email, Accion = "LOGIN_FALLIDO", Ip = ip });
            await db.SaveChangesAsync(ct);
            return null;
        }

        var rol = usuario.Rol!.Nombre;
        var (token, expira) = tokens.Generar(usuario, rol);

        usuario.UltimoAcceso = DateTime.UtcNow;
        db.Auditoria.Add(new RegistroAuditoria { UsuarioEmail = email, Accion = "LOGIN_OK", Ip = ip, Detalle = $"Rol: {rol}" });
        await db.SaveChangesAsync(ct);

        return new LoginResponse(token, expira, new UsuarioDto(usuario.Id, usuario.Nombre, usuario.Email, rol));
    }
}
