using EduMonitor.Academic.Api.Domain;

namespace EduMonitor.Academic.Api.Services;

/// <summary>RN-07: cada docente ve únicamente sus materias (comparación por correo institucional).</summary>
public static class MateriaAccess
{
    public static bool PuedeAcceder(Materia? materia, string? emailDocente) =>
        materia is not null &&
        !string.IsNullOrWhiteSpace(emailDocente) &&
        string.Equals(materia.DocenteEmail, emailDocente.Trim(), StringComparison.OrdinalIgnoreCase);
}
