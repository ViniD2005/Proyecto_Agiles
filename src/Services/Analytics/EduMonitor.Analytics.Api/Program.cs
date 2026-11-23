using EduMonitor.Analytics.Api.Agente;
using EduMonitor.Analytics.Api.Agente.Herramientas;
using EduMonitor.Analytics.Api.Agente.Llm;
using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Messaging;
using EduMonitor.Analytics.Api.Recomendaciones;
using EduMonitor.Analytics.Api.Reportes;
using EduMonitor.Analytics.Api.Services;
using EduMonitor.Analytics.Api.Silabo;
using EduMonitor.Common;
using EduMonitor.Common.Auditoria;
using EduMonitor.Common.Messaging;
using EduMonitor.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddDbContext<AnalyticsDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddEduMonitorAuth(builder.Configuration);
builder.Services.AddEduMonitorSwagger("MS-C · Analítica y Agente IA");
builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddDbContextCheck<AnalyticsDbContext>("sqlserver");

// Sprint 2: servicios de análisis y consumo de eventos de RabbitMQ
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddScoped<RecalculoService>();
builder.Services.AddScoped<SnapshotIngestor>();
builder.Services.AddScoped<AnaliticaService>();
builder.Services.AddHostedService<AnalyticsEventConsumer>();

// Sprint 3: agente de IA (HU-13/14), recomendaciones (HU-15), reportes (HU-16), auditoría (HU-17) y opcionales (HU-19/20/21)
builder.Services.AddHttpContextAccessor();
builder.Services.AddEduMonitorAudit(builder.Configuration);
builder.Services.AddLlmProvider(builder.Configuration);                  // Llm:Provider = Ollama | Gemini | Groq
builder.Services.AddScoped<AnalisisCursoService>();
builder.Services.AddScoped<IHerramienta, ListarCursosAutorizadosTool>();
builder.Services.AddScoped<IHerramienta, ObtenerCalificacionesTool>();
builder.Services.AddScoped<IHerramienta, IdentificarAlertasTool>();
builder.Services.AddScoped<IHerramienta, GenerarResumenTool>();
builder.Services.AddScoped<IHerramienta, ObtenerDesempenoTemasTool>();
builder.Services.AddScoped<IHerramienta, ObtenerFichaEstudianteTool>();
builder.Services.AddScoped<RegistroHerramientas>();
builder.Services.AddScoped<AgenteOrquestador>();
builder.Services.AddScoped<RecomendacionService>();
builder.Services.Configure<ReporteOptions>(builder.Configuration.GetSection(ReporteOptions.Section));
builder.Services.AddSingleton<IGeneradorPdfReporte, QuestPdfReporte>();
builder.Services.AddScoped<ReporteService>();
builder.Services.AddSingleton<IExtractorTextoPdf, PdfPigExtractor>();
builder.Services.AddScoped<SilaboService>();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.Section));
builder.Services.AddScoped<IEnviadorCorreo, MailKitEnviadorCorreo>();
builder.Services.AddHttpClient<IContactoEstudiante, HttpContactoEstudiante>(h =>
{
    var url = builder.Configuration["Academic:BaseUrl"];
    h.BaseAddress = new Uri((string.IsNullOrWhiteSpace(url) ? "http://localhost:5002/" : url).TrimEnd('/') + "/");
    h.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<EnvioRecomendacionService>();

var app = builder.Build();

// Migraciones de EF Core con reintentos (SQL Server tarda en iniciar). Reemplaza a EnsureCreated del Sprint 1.
await DatabaseMigrator.MigrateWithRetryAsync<AnalyticsDbContext>(app.Services, app.Logger);

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
