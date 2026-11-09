using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Common.Results;

public enum CodigoResultado { Ok, NoEncontrado, Prohibido, Invalido, Duplicado, Conflicto }

/// <summary>Resultado de una operación de servicio; el controlador lo traduce a HTTP sin repetir código.</summary>
public sealed record Resultado<T>(CodigoResultado Codigo, T? Valor = default, string? Mensaje = null, int ActividadesAfectadas = 0)
{
    public bool EsOk => Codigo == CodigoResultado.Ok;
    public static Resultado<T> Ok(T valor) => new(CodigoResultado.Ok, valor);
    public static Resultado<T> Falla(CodigoResultado codigo, string mensaje, int afectadas = 0) => new(codigo, default, mensaje, afectadas);
}

/// <summary>Cuerpo de la respuesta 409 cuando una acción necesita confirmación del docente (HU-08, criterio 3).</summary>
public record ConflictoRespuesta(bool RequiereConfirmacion, int ActividadesAfectadas, string Mensaje);

public static class ResultadoExtensions
{
    public static IActionResult ToActionResult<T>(this Resultado<T> r, ControllerBase c, Func<T, IActionResult>? ok = null)
    {
        switch (r.Codigo)
        {
            case CodigoResultado.Ok:
                return ok is not null ? ok(r.Valor!) : c.Ok(r.Valor);
            case CodigoResultado.NoEncontrado:
                return c.Problem(detail: r.Mensaje, statusCode: StatusCodes.Status404NotFound, title: "No encontrado");
            case CodigoResultado.Prohibido:
                return c.Problem(detail: r.Mensaje ?? "No tienes permiso sobre este recurso.", statusCode: StatusCodes.Status403Forbidden, title: "Acceso denegado");
            case CodigoResultado.Invalido:
                return c.Problem(detail: r.Mensaje, statusCode: StatusCodes.Status400BadRequest, title: "Solicitud inválida");
            case CodigoResultado.Duplicado:
                return c.Problem(detail: r.Mensaje, statusCode: StatusCodes.Status409Conflict, title: "Elemento duplicado");
            default:
                return c.Conflict(new ConflictoRespuesta(r.ActividadesAfectadas > 0, r.ActividadesAfectadas, r.Mensaje ?? "Conflicto."));
        }
    }
}
