using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Domain;
using EduMonitor.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Services;

/// <summary>HU-08: unidades → temas → contenidos mínimos de una materia (RN-05).</summary>
public class EstructuraService(AcademicDbContext db, MapeoEventDispatcher dispatcher)
{
    public const int MaxUnidades = 10;
    public const int MaxNombre = 200;

    public static bool TryParseNivel(string? texto, out string nivel)
    {
        nivel = (texto ?? string.Empty).Trim().ToLowerInvariant();
        return nivel is "unidad" or "tema" or "contenido";
    }

    // ------------------------------------------------------------------ lectura
    public async Task<EstructuraDto> ObtenerAsync(int materiaId, CancellationToken ct = default)
    {
        var unidades = await db.Unidades.AsNoTracking()
            .Where(u => u.MateriaId == materiaId)
            .Include(u => u.Temas).ThenInclude(t => t.Contenidos)
            .ToListAsync(ct);

        var contenidoIds = unidades.SelectMany(u => u.Temas).SelectMany(t => t.Contenidos).Select(c => c.Id).ToList();
        var conteos = await db.Mapeos.AsNoTracking()
            .Where(m => contenidoIds.Contains(m.ContenidoId))
            .GroupBy(m => m.ContenidoId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N, ct);

        var advertencias = new List<string>();
        if (unidades.Count == 0) advertencias.Add("La materia aún no tiene unidades registradas.");

        var dto = unidades.OrderBy(u => u.Orden).Select(u =>
        {
            if (u.Temas.Count == 0) advertencias.Add($"La unidad «{u.Nombre}» no tiene temas.");
            return new UnidadDto(u.Id, u.Nombre, u.Orden, u.Temas.OrderBy(t => t.Orden).Select(t =>
            {
                if (t.Contenidos.Count == 0) advertencias.Add($"El tema «{t.Nombre}» no tiene contenidos mínimos.");
                return new TemaDto(t.Id, t.Nombre, t.Orden, t.Contenidos.OrderBy(c => c.Orden)
                    .Select(c => new ContenidoDto(c.Id, c.Nombre, c.Orden, conteos.GetValueOrDefault(c.Id))).ToList());
            }).ToList());
        }).ToList();

        return new EstructuraDto(materiaId, dto, advertencias, dto.Count > 0 && advertencias.Count == 0);
    }

    // ------------------------------------------------------------------ crear
    public async Task<Resultado<ElementoDto>> CrearAsync(int materiaId, string? nivelTexto, int? padreId, string? nombreTexto, CancellationToken ct = default)
    {
        if (!TryParseNivel(nivelTexto, out var nivel))
            return Resultado<ElementoDto>.Falla(CodigoResultado.Invalido, "El nivel debe ser «unidad», «tema» o «contenido».");
        var nombre = (nombreTexto ?? string.Empty).Trim();
        var error = ValidarNombre(nombre);
        if (error is not null) return Resultado<ElementoDto>.Falla(CodigoResultado.Invalido, error);

        ElementoDto creado;
        switch (nivel)
        {
            case "unidad":
            {
                var total = await db.Unidades.CountAsync(u => u.MateriaId == materiaId, ct);
                if (total >= MaxUnidades)
                    return Resultado<ElementoDto>.Falla(CodigoResultado.Invalido, $"Una materia admite hasta {MaxUnidades} unidades.");
                if (await db.Unidades.AnyAsync(u => u.MateriaId == materiaId && u.Nombre.ToLower() == nombre.ToLower(), ct))
                    return Resultado<ElementoDto>.Falla(CodigoResultado.Duplicado, $"Ya existe una unidad llamada «{nombre}».");
                var orden = (await db.Unidades.Where(u => u.MateriaId == materiaId).MaxAsync(u => (int?)u.Orden, ct) ?? 0) + 1;
                var unidad = new Unidad { MateriaId = materiaId, Nombre = nombre, Orden = orden };
                db.Unidades.Add(unidad);
                await db.SaveChangesAsync(ct);
                creado = new ElementoDto("unidad", unidad.Id, unidad.Nombre, unidad.Orden);
                break;
            }
            case "tema":
            {
                var unidad = padreId is null ? null
                    : await db.Unidades.FirstOrDefaultAsync(u => u.Id == padreId && u.MateriaId == materiaId, ct);
                if (unidad is null)
                    return Resultado<ElementoDto>.Falla(CodigoResultado.NoEncontrado, "La unidad indicada no existe en esta materia.");
                if (await db.Temas.AnyAsync(t => t.UnidadId == unidad.Id && t.Nombre.ToLower() == nombre.ToLower(), ct))
                    return Resultado<ElementoDto>.Falla(CodigoResultado.Duplicado, $"La unidad ya tiene un tema llamado «{nombre}».");
                var orden = (await db.Temas.Where(t => t.UnidadId == unidad.Id).MaxAsync(t => (int?)t.Orden, ct) ?? 0) + 1;
                var tema = new Tema { UnidadId = unidad.Id, Nombre = nombre, Orden = orden };
                db.Temas.Add(tema);
                await db.SaveChangesAsync(ct);
                creado = new ElementoDto("tema", tema.Id, tema.Nombre, tema.Orden);
                break;
            }
            default:
            {
                var tema = padreId is null ? null
                    : await db.Temas.FirstOrDefaultAsync(t => t.Id == padreId && t.Unidad!.MateriaId == materiaId, ct);
                if (tema is null)
                    return Resultado<ElementoDto>.Falla(CodigoResultado.NoEncontrado, "El tema indicado no existe en esta materia.");
                if (await db.Contenidos.AnyAsync(c => c.TemaId == tema.Id && c.Nombre.ToLower() == nombre.ToLower(), ct))
                    return Resultado<ElementoDto>.Falla(CodigoResultado.Duplicado, $"El tema ya tiene un contenido llamado «{nombre}».");
                var orden = (await db.Contenidos.Where(c => c.TemaId == tema.Id).MaxAsync(c => (int?)c.Orden, ct) ?? 0) + 1;
                var contenido = new ContenidoMinimo { TemaId = tema.Id, Nombre = nombre, Orden = orden };
                db.Contenidos.Add(contenido);
                await db.SaveChangesAsync(ct);
                creado = new ElementoDto("contenido", contenido.Id, contenido.Nombre, contenido.Orden);
                break;
            }
        }

        await dispatcher.PublicarAsync(materiaId, ct);
        return Resultado<ElementoDto>.Ok(creado);
    }

    // ------------------------------------------------------------------ renombrar
    public async Task<Resultado<ElementoDto>> RenombrarAsync(int materiaId, string? nivelTexto, int itemId, string? nombreTexto, CancellationToken ct = default)
    {
        if (!TryParseNivel(nivelTexto, out var nivel))
            return Resultado<ElementoDto>.Falla(CodigoResultado.Invalido, "El nivel debe ser «unidad», «tema» o «contenido».");
        var nombre = (nombreTexto ?? string.Empty).Trim();
        var error = ValidarNombre(nombre);
        if (error is not null) return Resultado<ElementoDto>.Falla(CodigoResultado.Invalido, error);

        ElementoDto editado;
        switch (nivel)
        {
            case "unidad":
            {
                var u = await db.Unidades.FirstOrDefaultAsync(x => x.Id == itemId && x.MateriaId == materiaId, ct);
                if (u is null) return NoExiste(nivel);
                if (await db.Unidades.AnyAsync(x => x.MateriaId == materiaId && x.Id != u.Id && x.Nombre.ToLower() == nombre.ToLower(), ct))
                    return Resultado<ElementoDto>.Falla(CodigoResultado.Duplicado, $"Ya existe una unidad llamada «{nombre}».");
                u.Nombre = nombre;
                await db.SaveChangesAsync(ct);
                editado = new ElementoDto(nivel, u.Id, u.Nombre, u.Orden);
                break;
            }
            case "tema":
            {
                var t = await db.Temas.FirstOrDefaultAsync(x => x.Id == itemId && x.Unidad!.MateriaId == materiaId, ct);
                if (t is null) return NoExiste(nivel);
                if (await db.Temas.AnyAsync(x => x.UnidadId == t.UnidadId && x.Id != t.Id && x.Nombre.ToLower() == nombre.ToLower(), ct))
                    return Resultado<ElementoDto>.Falla(CodigoResultado.Duplicado, $"La unidad ya tiene un tema llamado «{nombre}».");
                t.Nombre = nombre;
                await db.SaveChangesAsync(ct);
                editado = new ElementoDto(nivel, t.Id, t.Nombre, t.Orden);
                break;
            }
            default:
            {
                var c = await db.Contenidos.FirstOrDefaultAsync(x => x.Id == itemId && x.Tema!.Unidad!.MateriaId == materiaId, ct);
                if (c is null) return NoExiste(nivel);
                if (await db.Contenidos.AnyAsync(x => x.TemaId == c.TemaId && x.Id != c.Id && x.Nombre.ToLower() == nombre.ToLower(), ct))
                    return Resultado<ElementoDto>.Falla(CodigoResultado.Duplicado, $"El tema ya tiene un contenido llamado «{nombre}».");
                c.Nombre = nombre;
                await db.SaveChangesAsync(ct);
                editado = new ElementoDto(nivel, c.Id, c.Nombre, c.Orden);
                break;
            }
        }

        await dispatcher.PublicarAsync(materiaId, ct);
        return Resultado<ElementoDto>.Ok(editado);
    }

    // ------------------------------------------------------------------ eliminar
    /// <summary>
    /// HU-08, criterio 3: si el contenido está asignado a alguna actividad se pide confirmación
    /// (Conflicto con el número de actividades afectadas) y solo se elimina con confirmar = true.
    /// </summary>
    public async Task<Resultado<bool>> EliminarAsync(int materiaId, string? nivelTexto, int itemId, bool confirmar, CancellationToken ct = default)
    {
        if (!TryParseNivel(nivelTexto, out var nivel))
            return Resultado<bool>.Falla(CodigoResultado.Invalido, "El nivel debe ser «unidad», «tema» o «contenido».");

        List<int> contenidoIds;
        object entidad;
        switch (nivel)
        {
            case "unidad":
            {
                var u = await db.Unidades.Include(x => x.Temas).ThenInclude(t => t.Contenidos)
                    .FirstOrDefaultAsync(x => x.Id == itemId && x.MateriaId == materiaId, ct);
                if (u is null) return Resultado<bool>.Falla(CodigoResultado.NoEncontrado, "La unidad no existe en esta materia.");
                contenidoIds = u.Temas.SelectMany(t => t.Contenidos).Select(c => c.Id).ToList();
                entidad = u;
                break;
            }
            case "tema":
            {
                var t = await db.Temas.Include(x => x.Contenidos)
                    .FirstOrDefaultAsync(x => x.Id == itemId && x.Unidad!.MateriaId == materiaId, ct);
                if (t is null) return Resultado<bool>.Falla(CodigoResultado.NoEncontrado, "El tema no existe en esta materia.");
                contenidoIds = t.Contenidos.Select(c => c.Id).ToList();
                entidad = t;
                break;
            }
            default:
            {
                var c = await db.Contenidos.FirstOrDefaultAsync(x => x.Id == itemId && x.Tema!.Unidad!.MateriaId == materiaId, ct);
                if (c is null) return Resultado<bool>.Falla(CodigoResultado.NoEncontrado, "El contenido no existe en esta materia.");
                contenidoIds = new List<int> { c.Id };
                entidad = c;
                break;
            }
        }

        var mapeos = await db.Mapeos.Where(m => contenidoIds.Contains(m.ContenidoId)).ToListAsync(ct);
        var actividadesAfectadas = mapeos.Select(m => m.ActividadId).Distinct().Count();
        if (actividadesAfectadas > 0 && !confirmar)
            return Resultado<bool>.Falla(CodigoResultado.Conflicto,
                $"Este elemento está asignado a {actividadesAfectadas} actividad(es). Si lo elimina, esas actividades quedarán pendientes de mapeo.",
                actividadesAfectadas);

        db.Mapeos.RemoveRange(mapeos);
        db.Remove(entidad);
        await db.SaveChangesAsync(ct);
        await dispatcher.PublicarAsync(materiaId, ct);
        return Resultado<bool>.Ok(true);
    }

    // ------------------------------------------------------------------ importar (T8.4)
    /// <summary>Carga la estructura completa del sílabo. Solo se permite si la materia aún no tiene estructura.</summary>
    public async Task<Resultado<EstructuraDto>> ImportarAsync(int materiaId, ImportarEstructuraRequest? request, CancellationToken ct = default)
    {
        var unidadesReq = request?.Unidades;
        if (unidadesReq is null || unidadesReq.Count == 0)
            return Resultado<EstructuraDto>.Falla(CodigoResultado.Invalido, "Debe indicar al menos una unidad.");
        if (unidadesReq.Count > MaxUnidades)
            return Resultado<EstructuraDto>.Falla(CodigoResultado.Invalido, $"Una materia admite hasta {MaxUnidades} unidades.");
        if (await db.Unidades.AnyAsync(u => u.MateriaId == materiaId, ct))
            return Resultado<EstructuraDto>.Falla(CodigoResultado.Conflicto, "La materia ya tiene estructura temática. Elimínela antes de importar otra.");

        var errorValidacion = ValidarImportacion(unidadesReq);
        if (errorValidacion is not null) return Resultado<EstructuraDto>.Falla(CodigoResultado.Invalido, errorValidacion);

        var uOrden = 0;
        foreach (var ur in unidadesReq)
        {
            var unidad = new Unidad { MateriaId = materiaId, Nombre = ur.Nombre!.Trim(), Orden = ++uOrden };
            var tOrden = 0;
            foreach (var tr in ur.Temas!)
            {
                var tema = new Tema { Nombre = tr.Nombre!.Trim(), Orden = ++tOrden };
                var cOrden = 0;
                foreach (var cn in tr.Contenidos!)
                    tema.Contenidos.Add(new ContenidoMinimo { Nombre = cn.Trim(), Orden = ++cOrden });
                unidad.Temas.Add(tema);
            }
            db.Unidades.Add(unidad);
        }
        await db.SaveChangesAsync(ct);
        await dispatcher.PublicarAsync(materiaId, ct);
        return Resultado<EstructuraDto>.Ok(await ObtenerAsync(materiaId, ct));
    }

    // ------------------------------------------------------------------ validaciones
    private static string? ValidarNombre(string nombre)
    {
        if (nombre.Length == 0) return "El nombre es obligatorio.";
        if (nombre.Length > MaxNombre) return $"El nombre no puede superar {MaxNombre} caracteres.";
        return null;
    }

    private static string? ValidarImportacion(List<ImportarUnidad> unidades)
    {
        if (HayRepetidos(unidades.Select(u => u.Nombre))) return "Hay unidades con el mismo nombre.";
        foreach (var u in unidades)
        {
            var errU = ValidarNombre((u.Nombre ?? string.Empty).Trim());
            if (errU is not null) return $"Unidad: {errU}";
            if (u.Temas is null || u.Temas.Count == 0) return $"La unidad «{u.Nombre}» debe tener al menos un tema.";
            if (HayRepetidos(u.Temas.Select(t => t.Nombre))) return $"La unidad «{u.Nombre}» tiene temas con el mismo nombre.";
            foreach (var t in u.Temas)
            {
                var errT = ValidarNombre((t.Nombre ?? string.Empty).Trim());
                if (errT is not null) return $"Tema de «{u.Nombre}»: {errT}";
                if (t.Contenidos is null || t.Contenidos.Count == 0) return $"El tema «{t.Nombre}» debe tener al menos un contenido mínimo.";
                if (HayRepetidos(t.Contenidos)) return $"El tema «{t.Nombre}» tiene contenidos con el mismo nombre.";
                foreach (var c in t.Contenidos)
                {
                    var errC = ValidarNombre((c ?? string.Empty).Trim());
                    if (errC is not null) return $"Contenido de «{t.Nombre}»: {errC}";
                }
            }
        }
        return null;
    }

    private static bool HayRepetidos(IEnumerable<string?> nombres)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return nombres.Any(n => !vistos.Add((n ?? string.Empty).Trim()));
    }

    private static Resultado<ElementoDto> NoExiste(string nivel) =>
        Resultado<ElementoDto>.Falla(CodigoResultado.NoEncontrado, $"El elemento ({nivel}) no existe en esta materia.");
}
