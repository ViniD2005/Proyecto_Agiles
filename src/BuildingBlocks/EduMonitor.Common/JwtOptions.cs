namespace EduMonitor.Common;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "edumonitor-identity";
    public string Audience { get; set; } = "edumonitor-clients";
    public int ExpirationMinutes { get; set; } = 120;
}
