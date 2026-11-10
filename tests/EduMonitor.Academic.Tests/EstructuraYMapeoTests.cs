using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Domain;
using EduMonitor.Academic.Api.Services;
using EduMonitor.Common.Messaging;
using EduMonitor.Common.Results;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EduMonitor.Academic.Tests.TestSupport;

namespace EduMonitor.Academic.Tests;

/// <summary>HU-08 y HU-09 · estructura temática y mapeo actividad ↔ contenidos.</summary>
public class EstructuraYMapeoTests
{
    private static async Task<(Ctx Ctx, Materia Materia)> PrepararAsync()
    {
        var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var pub = new FakePublisher();
        var dispatcher = Dispatcher(db, pub);
        return (new Ctx(db, pub, new EstructuraService(db, dispatcher), new MapeoService(db, dispatcher)), materia);
    }

    private sealed record Ctx(AcademicDbContext Db, FakePublisher Pub, EstructuraService Estructura, MapeoService Mapeo);

    // Crea U1 → T1 → C1 y devuelve los ids
    private static async Task<(int unidad, int tema, int contenido)> CrearCadenaAsync(Ctx c, int materiaId)
    {
        var u = (await c.Estructura.CrearAsync(materiaId, "unidad", null, "Unidad 1")).Valor!;
        var t = (await c.Estructura.CrearAsync(materiaId, "tema", u.Id, "Fundamentos HTML")).Valor!;
        var k = (await c.Estructura.CrearAsync(materiaId, "contenido", t.Id, "Etiquetas semánticas")).Valor!;
        return (u.Id, t.Id, k.Id);
    }

    private static async Task<Actividad> ActividadAsync(AcademicDbContext db, int materiaId, string nombre = "APE 1", int cmid = 4)
    {
        var a = new Actividad { MateriaId = materiaId, MoodleCmId = cmid, MoodleInstanceId = cmid, Modulo = "assign", Nombre = nombre, Tipo = TipoActividad.APE, Peso = 15m };
        db.Actividades.Add(a);
        await db.SaveChangesAsync();
        return a;
    }

    // ------------------------------------------------------------------ HU-08
    [Fact]
    public async Task CrearUnidadTemaYContenido_ArmanElArbol_YPublicanElEvento()
    {
        var (c, materia) = await PrepararAsync();
        await CrearCadenaAsync(c, materia.Id);

        var arbol = await c.Estructura.ObtenerAsync(materia.Id);
        var unidad = Assert.Single(arbol.Unidades);
        Assert.Equal("Unidad 1", unidad.Nombre);
        Assert.Equal("Etiquetas semánticas", Assert.Single(Assert.Single(unidad.Temas).Contenidos).Nombre);
        Assert.True(arbol.Completa);
        Assert.Equal(3, c.Pub.Publicados.Count(p => p.Evento == EventNames.MapeoActividadActualizado));
    }

    [Fact]
    public async Task UnaUnidadSinTemas_GeneraAdvertencia_YLaEstructuraNoEstaCompleta()
    {
        var (c, materia) = await PrepararAsync();
        await c.Estructura.CrearAsync(materia.Id, "unidad", null, "Unidad 1");

        var arbol = await c.Estructura.ObtenerAsync(materia.Id);
        Assert.False(arbol.Completa);
        Assert.Contains(arbol.Advertencias, a => a.Contains("no tiene temas"));
    }

    [Fact]
    public async Task NombresVaciosOExtensos_YDuplicados_SeRechazan()
    {
        var (c, materia) = await PrepararAsync();
        Assert.Equal(CodigoResultado.Invalido, (await c.Estructura.CrearAsync(materia.Id, "unidad", null, "   ")).Codigo);
        Assert.Equal(CodigoResultado.Invalido, (await c.Estructura.CrearAsync(materia.Id, "unidad", null, new string('x', 201))).Codigo);
        Assert.Equal(CodigoResultado.Invalido, (await c.Estructura.CrearAsync(materia.Id, "capitulo", null, "X")).Codigo);

        await c.Estructura.CrearAsync(materia.Id, "unidad", null, "Unidad 1");
        Assert.Equal(CodigoResultado.Duplicado, (await c.Estructura.CrearAsync(materia.Id, "unidad", null, "unidad 1")).Codigo);
    }

    [Fact]
    public async Task UnaMateriaAdmiteHasta10Unidades()
    {
        var (c, materia) = await PrepararAsync();
        for (var i = 1; i <= EstructuraService.MaxUnidades; i++)
            Assert.True((await c.Estructura.CrearAsync(materia.Id, "unidad", null, $"Unidad {i}")).EsOk);
        Assert.Equal(CodigoResultado.Invalido, (await c.Estructura.CrearAsync(materia.Id, "unidad", null, "Unidad 11")).Codigo);
    }

    [Fact]
    public async Task NoSePuedeColgarUnTemaDeLaUnidadDeOtraMateria()
    {
        var (c, materia) = await PrepararAsync();
        var otra = new Materia { MoodleCourseId = 99, Nombre = "Otra", DocenteEmail = "docente2@uta.edu.ec" };
        c.Db.Materias.Add(otra); await c.Db.SaveChangesAsync();
        var unidadAjena = (await c.Estructura.CrearAsync(otra.Id, "unidad", null, "Ajena")).Valor!;

        var r = await c.Estructura.CrearAsync(materia.Id, "tema", unidadAjena.Id, "Intruso");
        Assert.Equal(CodigoResultado.NoEncontrado, r.Codigo);
        Assert.Equal(CodigoResultado.NoEncontrado, (await c.Estructura.RenombrarAsync(materia.Id, "unidad", unidadAjena.Id, "Hackeada")).Codigo);
        Assert.Equal(CodigoResultado.NoEncontrado, (await c.Estructura.EliminarAsync(materia.Id, "unidad", unidadAjena.Id, true)).Codigo);
    }

    [Fact]
    public async Task EliminarUnContenidoAsignadoPideConfirmacion_YAlConfirmarLoElimina()
    {
        var (c, materia) = await PrepararAsync();
        var (_, _, contenido) = await CrearCadenaAsync(c, materia.Id);
        var actividad = await ActividadAsync(c.Db, materia.Id);
        await c.Mapeo.AsignarAsync(actividad, new[] { contenido });

        var sinConfirmar = await c.Estructura.EliminarAsync(materia.Id, "contenido", contenido, confirmar: false);
        Assert.Equal(CodigoResultado.Conflicto, sinConfirmar.Codigo);
        Assert.Equal(1, sinConfirmar.ActividadesAfectadas);
        Assert.Equal(1, await c.Db.Contenidos.CountAsync());

        var confirmado = await c.Estructura.EliminarAsync(materia.Id, "contenido", contenido, confirmar: true);
        Assert.True(confirmado.EsOk);
        Assert.Equal(0, await c.Db.Contenidos.CountAsync());
        Assert.Equal(0, await c.Db.Mapeos.CountAsync());
        Assert.True((await c.Mapeo.ObtenerAsync(materia.Id)).Actividades.Single().Pendiente);
    }

    [Fact]
    public async Task EliminarUnaUnidadSinMapeos_NoPideConfirmacion_YBorraSusHijos()
    {
        var (c, materia) = await PrepararAsync();
        var (unidad, _, _) = await CrearCadenaAsync(c, materia.Id);

        Assert.True((await c.Estructura.EliminarAsync(materia.Id, "unidad", unidad, confirmar: false)).EsOk);
        Assert.Equal(0, await c.Db.Unidades.CountAsync());
        Assert.Equal(0, await c.Db.Temas.CountAsync());
        Assert.Equal(0, await c.Db.Contenidos.CountAsync());
    }

    [Fact]
    public async Task Renombrar_CambiaElNombre_YRechazaDuplicados()
    {
        var (c, materia) = await PrepararAsync();
        var a = (await c.Estructura.CrearAsync(materia.Id, "unidad", null, "Unidad 1")).Valor!;
        await c.Estructura.CrearAsync(materia.Id, "unidad", null, "Unidad 2");

        Assert.Equal("Introducción", (await c.Estructura.RenombrarAsync(materia.Id, "unidad", a.Id, "Introducción")).Valor!.Nombre);
        Assert.Equal(CodigoResultado.Duplicado, (await c.Estructura.RenombrarAsync(materia.Id, "unidad", a.Id, "Unidad 2")).Codigo);
    }

    private static ImportarEstructuraRequest Silabo() => new(new List<ImportarUnidad>
    {
        new("Unidad 1", new() { new("HTML y CSS", new() { "Etiquetas semánticas", "Selectores CSS" }) }),
        new("Unidad 2", new() { new("JavaScript básico", new() { "Variables y tipos" }) }),
    });

    [Fact]
    public async Task ImportarSilabo_CargaLaEstructura_SoloSiLaMateriaEstaVacia()
    {
        var (c, materia) = await PrepararAsync();
        var primera = await c.Estructura.ImportarAsync(materia.Id, Silabo());
        Assert.True(primera.EsOk);
        Assert.Equal(2, primera.Valor!.Unidades.Count);
        Assert.Equal(3, await c.Db.Contenidos.CountAsync());
        Assert.True(primera.Valor.Completa);

        Assert.Equal(CodigoResultado.Conflicto, (await c.Estructura.ImportarAsync(materia.Id, Silabo())).Codigo);
    }

    [Fact]
    public async Task ImportarSilabo_RechazaUnidadesSinTemas_YTemasSinContenidos()
    {
        var (c, materia) = await PrepararAsync();
        var sinTemas = new ImportarEstructuraRequest(new() { new("Unidad 1", new()) });
        var sinContenidos = new ImportarEstructuraRequest(new() { new("Unidad 1", new() { new("Tema", new()) }) });
        Assert.Equal(CodigoResultado.Invalido, (await c.Estructura.ImportarAsync(materia.Id, sinTemas)).Codigo);
        Assert.Equal(CodigoResultado.Invalido, (await c.Estructura.ImportarAsync(materia.Id, sinContenidos)).Codigo);
        Assert.Equal(CodigoResultado.Invalido, (await c.Estructura.ImportarAsync(materia.Id, new ImportarEstructuraRequest(null))).Codigo);
        Assert.Equal(0, await c.Db.Unidades.CountAsync());
    }

    // ------------------------------------------------------------------ HU-09
    [Fact]
    public async Task AsignarUnoOVariosContenidos_MarcaLaActividadComoMapeada_YPublicaElEvento()
    {
        var (c, materia) = await PrepararAsync();
        var (_, tema, c1) = await CrearCadenaAsync(c, materia.Id);
        var c2 = (await c.Estructura.CrearAsync(materia.Id, "contenido", tema, "Selectores CSS")).Valor!.Id;
        var actividad = await ActividadAsync(c.Db, materia.Id);
        c.Pub.Publicados.Clear();

        var r = await c.Mapeo.AsignarAsync(actividad, new[] { c1, c2 });

        Assert.True(r.EsOk);
        Assert.Equal(2, r.Valor!.Actividad.Contenidos.Count);
        Assert.False(r.Valor.Actividad.Pendiente);
        Assert.True(r.Valor.EventoPublicado);
        var (evento, mensaje) = Assert.Single(c.Pub.Publicados);
        Assert.Equal(EventNames.MapeoActividadActualizado, evento);
        Assert.Equal(2, ((MapeoActividadActualizado)mensaje).Mapeo.Count);
    }

    [Fact]
    public async Task LasActividadesSinMapear_SeMarcanComoPendientes()
    {
        var (c, materia) = await PrepararAsync();
        var (_, _, contenido) = await CrearCadenaAsync(c, materia.Id);
        var mapeada = await ActividadAsync(c.Db, materia.Id, "APE 1", 4);
        await ActividadAsync(c.Db, materia.Id, "APE 2", 5);
        await c.Mapeo.AsignarAsync(mapeada, new[] { contenido });

        var dto = await c.Mapeo.ObtenerAsync(materia.Id);
        Assert.Equal(1, dto.Pendientes);
        Assert.True(dto.Actividades.Single(a => a.Nombre == "APE 2").Pendiente);
        Assert.False(dto.Actividades.Single(a => a.Nombre == "APE 1").Pendiente);
    }

    [Fact]
    public async Task UnContenidoDeOtraMateria_SeRechaza_YNoSeGuardaNada()
    {
        var (c, materia) = await PrepararAsync();
        var otra = new Materia { MoodleCourseId = 99, Nombre = "Otra", DocenteEmail = "docente2@uta.edu.ec" };
        c.Db.Materias.Add(otra); await c.Db.SaveChangesAsync();
        var (_, _, ajeno) = await CrearCadenaAsync(c, otra.Id);
        var actividad = await ActividadAsync(c.Db, materia.Id);

        var r = await c.Mapeo.AsignarAsync(actividad, new[] { ajeno });
        Assert.Equal(CodigoResultado.Invalido, r.Codigo);
        Assert.Equal(0, await c.Db.Mapeos.CountAsync());
    }

    [Fact]
    public async Task ReasignarReemplazaLosContenidos_YUnaListaVaciaDejaLaActividadPendiente()
    {
        var (c, materia) = await PrepararAsync();
        var (_, tema, c1) = await CrearCadenaAsync(c, materia.Id);
        var c2 = (await c.Estructura.CrearAsync(materia.Id, "contenido", tema, "Selectores CSS")).Valor!.Id;
        var actividad = await ActividadAsync(c.Db, materia.Id);

        await c.Mapeo.AsignarAsync(actividad, new[] { c1, c2 });
        var soloUno = await c.Mapeo.AsignarAsync(actividad, new[] { c2, c2 });   // repetidos se ignoran
        Assert.Equal(c2, Assert.Single(soloUno.Valor!.Actividad.Contenidos).ContenidoId);

        var vacia = await c.Mapeo.AsignarAsync(actividad, new List<int>());
        Assert.True(vacia.Valor!.Actividad.Pendiente);
        Assert.Equal(0, await c.Db.Mapeos.CountAsync());
    }
}
