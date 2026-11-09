namespace EduMonitor.Analytics.Api.Contracts;

public record UmbralesDto(decimal Critico, decimal Aprobado, bool Personalizado);
public record DistribucionDto(string Rango, int Cantidad);

// ---------- HU-10 · indicadores ----------
public record IndicadorCursoDto(
    decimal? Promedio, decimal? Desviacion, int Estudiantes, int Criticos, int EnUmbral, int Aprobados, int SinDatos,
    decimal PesoEvaluadoPromedio, IReadOnlyList<DistribucionDto> Distribucion);

public record IndicadorActividadDto(
    int ActividadId, string Nombre, string Tipo, decimal Peso, decimal? Promedio, decimal? Desviacion,
    int Calificadas, int Ceros, int SinEntrega, int Pendientes, IReadOnlyList<DistribucionDto> Distribucion);

public record IndicadorEstudianteDto(
    int EstudianteId, string Nombre, decimal? NotaPonderada, decimal NotaAcumulada, decimal PesoEvaluado,
    string Nivel, int Alertas, string? AlertaPrincipal);

public record IndicadoresDto(
    int MateriaId, string Materia, DateTime? ActualizadoUtc, string Escala, UmbralesDto Umbrales,
    IndicadorCursoDto Curso, IReadOnlyList<IndicadorActividadDto> Actividades, IReadOnlyList<IndicadorEstudianteDto> Estudiantes);

// ---------- HU-11 · riesgo y alertas ----------
public record AlertaDto(string Tipo, string Regla, string Evidencia, string? Actividad, decimal? Valor);
public record EstudianteRiesgoDto(int EstudianteId, string Nombre, decimal? NotaPonderada, string Nivel, IReadOnlyList<AlertaDto> Alertas);
public record RiesgoDto(int MateriaId, UmbralesDto Umbrales, string Aviso, IReadOnlyList<EstudianteRiesgoDto> Estudiantes);
public record ActualizarUmbralesRequest(int MateriaId, decimal Critico, decimal Aprobado);

// ---------- HU-12 · desempeño por tema y ficha ----------
public record CeldaDto(decimal? Nota, string Nivel);
public record ContenidoColumnaDto(int ContenidoId, string Contenido);
public record TemaColumnaDto(int TemaId, string Tema, int UnidadId, string Unidad, IReadOnlyList<ContenidoColumnaDto> Contenidos);
public record FilaMapaDto(int EstudianteId, string Nombre, IReadOnlyDictionary<int, CeldaDto> Temas, IReadOnlyDictionary<int, CeldaDto> Contenidos);
public record MapaCalorDto(
    int MateriaId, UmbralesDto Umbrales, IReadOnlyList<TemaColumnaDto> Temas, IReadOnlyList<FilaMapaDto> Estudiantes,
    IReadOnlyDictionary<int, CeldaDto> PromedioCursoTemas, IReadOnlyDictionary<int, CeldaDto> PromedioCursoContenidos,
    int ActividadesSinMapear);

public record RelacionFichaDto(string Unidad, string Tema, string Contenido);
public record ActividadFichaDto(int ActividadId, string Nombre, string Tipo, decimal Peso, decimal? Nota, string Estado, IReadOnlyList<RelacionFichaDto> Contenidos);
public record EvolucionDto(string Actividad, decimal Nota);
public record TemaFichaDto(int TemaId, string Tema, string Unidad, decimal? Nota, string Nivel);
public record FichaEstudianteDto(
    int MateriaId, string Materia, int EstudianteId, string Nombre, decimal? NotaPonderada, decimal NotaAcumulada,
    decimal PesoEvaluado, string Nivel, UmbralesDto Umbrales, IReadOnlyList<ActividadFichaDto> Actividades,
    IReadOnlyList<EvolucionDto> Evolucion, IReadOnlyList<TemaFichaDto> Temas, IReadOnlyList<AlertaDto> Alertas, string Aviso);
