using EduMonitor.Academic.Api.Domain;
using EduMonitor.Academic.Api.Services;
using Xunit;

namespace EduMonitor.Academic.Tests;

public class ClasificacionTests
{
    [Theory]
    [InlineData("Evaluación 1", TipoActividad.Evaluacion)]
    [InlineData("Examen del primer parcial", TipoActividad.Evaluacion)]
    [InlineData("APE 1", TipoActividad.APE)]
    [InlineData("ape-2 práctica de laboratorio", TipoActividad.APE)]
    [InlineData("Trabajo autónomo 3", TipoActividad.TrabajoAutonomo)]
    [InlineData("T4: Consumo de APIs", TipoActividad.TrabajoAutonomo)]
    [InlineData("Foro de dudas", TipoActividad.Otra)]
    public void Clasifica_PorNombre(string nombre, TipoActividad esperado) =>
        Assert.Equal(esperado, ActivityClassifier.Clasificar(nombre));

    [Fact]
    public void AsignaPesos_40_30_30_ConRN03()
    {
        // 1 evaluación, 2 APE, 4 trabajos autónomos (Moodle de prueba)
        var acts = new List<Actividad>
        {
            new() { Tipo = TipoActividad.Evaluacion },
            new() { Tipo = TipoActividad.APE }, new() { Tipo = TipoActividad.APE },
            new() { Tipo = TipoActividad.TrabajoAutonomo }, new() { Tipo = TipoActividad.TrabajoAutonomo },
            new() { Tipo = TipoActividad.TrabajoAutonomo }, new() { Tipo = TipoActividad.TrabajoAutonomo },
        };
        ActivityClassifier.AsignarPesos(acts);

        Assert.Equal(40m, acts[0].Peso);
        Assert.All(acts.Where(a => a.Tipo == TipoActividad.APE), a => Assert.Equal(15m, a.Peso));
        Assert.All(acts.Where(a => a.Tipo == TipoActividad.TrabajoAutonomo), a => Assert.Equal(7.5m, a.Peso));
        Assert.Equal(100m, acts.Sum(a => a.Peso));
    }
}

/// <summary>T6.5 - Un docente no accede a la materia de otro (HU-06, criterio 4 / RN-07).</summary>
public class PermisosTests
{
    private static readonly Materia MateriaDocenteA = new() { Id = 1, Nombre = "Programación Web", DocenteEmail = "docente1@uta.edu.ec" };

    [Fact] public void DocenteDueno_PuedeAcceder() =>
        Assert.True(MateriaAccess.PuedeAcceder(MateriaDocenteA, "docente1@uta.edu.ec"));

    [Fact] public void ComparacionInsensibleAMayusculas() =>
        Assert.True(MateriaAccess.PuedeAcceder(MateriaDocenteA, "DOCENTE1@UTA.EDU.EC"));

    [Fact] public void OtroDocente_NoPuedeAcceder() =>
        Assert.False(MateriaAccess.PuedeAcceder(MateriaDocenteA, "docente2@uta.edu.ec"));

    [Fact] public void SinCorreo_NoPuedeAcceder() =>
        Assert.False(MateriaAccess.PuedeAcceder(MateriaDocenteA, null));

    [Fact] public void MateriaInexistente_NoPuedeAcceder() =>
        Assert.False(MateriaAccess.PuedeAcceder(null, "docente1@uta.edu.ec"));
}
