using EduMonitor.Analytics.Api.Data;
using EduMonitor.Common;
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
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
