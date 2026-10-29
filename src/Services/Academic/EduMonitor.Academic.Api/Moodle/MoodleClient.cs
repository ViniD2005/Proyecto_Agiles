using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EduMonitor.Academic.Api.Moodle;

public interface IMoodleClient
{
    string BaseUrl { get; }
    Task<MoodleSiteInfo> GetSiteInfoAsync(CancellationToken ct = default);
    Task<MoodleUser?> GetUserByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<MoodleCourse>> GetUserCoursesAsync(int userId, CancellationToken ct = default);
    Task<IReadOnlyList<MoodleUser>> GetEnrolledUsersAsync(int courseId, CancellationToken ct = default);
    Task<IReadOnlyList<MoodleSection>> GetCourseContentsAsync(int courseId, CancellationToken ct = default);
}

/// <summary>
/// Cliente HTTP de los servicios web REST de Moodle (solo lectura, RN-06).
/// Todas las llamadas usan el token de alcance limitado (HU-02) leído de variables de entorno.
/// </summary>
public class MoodleClient(HttpClient http, IOptions<MoodleOptions> options, ILogger<MoodleClient> logger) : IMoodleClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly MoodleOptions _opt = options.Value;

    public string BaseUrl => _opt.BaseUrl;

    public Task<MoodleSiteInfo> GetSiteInfoAsync(CancellationToken ct = default) =>
        CallAsync<MoodleSiteInfo>("core_webservice_get_site_info", new(), ct);

    public async Task<MoodleUser?> GetUserByEmailAsync(string email, CancellationToken ct = default)
    {
        var users = await CallAsync<List<MoodleUser>>("core_user_get_users_by_field",
            new() { ["field"] = "email", ["values[0]"] = email }, ct);
        return users.FirstOrDefault();
    }

    public async Task<IReadOnlyList<MoodleCourse>> GetUserCoursesAsync(int userId, CancellationToken ct = default) =>
        await CallAsync<List<MoodleCourse>>("core_enrol_get_users_courses",
            new() { ["userid"] = userId.ToString() }, ct);

    public async Task<IReadOnlyList<MoodleUser>> GetEnrolledUsersAsync(int courseId, CancellationToken ct = default) =>
        await CallAsync<List<MoodleUser>>("core_enrol_get_enrolled_users",
            new() { ["courseid"] = courseId.ToString() }, ct);

    public async Task<IReadOnlyList<MoodleSection>> GetCourseContentsAsync(int courseId, CancellationToken ct = default) =>
        await CallAsync<List<MoodleSection>>("core_course_get_contents",
            new() { ["courseid"] = courseId.ToString() }, ct);

    private async Task<T> CallAsync<T>(string function, Dictionary<string, string> parameters, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.Token))
            throw new MoodleException("sin_token", "No se configuró Moodle__Token (variable de entorno).");

        var form = new Dictionary<string, string>(parameters)
        {
            ["wstoken"] = _opt.Token,
            ["wsfunction"] = function,
            ["moodlewsrestformat"] = "json"
        };

        logger.LogInformation("Moodle WS -> {Function}", function); // nunca se registra el token

        using var response = await http.PostAsync("webservice/rest/server.php", new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        // Moodle devuelve los errores con HTTP 200 y un objeto con "exception"
        if (body.TrimStart().StartsWith('{') && body.Contains("\"exception\""))
        {
            var err = JsonSerializer.Deserialize<MoodleError>(body, Json);
            throw new MoodleException(err?.ErrorCode ?? "desconocido", err?.Message ?? "Error de Moodle");
        }

        return JsonSerializer.Deserialize<T>(body, Json)
               ?? throw new MoodleException("respuesta_vacia", $"Moodle devolvió una respuesta vacía para {function}.");
    }
}
