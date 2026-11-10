namespace EduMonitor.Common.Messaging;

/// <summary>Nombres de los eventos asíncronos (routing keys) definidos en la arquitectura (sección 4.2).</summary>
public static class EventNames
{
    public const string CalificacionesSincronizadas = "CalificacionesSincronizadas";
    public const string MapeoActividadActualizado = "MapeoActividadActualizado";
}

/// <summary>Estados de una calificación (RN-08: se distingue nota cero, sin entrega y pendiente de calificar).</summary>
public static class EstadosCalificacion
{
    public const string Calificada = "Calificada";
    public const string Cero = "Cero";
    public const string SinEntrega = "SinEntrega";
    public const string PendienteDeCalificar = "PendienteDeCalificar";
}

// ---- Fragmentos comunes de los eventos (solo los datos mínimos necesarios, RNF-02) ----
public record MateriaSnapshot(int Id, string Nombre, string DocenteEmail);
public record EstudianteSnapshot(int Id, string Nombres, string Apellidos);
public record ActividadSnapshot(int Id, string Nombre, string Tipo, decimal Peso, int Orden);
public record CalificacionSnapshot(int ActividadId, int EstudianteId, decimal? Nota, string Estado);

public record UnidadSnapshot(int Id, string Nombre, int Orden);
public record TemaSnapshot(int Id, int UnidadId, string Nombre, int Orden);
public record ContenidoSnapshot(int Id, int TemaId, string Nombre, int Orden);
public record MapeoSnapshot(int ActividadId, int ContenidoId);

/// <summary>
/// MS-B → MS-C. Foto completa de estudiantes, actividades y calificaciones de una materia.
/// El consumidor reemplaza su copia local (operación idempotente).
/// </summary>
public record CalificacionesSincronizadas(
    Guid EventId,
    DateTime OcurrioEnUtc,
    MateriaSnapshot Materia,
    IReadOnlyList<EstudianteSnapshot> Estudiantes,
    IReadOnlyList<ActividadSnapshot> Actividades,
    IReadOnlyList<CalificacionSnapshot> Calificaciones);

/// <summary>
/// MS-B → MS-C. Foto de la estructura temática (unidades → temas → contenidos) y del mapeo actividad ↔ contenido.
/// Se publica al guardar un mapeo y al modificar la estructura.
/// </summary>
public record MapeoActividadActualizado(
    Guid EventId,
    DateTime OcurrioEnUtc,
    MateriaSnapshot Materia,
    IReadOnlyList<UnidadSnapshot> Unidades,
    IReadOnlyList<TemaSnapshot> Temas,
    IReadOnlyList<ContenidoSnapshot> Contenidos,
    IReadOnlyList<MapeoSnapshot> Mapeo);
