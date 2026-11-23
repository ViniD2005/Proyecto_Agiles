using EduMonitor.Analytics.Api.Agente;
using Xunit;

namespace EduMonitor.Analytics.Tests;

/// <summary>T13.2 / T13.3 / T13.4 — Anonimización (RN-11), planificación de herramientas y descarte de datos inventados.</summary>
public class AgenteLogicaTests
{
    private static Anonimizador Crear() => new(new[] { (10, "Lucía Ramírez"), (11, "Carlos Mena"), (12, "Joel Herrera"), (13, "Joel Mena") });

    // ---------------------------------------------------------------- anonimización
    [Fact]
    public void LosAliasSonConsecutivosPorId_YElModeloNuncaVeElNombre()
    {
        var a = Crear();
        Assert.Equal("E01", a.Alias(10));
        Assert.Equal("E03", a.Alias(12));
        var paraModelo = a.ParaModelo($"El estudiante {Marcador.Est(10)} tiene nota 4,2 y {Marcador.Est(12)} tiene 8,0.");
        Assert.Contains("E01", paraModelo);
        Assert.Contains("E03", paraModelo);
        Assert.DoesNotContain("Lucía", paraModelo);
        Assert.DoesNotContain("Joel", paraModelo);
        Assert.DoesNotContain("{est:", paraModelo);
    }

    [Fact]
    public void ElDocenteVeElNombreReal()
    {
        var a = Crear();
        Assert.Equal("Lucía Ramírez está en riesgo.", a.ParaDocente($"{Marcador.Est(10)} está en riesgo."));
    }

    [Fact]
    public void LaPreguntaDelDocenteSeAnonimiza_YSeIdentificaAlEstudiante()
    {
        var a = Crear();
        var (texto, mencionados) = a.AnonimizarPregunta("¿Cómo va Lucía Ramírez en el parcial?");
        Assert.DoesNotContain("Lucía", texto);
        Assert.DoesNotContain("Ramírez", texto);
        Assert.Contains("E01", texto);
        Assert.Equal(new[] { 10 }, mencionados);
    }

    [Fact]
    public void LaBusquedaDeNombresIgnoraTildesYMayusculas()
    {
        var (texto, mencionados) = Crear().AnonimizarPregunta("como esta LUCIA ramirez");
        Assert.DoesNotContain("LUCIA", texto);
        Assert.Equal(new[] { 10 }, mencionados);
    }

    [Fact]
    public void UnNombreCompartidoPorVariosEstudiantesSeOcultaSinElegirUno()
    {
        var (texto, mencionados) = Crear().AnonimizarPregunta("¿Y Mena?");   // Carlos Mena y Joel Mena
        Assert.DoesNotContain("Mena", texto);
        Assert.Contains("[estudiante]", texto);
        Assert.Empty(mencionados);
    }

    [Fact]
    public void UnCodigoEscritoPorElDocenteTambienCuentaComoMencion()
    {
        var (_, mencionados) = Crear().AnonimizarPregunta("Dame la ficha de E02");
        Assert.Equal(new[] { 11 }, mencionados);
    }

    [Fact]
    public void SeDetectaUnCodigoQueElModeloInvento()
    {
        var a = Crear();
        Assert.False(a.TieneCodigoInventado("E01 y E04 necesitan refuerzo."));
        Assert.True(a.TieneCodigoInventado("E01 y E09 necesitan refuerzo."));
    }

    [Fact]
    public void LosCodigosDelModeloVuelvenANombresParaElDocente()
    {
        Assert.Equal("Lucía Ramírez y Carlos Mena", Crear().RestaurarParaDocente("E01 y E02"));
    }

    // ---------------------------------------------------------------- planificador
    private static readonly string[] Base = { "listar_cursos_autorizados", "obtener_calificaciones", "identificar_alertas", "generar_resumen" };

    [Fact]
    public void TodaConsultaUsaAlMenosCuatroHerramientasReales()
    {
        var plan = PlanificadorConsulta.Planificar("hola", Array.Empty<int>(), 7);
        Assert.True(plan.Llamadas.Count >= 4);
        Assert.Equal(Base, plan.Llamadas.Take(4).Select(l => l.Nombre));
    }

    [Fact]
    public void LaPreguntaDeRiesgoOTemasAgregaElDesempenoPorTema()
    {
        var plan = PlanificadorConsulta.Planificar("¿Qué estudiantes están en riesgo?", Array.Empty<int>(), 7);
        Assert.Contains(plan.Llamadas, l => l.Nombre == "obtener_desempeno_temas");
        Assert.Contains("Riesgo", plan.Intencion);
    }

    [Fact]
    public void ElEstudianteMencionadoAgregaSuFicha_ConLaMateriaDelCurso()
    {
        var plan = PlanificadorConsulta.Planificar("ficha de E01", new[] { 10 }, 7);
        var ficha = Assert.Single(plan.Llamadas, l => l.Nombre == "obtener_ficha_estudiante");
        Assert.Equal("7", ficha.Argumentos["materiaId"]);
        Assert.Equal("10", ficha.Argumentos["estudianteId"]);
    }

    [Fact]
    public void ElModeloNoPuedeCambiarLaMateria_TodasLasLlamadasUsanLaDelDocente()
    {
        var plan = PlanificadorConsulta.Planificar("riesgo en la materia 99", new[] { 10 }, 7);
        Assert.All(plan.Llamadas.Where(l => l.Argumentos.ContainsKey("materiaId")), l => Assert.Equal("7", l.Argumentos["materiaId"]));
    }

    // ---------------------------------------------------------------- enfoque y alcance
    [Theory]
    [InlineData("¿Quién ganó el mundial de fútbol?")]
    [InlineData("Dame una receta de pasta")]
    [InlineData("hola")]
    public void UnaPreguntaAjenaAlCursoSeMarcaFueraDeAlcance(string pregunta)
    {
        var plan = PlanificadorConsulta.Planificar(pregunta, Array.Empty<int>(), 7);
        Assert.True(plan.FueraDeAlcance);
        Assert.Equal(PlanificadorConsulta.IntencionFueraDeAlcance, plan.Intencion);
    }

    [Theory]
    [InlineData("¿Qué estudiantes están en riesgo y por qué?", Enfoque.Riesgo)]
    [InlineData("¿Qué temas son más débiles?", Enfoque.Temas)]
    [InlineData("¿Quiénes no han entregado actividades?", Enfoque.Entregas)]
    [InlineData("¿Qué alertas hay en el curso?", Enfoque.Alertas)]
    [InlineData("¿Cuál es el promedio del curso?", Enfoque.Calificaciones)]
    [InlineData("Dame un resumen del curso", Enfoque.Resumen)]
    public void LaPreguntaDelCursoTieneSuEnfoque(string pregunta, Enfoque esperado)
    {
        var plan = PlanificadorConsulta.Planificar(pregunta, Array.Empty<int>(), 7);
        Assert.False(plan.FueraDeAlcance);
        Assert.True(plan.Enfoque.HasFlag(esperado), $"{plan.Enfoque}");
    }

    [Fact]
    public void LaPreguntaDeEntregasNoTrataLasAlertasDeNotaBaja()
    {
        var plan = PlanificadorConsulta.Planificar("¿Quiénes no han entregado actividades?", Array.Empty<int>(), 7);
        Assert.False(plan.Enfoque.HasFlag(Enfoque.Alertas));
        Assert.False(plan.Enfoque.HasFlag(Enfoque.Riesgo));
    }

    [Fact]
    public void UnEstudianteMencionadoNuncaEsFueraDeAlcance()
    {
        var plan = PlanificadorConsulta.Planificar("¿Cómo va E03?", new[] { 3 }, 7);
        Assert.False(plan.FueraDeAlcance);
        Assert.True(plan.Enfoque.HasFlag(Enfoque.Estudiante));
    }

    // ---------------------------------------------------------------- validador del modelo
    private const string Evidencia =
        "- E03: nota ponderada 3,72 (crítico), 92,8 % del parcial evaluado.\n- 6 estudiante(s) en riesgo: E03 (3,72), E04 (3,74), E10 (6,11).";

    [Fact]
    public void AceptaLoQueRepiteLosHechos() =>
        Assert.True(ValidadorModelo.EstaRespaldada("E03 tiene nota 3.72 y está en nivel crítico.", Evidencia, 0, false));

    [Fact]
    public void DescartaUnaCifraQueElSistemaNoCalculo() =>
        Assert.False(ValidadorModelo.EstaRespaldada("El promedio real sería 5.4.", Evidencia, 0, false));

    [Fact]
    public void DescartaLaNotaDeOtroEstudiante() =>
        Assert.False(ValidadorModelo.EstaRespaldada("E04 con una nota de 3.72 necesita apoyo.", Evidencia, 0, false));

    [Fact]
    public void DescartaSinDatosCuandoTodosTienenNotas()
    {
        Assert.False(ValidadorModelo.EstaRespaldada("E10 no tiene datos todavía.", Evidencia, 0, false));
        Assert.True(ValidadorModelo.EstaRespaldada("E10 no tiene datos todavía.", Evidencia, 1, false));
    }

    [Fact]
    public void DescartaUnaTendenciaQueLosHechosNoMencionan()
    {
        Assert.False(ValidadorModelo.EstaRespaldada("E03 muestra una tendencia descendente.", Evidencia, 0, false));
        Assert.True(ValidadorModelo.EstaRespaldada("E03 muestra una tendencia descendente.", Evidencia + "\n- E03: tendencia a la baja.", 0, false));
    }

    [Fact]
    public void DescartaElRellenoDeIncertidumbre() =>
        Assert.False(ValidadorModelo.EstaRespaldada("No hay datos adicionales que afecten la interpretación.", Evidencia, 0, true));

    [Fact]
    public void ElRecorteDelModeloTerminaEnUnaFraseCompleta()
    {
        var largo = string.Join(" ", Enumerable.Repeat("El curso tiene un promedio de 6,29 y varios estudiantes en umbral.", 12));
        var corto = SalidaModelo.CortarEnFrase(largo, 200);
        Assert.True(corto.Length <= 200);
        Assert.True(corto.EndsWith("."), corto);
    }

    [Fact]
    public void ElRecorteSinPuntosNoDejaPalabrasAMedias()
    {
        var corto = SalidaModelo.CortarEnFrase(string.Join(" ", Enumerable.Repeat("palabra", 80)), 100);
        Assert.True(corto.EndsWith("palabra…"), corto);
    }
}
