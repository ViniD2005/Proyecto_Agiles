namespace EduMonitor.Analytics.Api.Domain;

/// <summary>Una consulta del docente al agente (HU-14). La pregunta se guarda ya anonimizada (RN-11).</summary>
public class ConsultaAgente
{
    public long Id { get; set; }
    public DateTime FechaUtc { get; set; }
    public string DocenteEmail { get; set; } = string.Empty;
    public int MateriaId { get; set; }
    public string Pregunta { get; set; } = string.Empty;
    public string Intencion { get; set; } = string.Empty;
    public string? Modelo { get; set; }
    public bool ModeloDisponible { get; set; }
    public int DuracionMs { get; set; }
    /// <summary>Respondida · SinDatos (no se pudo obtener información) · Denegada.</summary>
    public string Resultado { get; set; } = string.Empty;
}

/// <summary>Cada herramienta ejecutada por el agente: nombre, parámetros validados y resultado (HU-13, criterio 2).</summary>
public class InvocacionHerramienta
{
    public long Id { get; set; }
    public long ConsultaId { get; set; }
    public DateTime FechaUtc { get; set; }
    public string Herramienta { get; set; } = string.Empty;
    public string Parametros { get; set; } = string.Empty;
    public bool Exito { get; set; }
    public string Resumen { get; set; } = string.Empty;
    public int DuracionMs { get; set; }
}

public static class EstadosRecomendacion
{
    public const string Borrador = "Borrador";
    public const string Aprobada = "Aprobada";
}

public static class FuentesRecomendacion
{
    public const string Modelo = "Modelo";
    public const string Plantilla = "Plantilla";
}

/// <summary>Recomendación pedagógica por estudiante (HU-15): con su evidencia, editable y aprobada por el docente.</summary>
public class RecomendacionEstudiante
{
    public long Id { get; set; }
    public int MateriaId { get; set; }
    public int EstudianteId { get; set; }
    public string Nivel { get; set; } = string.Empty;
    public decimal? NotaPonderada { get; set; }
    /// <summary>Texto de evidencia (notas y temas) armado por el código, no por el modelo.</summary>
    public string Evidencia { get; set; } = string.Empty;
    /// <summary>JSON: falencias, actividades de refuerzo, contenidos a repasar y observación.</summary>
    public string Contenido { get; set; } = string.Empty;
    public string Fuente { get; set; } = FuentesRecomendacion.Plantilla;
    public string Estado { get; set; } = EstadosRecomendacion.Borrador;
    public DateTime GeneradaUtc { get; set; }
    public string GeneradaPor { get; set; } = string.Empty;
    public DateTime? EditadaUtc { get; set; }
    public string? EditadaPor { get; set; }
    public DateTime? AprobadaUtc { get; set; }
    public string? AprobadaPor { get; set; }
    /// <summary>HU-20: último envío por correo (solo por acción manual del docente).</summary>
    public DateTime? EnviadaUtc { get; set; }
    public string? EnviadaA { get; set; }
}

/// <summary>Reporte inteligente del curso (HU-16). El contenido queda fijo: la vista previa y el PDF muestran lo mismo.</summary>
public class ReporteCurso
{
    public long Id { get; set; }
    public int MateriaId { get; set; }
    public DateTime GeneradoUtc { get; set; }
    public string GeneradoPor { get; set; } = string.Empty;
    public string Contenido { get; set; } = string.Empty;
}
