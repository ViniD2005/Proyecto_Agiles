namespace EduMonitor.Academic.Api.Contracts;

// ---------- HU-08: estructura temática ----------
public record ContenidoDto(int Id, string Nombre, int Orden, int ActividadesAsignadas);
public record TemaDto(int Id, string Nombre, int Orden, IReadOnlyList<ContenidoDto> Contenidos);
public record UnidadDto(int Id, string Nombre, int Orden, IReadOnlyList<TemaDto> Temas);
public record EstructuraDto(int MateriaId, IReadOnlyList<UnidadDto> Unidades, IReadOnlyList<string> Advertencias, bool Completa);

public record CrearElementoRequest(string? Nivel, int? PadreId, string? Nombre);
public record RenombrarRequest(string? Nombre);
public record ElementoDto(string Nivel, int Id, string Nombre, int Orden);

public record ImportarTema(string? Nombre, List<string>? Contenidos);
public record ImportarUnidad(string? Nombre, List<ImportarTema>? Temas);
public record ImportarEstructuraRequest(List<ImportarUnidad>? Unidades);

// ---------- HU-09: mapeo actividad ↔ contenidos ----------
public record RelacionContenidoDto(int ContenidoId, string Contenido, int TemaId, string Tema, int UnidadId, string Unidad);
public record ActividadMapeoDto(int ActividadId, string Nombre, string Tipo, decimal Peso, bool Pendiente, IReadOnlyList<RelacionContenidoDto> Contenidos);
public record MapeoDto(int MateriaId, int Pendientes, IReadOnlyList<ActividadMapeoDto> Actividades);
public record AsignarContenidosRequest(List<int>? ContenidoIds);
public record AsignacionDto(ActividadMapeoDto Actividad, bool EventoPublicado);

// ---------- HU-07: calificaciones sincronizadas ----------
public record NotaCeldaDto(decimal? Nota, string Estado);
public record ActividadColumnaDto(int Id, string Nombre, string Tipo, decimal Peso);
public record EstudianteNotasDto(int EstudianteId, string Nombre, IReadOnlyDictionary<int, NotaCeldaDto> Notas);
public record CalificacionesDto(int MateriaId, DateTime? UltimaSincronizacion, IReadOnlyList<ActividadColumnaDto> Actividades, PagedResult<EstudianteNotasDto> Estudiantes);
