namespace EduMonitor.Analytics.Api.Domain;

// DB_Analitica es un modelo de lectura: MS-C guarda su propia copia de lo que MS-B publica por RabbitMQ.
// Los Id de MateriaId / EstudianteId / ActividadId / ... son los de MS-B (no se generan aquí).

public class MateriaAnalitica
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Dueño de la materia: se usa para el control de acceso por curso (RN-07).</summary>
    public string DocenteEmail { get; set; } = string.Empty;
    public DateTime? UltimasCalificacionesUtc { get; set; }
    public DateTime? UltimoMapeoUtc { get; set; }
}

public class EstudianteAnalitico
{
    public int MateriaId { get; set; }
    public int EstudianteId { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
}

public class ActividadAnalitica
{
    public int MateriaId { get; set; }
    public int ActividadId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public decimal Peso { get; set; }
    public int Orden { get; set; }
}

public class CalificacionAnalitica
{
    public int MateriaId { get; set; }
    public int ActividadId { get; set; }
    public int EstudianteId { get; set; }
    public decimal? Nota { get; set; }
    public string Estado { get; set; } = string.Empty;
}

public class UnidadAnalitica
{
    public int MateriaId { get; set; }
    public int UnidadId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
}

public class TemaAnalitico
{
    public int MateriaId { get; set; }
    public int TemaId { get; set; }
    public int UnidadId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
}

public class ContenidoAnalitico
{
    public int MateriaId { get; set; }
    public int ContenidoId { get; set; }
    public int TemaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
}

public class MapeoAnalitico
{
    public int MateriaId { get; set; }
    public int ActividadId { get; set; }
    public int ContenidoId { get; set; }
}

/// <summary>Umbrales de riesgo configurables por materia (RN-04). Sin fila = valores por defecto 5 y 7.</summary>
public class UmbralMateria
{
    public int MateriaId { get; set; }
    public decimal Critico { get; set; }
    public decimal Aprobado { get; set; }
    public DateTime ActualizadoUtc { get; set; }
}

/// <summary>Indicador calculado por el código (RN-09) cada vez que llegan notas nuevas o cambian los umbrales.</summary>
public class IndicadorEstudiante
{
    public int MateriaId { get; set; }
    public int EstudianteId { get; set; }
    public decimal? NotaPonderada { get; set; }
    public decimal NotaAcumulada { get; set; }
    public decimal PesoEvaluado { get; set; }
    public string Nivel { get; set; } = string.Empty;
    public DateTime CalculadoUtc { get; set; }
}

public class AlertaRiesgo
{
    public long Id { get; set; }
    public int MateriaId { get; set; }
    public int EstudianteId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Regla { get; set; } = string.Empty;
    public string Evidencia { get; set; } = string.Empty;
    public string? Actividad { get; set; }
    public decimal? Valor { get; set; }
    public DateTime CreadaUtc { get; set; }
}
