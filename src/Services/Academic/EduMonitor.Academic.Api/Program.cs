using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Moodle;
using EduMonitor.Academic.Api.Services;
using EduMonitor.Common;
using EduMonitor.Common.Messaging;
using EduMonitor.Common.Persistence;
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
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddScoped<EventSnapshotBuilder>();
builder.Services.AddScoped<MapeoEventDispatcher>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<EstructuraService>();
builder.Services.AddScoped<MapeoService>();

builder.Services.AddEduMonitorAuth(builder.Configuration);
builder.Services.AddEduMonitorSwagger("MS-B · Académico (Moodle)");
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks().AddDbContextCheck<AcademicDbContext>("sqlserver");

var app = builder.Build();

// Migraciones de EF Core con reintentos (SQL Server tarda en iniciar). Reemplaza a EnsureCreated del Sprint 1.
await DatabaseMigrator.MigrateWithRetryAsync<AcademicDbContext>(app.Services, app.Logger);

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
