namespace EduMonitor.Analytics.Api.Contracts;

// ---------- HU-19 · tendencia (opcional) ----------
public record TendenciaDto(
    bool Disponible, string Mensaje, int Puntos, decimal? Pendiente, decimal? R2, string Direccion,
    decimal? Proyeccion, bool AlertaTemprana, string? MensajeAlerta, IReadOnlyList<decimal> Linea, string Aviso);

// ---------- HU-13 / HU-14 · agente ----------
public record ConsultaAgenteRequest(int MateriaId, string? Pregunta);

public record InvocacionDto(string Herramienta, IReadOnlyDictionary<string, string> Parametros, bool Exito, string Resumen, int DuracionMs);

public record AgenteRespuestaDto(
    long ConsultaId, int MateriaId, string Materia, string Intencion,
    IReadOnlyList<InvocacionDto> Herramientas,
    IReadOnlyList<string> Hechos, IReadOnlyList<string> Inferencias, IReadOnlyList<string> Recomendaciones,
    IReadOnlyList<string> Incertidumbre, string? Modelo, bool ModeloDisponible,
    IReadOnlyList<string> Advertencias, DateTime FechaUtc);

public record EstadoAgenteDto(string Proveedor, string Modelo, bool Disponible, string? Detalle, IReadOnlyList<string> Herramientas);

// ---------- HU-15 · recomendaciones ----------
public record ContenidoRecomendacionDto(
    IReadOnlyList<string> Falencias, IReadOnlyList<string> ActividadesRefuerzo,
    IReadOnlyList<string> ContenidosARepasar, string? Observacion);

public record RecomendacionDto(
    long Id, int MateriaId, int EstudianteId, string Estudiante, string Nivel, decimal? NotaPonderada,
    string Evidencia, ContenidoRecomendacionDto Contenido, string Fuente, string Estado,
    DateTime GeneradaUtc, DateTime? EditadaUtc, string? EditadaPor, DateTime? AprobadaUtc, string? AprobadaPor,
    DateTime? EnviadaUtc, string? EnviadaA, string Aviso);

public record RecomendacionResumenDto(int EstudianteId, string Estudiante, string Nivel, decimal? NotaPonderada, string? Estado, string? Fuente, DateTime? GeneradaUtc);

public record RecomendacionesCursoDto(int MateriaId, string Materia, string Aviso, IReadOnlyList<RecomendacionResumenDto> Estudiantes);

public record EditarRecomendacionRequest(List<string>? Falencias, List<string>? ActividadesRefuerzo, List<string>? ContenidosARepasar, string? Observacion);

public record EnvioRecomendacionDto(long RecomendacionId, string EnviadaA, DateTime EnviadaUtc);

// ---------- HU-16 · reporte ----------
public record TemaReporteDto(string Tema, string Unidad, decimal? Promedio, string Nivel, int EstudiantesCriticos);

public record ReporteDto(
    long ReporteId, int MateriaId, string Materia, string Periodo, string Fuente,
    DateTime GeneradoUtc, DateTime? DatosActualizadosUtc, string GeneradoPor,
    IndicadorCursoDto Curso, UmbralesDto Umbrales, IReadOnlyList<TemaReporteDto> Temas,
    IReadOnlyList<string> Hallazgos, IReadOnlyList<string> Recomendaciones, string? AnalisisIa, string Aviso);

// ---------- HU-21 · sílabo (opcional) ----------
public record SilaboTemaDto(string Nombre, List<string> Contenidos);
public record SilaboUnidadDto(string Nombre, List<SilaboTemaDto> Temas);
public record PropuestaSilaboDto(string Fuente, IReadOnlyList<SilaboUnidadDto> Unidades, IReadOnlyList<string> Advertencias, string Aviso);
