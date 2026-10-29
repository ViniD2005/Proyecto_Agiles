namespace EduMonitor.Identity.Api.Domain;

public class Rol
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}

public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Correo institucional. Debe coincidir con el correo del docente en Moodle (así MS-B encuentra sus cursos).</summary>
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int RolId { get; set; }
    public Rol? Rol { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoAcceso { get; set; }
}

/// <summary>Bitácora de auditoría (RF-12). Nunca se guardan contraseñas ni tokens.</summary>
public class RegistroAuditoria
{
    public long Id { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public string? UsuarioEmail { get; set; }
    public string Accion { get; set; } = string.Empty;   // LOGIN_OK, LOGIN_FALLIDO, SINCRONIZACION, ...
    public string Servicio { get; set; } = "MS-A";
    public string? Detalle { get; set; }
    public string? Ip { get; set; }
}
