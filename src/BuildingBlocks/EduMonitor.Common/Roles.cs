namespace EduMonitor.Common;

/// <summary>Roles del sistema (RF-01).</summary>
public static class Roles
{
    public const string Docente = "Docente";
    public const string AdministradorTecnico = "AdministradorTecnico";
}

/// <summary>Políticas de autorización compartidas entre Gateway y microservicios.</summary>
public static class Policies
{
    public const string SoloDocente = "SoloDocente";
    public const string SoloAdministrador = "SoloAdministrador";
    public const string Autenticado = "Autenticado";
}
