namespace EduMonitor.Academic.Api.Domain;

/// <summary>Tipo de actividad según la ponderación de la UTA (RN-02).</summary>
public enum TipoActividad
{
    Evaluacion = 1,       // 40 %  (ACD - aprendizaje en contacto con el docente)
    APE = 2,              // 30 %  (aprendizaje práctico-experimental)
    TrabajoAutonomo = 3,  // 30 %  (AA - aprendizaje autónomo)
    Otra = 9              // no pondera (foros, recursos, asistencia...)
}

/// <summary>Copia local de un curso de Moodle (solo lectura, RN-06).</summary>
public class Materia
{
    public int Id { get; set; }
    public int MoodleCourseId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string NombreCorto { get; set; } = string.Empty;
    /// <summary>Correo del docente dueño del curso. Se usa para la autorización por curso (RN-07).</summary>
    public string DocenteEmail { get; set; } = string.Empty;
    public int DocenteMoodleUserId { get; set; }
    public int TotalEstudiantes { get; set; }
    public int TotalActividades { get; set; }
    public DateTime? UltimaSincronizacion { get; set; }
    public string? OrigenSincronizacion { get; set; }

    public ICollection<Matricula> Matriculas { get; set; } = new List<Matricula>();
    public ICollection<Actividad> Actividades { get; set; } = new List<Actividad>();
}

public class Estudiante
{
    public int Id { get; set; }
    public int MoodleUserId { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string? Email { get; set; }
    public ICollection<Matricula> Matriculas { get; set; } = new List<Matricula>();
}

public class Matricula
{
    public int MateriaId { get; set; }
    public Materia? Materia { get; set; }
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }
    public bool Activa { get; set; } = true;
}

public class Actividad
{
    public int Id { get; set; }
    public int MateriaId { get; set; }
    public Materia? Materia { get; set; }
    /// <summary>Id del módulo de curso (cmid) en Moodle.</summary>
    public int MoodleCmId { get; set; }
    public int MoodleInstanceId { get; set; }
    public string Modulo { get; set; } = string.Empty; // assign, quiz
    public string Nombre { get; set; } = string.Empty;
    public string? Seccion { get; set; }
    public TipoActividad Tipo { get; set; }
    /// <summary>Peso dentro del parcial en % (ej. 40, 15, 7.5).</summary>
    public decimal Peso { get; set; }
}

/// <summary>Bitácora de sincronización: fecha, origen, resultado y errores (HU-05 / HU-07).</summary>
public class SincronizacionLog
{
    public long Id { get; set; }
    public int? MateriaId { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public string Tipo { get; set; } = string.Empty;       // MATERIAS, DETALLE_MATERIA
    public string Origen { get; set; } = string.Empty;     // URL de Moodle
    public bool Exitosa { get; set; }
    public string? Mensaje { get; set; }
    public string? UsuarioEmail { get; set; }
}
