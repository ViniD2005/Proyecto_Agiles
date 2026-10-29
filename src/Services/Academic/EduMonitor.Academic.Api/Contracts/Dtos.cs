namespace EduMonitor.Academic.Api.Contracts;

public record MateriaDto(
    int Id, int MoodleCourseId, string Nombre, string NombreCorto,
    int TotalEstudiantes, int TotalActividades,
    DateTime? UltimaSincronizacion, string? OrigenSincronizacion);

public record EstudianteDto(int Id, int MoodleUserId, string Nombres, string Apellidos, string? Email, string Estado);

public record ActividadDto(int Id, int MoodleCmId, string Nombre, string Modulo, string Tipo, decimal Peso, string? Seccion);

public record SincronizacionDto(long Id, DateTime Fecha, string Tipo, string Origen, bool Exitosa, string? Mensaje);

public record ResultadoSincronizacion(bool Exitosa, string Mensaje, DateTime Fecha, string Origen, int Elementos);

public record EstadoMoodleDto(bool Conectado, string Origen, string? Sitio, string? Version, string? Error);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
