using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Moodle;
using EduMonitor.Academic.Api.Services;
using EduMonitor.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddDbContext<AcademicDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<MoodleOptions>(builder.Configuration.GetSection(MoodleOptions.Section));
builder.Services.AddHttpClient<IMoodleClient, MoodleClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptions<MoodleOptions>>().Value;
    http.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
});
builder.Services.AddScoped<ISyncService, SyncService>();

builder.Services.AddEduMonitorAuth(builder.Configuration);
builder.Services.AddEduMonitorSwagger("MS-B · Académico (Moodle)");
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks().AddDbContextCheck<AcademicDbContext>("sqlserver");

var app = builder.Build();

// Crear BD con reintentos (SQL Server tarda en iniciar)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
    for (var i = 1; ; i++)
    {
        try { await db.Database.EnsureCreatedAsync(); break; }
        catch (Exception ex) when (i < 10)
        {
            app.Logger.LogWarning("BD no disponible (intento {I}): {Msg}", i, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
