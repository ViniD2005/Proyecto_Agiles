using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Silabo;
using EduMonitor.Common.Auditoria;
using Xunit;

namespace EduMonitor.Analytics.Tests;

/// <summary>HU-21 (extracción del sílabo) y HU-17 (la bitácora no guarda secretos).</summary>
public class SilaboYAuditoriaTests
{
    private const string Silabo = """
        UNIVERSIDAD TÉCNICA DE AMBATO
        Unidad 1: Fundamentos web
        1.1 HTML y CSS
        1.1.1 Etiquetas semánticas
        1.1.2 Flexbox
        1.2 JavaScript
        Unidad 2: Backend
        • APIs REST
        • Autenticación
        """;

    [Fact]
    public void ElExtractorPorPatronesArmaUnidadesTemasYContenidos()
    {
        var u = ExtractorSilaboPorPatrones.Extraer(Silabo);
        Assert.Equal(2, u.Count);
        Assert.Contains("Fundamentos web", u[0].Nombre);
        Assert.Equal(2, u[0].Temas.Count);
        Assert.Equal("HTML y CSS", u[0].Temas[0].Nombre);
        Assert.Equal(new[] { "Etiquetas semánticas", "Flexbox" }, u[0].Temas[0].Contenidos);
        Assert.Equal(new[] { "APIs REST", "Autenticación" }, u[1].Temas.Select(t => t.Nombre));
    }

    [Fact]
    public void ElTextoAntesDeLaPrimeraUnidadSeIgnora()
    {
        var u = ExtractorSilaboPorPatrones.Extraer(Silabo);
        Assert.DoesNotContain(u, x => x.Nombre.Contains("UNIVERSIDAD"));
    }

    [Fact]
    public void SinPatronesReconocibles_NoSeInventaNada()
    {
        Assert.Empty(ExtractorSilaboPorPatrones.Extraer("Texto libre sin estructura de unidades."));
    }

    [Fact]
    public void SeLeeLaPropuestaJsonDelModelo()
    {
        var r = PropuestaSilaboParser.ParsearModelo(
            "{\"unidades\":[{\"nombre\":\"Unidad 1\",\"temas\":[{\"nombre\":\"HTML\",\"contenidos\":[\"Etiquetas\",\"Etiquetas\"]}]}]}");
        Assert.NotNull(r);
        var tema = Assert.Single(Assert.Single(r!).Temas);
        Assert.Equal("HTML", tema.Nombre);
        Assert.Single(tema.Contenidos);   // sin duplicados
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nada")]
    [InlineData("{\"unidades\":[]}")]
    [InlineData("{\"unidades\":[{\"nombre\":\"\",\"temas\":[]}]}")]
    public void UnaRespuestaInutilizableDelModeloDevuelveNulo(string? texto)
    {
        Assert.Null(PropuestaSilaboParser.ParsearModelo(texto));
    }

    [Fact]
    public void LaPropuestaRespetaLosLimitesDeLaImportacion()
    {
        var muchas = Enumerable.Range(1, 30).Select(i => new SilaboUnidadDto($"Unidad {i}",
            new List<SilaboTemaDto> { new($"Tema {i}", new List<string> { "c" }) })).ToList();
        Assert.True(PropuestaSilaboParser.Depurar(muchas).Count <= LimitesSilabo.MaxUnidades);
    }

    [Fact]
    public void ElTextoSeNormalizaYSeLimita()
    {
        var largo = string.Join("\n", Enumerable.Repeat("línea de prueba   con   espacios", 2000));
        var n = TextoSilabo.Normalizar(largo);
        Assert.True(n.Length <= LimitesSilabo.MaxCaracteresTexto);
        Assert.DoesNotContain("  ", n);
        Assert.Equal(string.Empty, TextoSilabo.Normalizar("   "));
    }

    // ---------------------------------------------------------------- auditoría (HU-17, criterio 2)
    [Theory]
    [InlineData("Authorization: Bearer abcdefghijklmnop", "abcdefghijklmnop")]
    [InlineData("login password=Secreta123 ok", "Secreta123")]
    [InlineData("{\"token\": \"xyz-secreto-123\"}", "xyz-secreto-123")]
    [InlineData("jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.firma123456 fin", "eyJhbGciOiJIUzI1NiJ9")]
    public void LaBitacoraOcultaTokensYContrasenas(string entrada, string secreto)
    {
        var limpio = AuditSanitizer.Limpiar(entrada);
        Assert.DoesNotContain(secreto, limpio);
        Assert.Contains(AuditSanitizer.Oculto, limpio);
    }

    [Fact]
    public void UnDetalleSinSecretosNoSeAltera_YSeLimitaLaLongitud()
    {
        Assert.Equal("POST /api/materias/3/reporte materia=3", AuditSanitizer.Limpiar("POST /api/materias/3/reporte materia=3"));
        Assert.Equal(10, AuditSanitizer.Limpiar(new string('a', 50), 10)!.Length);
        Assert.Null(AuditSanitizer.Limpiar(null));
    }

    [Theory]
    [InlineData("lucia@uta.edu.ec", "l***@uta.edu.ec")]
    [InlineData("", "(sin correo)")]
    [InlineData("sin-arroba", "(sin correo)")]
    public void ElCorreoSeEnmascaraParaLaBitacora(string correo, string esperado)
    {
        Assert.Equal(esperado, AuditSanitizer.EnmascararCorreo(correo));
    }
}
