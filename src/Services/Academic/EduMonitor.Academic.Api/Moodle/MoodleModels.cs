using System.Text.Json.Serialization;

namespace EduMonitor.Academic.Api.Moodle;

public class MoodleOptions
{
    public const string Section = "Moodle";
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Token del servicio externo. SIEMPRE por variable de entorno (Moodle__Token).</summary>
    public string Token { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>Error de Moodle: llega con HTTP 200 y cuerpo {exception, errorcode, message}.</summary>
public class MoodleException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

public record MoodleError(
    [property: JsonPropertyName("exception")] string? Exception,
    [property: JsonPropertyName("errorcode")] string? ErrorCode,
    [property: JsonPropertyName("message")] string? Message);

public record MoodleSiteInfo(
    [property: JsonPropertyName("sitename")] string SiteName,
    [property: JsonPropertyName("username")] string UserName,
    [property: JsonPropertyName("release")] string Release,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("userid")] int UserId);

public record MoodleUser(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("firstname")] string? FirstName,
    [property: JsonPropertyName("lastname")] string? LastName,
    [property: JsonPropertyName("fullname")] string? FullName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("roles")] List<MoodleRole>? Roles);

public record MoodleRole(
    [property: JsonPropertyName("roleid")] int RoleId,
    [property: JsonPropertyName("shortname")] string ShortName);

public record MoodleCourse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("shortname")] string ShortName,
    [property: JsonPropertyName("fullname")] string FullName,
    [property: JsonPropertyName("enrolledusercount")] int? EnrolledUserCount,
    [property: JsonPropertyName("visible")] int? Visible);

public record MoodleSection(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("section")] int Section,
    [property: JsonPropertyName("modules")] List<MoodleModule>? Modules);

public record MoodleModule(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("instance")] int Instance,
    [property: JsonPropertyName("modname")] string ModName,
    [property: JsonPropertyName("visible")] int? Visible);
