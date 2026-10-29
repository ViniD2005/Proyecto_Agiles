namespace EduMonitor.Identity.Api.Contracts;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, DateTime ExpiraEn, UsuarioDto Usuario);

public record UsuarioDto(int Id, string Nombre, string Email, string Rol);

public record AuditoriaDto(long Id, DateTime Fecha, string? UsuarioEmail, string Accion, string Servicio, string? Detalle);

public record RegistrarAuditoriaRequest(string Accion, string Servicio, string? Detalle);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
