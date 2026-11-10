namespace EduMonitor.Academic.Api.Domain;

/// <summary>Estado de una calificación (RN-08).</summary>
public enum EstadoCalificacion
{
    Calificada = 1,
    Cero = 2,
    SinEntrega = 3,
    PendienteDeCalificar = 4
}

/// <summary>Nota de un estudiante en una actividad, copiada de Moodle (solo lectura, RN-06). Escala 0-10 (RN-01).</summary>
public class Calificacion
{
    public long Id { get; set; }
    public int ActividadId { get; set; }
    public Actividad? Actividad { get; set; }
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }
    /// <summary>Nula cuando la actividad no tiene nota (sin entrega o pendiente de calificar).</summary>
    public decimal? Nota { get; set; }
    public EstadoCalificacion Estado { get; set; }
    public DateTime FechaSincronizacion { get; set; } = DateTime.UtcNow;
}

// ---- Estructura temática del sílabo (RN-05): unidad → tema → contenido mínimo ----
public class Unidad
{
    public int Id { get; set; }
    public int MateriaId { get; set; }
    public Materia? Materia { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
    public List<Tema> Temas { get; set; } = new();
}

public class Tema
{
    public int Id { get; set; }
    public int UnidadId { get; set; }
    public Unidad? Unidad { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
    public List<ContenidoMinimo> Contenidos { get; set; } = new();
}

public class ContenidoMinimo
{
    public int Id { get; set; }
    public int TemaId { get; set; }
    public Tema? Tema { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
}

/// <summary>Relación N:M entre una actividad y los contenidos mínimos que evalúa (HU-09).</summary>
public class ActividadContenido
{
    public int ActividadId { get; set; }
    public Actividad? Actividad { get; set; }
    public int ContenidoId { get; set; }
    public ContenidoMinimo? Contenido { get; set; }
}
