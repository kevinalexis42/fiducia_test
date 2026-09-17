using System.Text.Json;

using CoreFid.Auditoria.Api.Infraestructura;
using CoreFid.Auditoria.Application;
using CoreFid.Auditoria.Infrastructure;
using CoreFid.Auditoria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    .Enrich.WithProperty("service", "corefid-auditoria")
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSeguridadJwt(builder.Configuration);

builder.Services
    .AddControllers(options => options.SuppressAsyncSuffixInActionNames = false)
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ManejoErroresMiddleware.RespuestaModeloInvalido;
        options.SuppressMapClientErrors = true;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(SwaggerContrato.NombreDeEsquema);
    options.SchemaFilter<SwaggerContrato>();
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CORE-FID API - Módulo Auditoría",
        Version = "1.0",
        Description = "Microservicio de Auditoría. Respeta el contrato legacy vigente en producción (contrato/auditoria-legacy-v1.yaml)."
    });
    options.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearerAuth" } }] = Array.Empty<string>()
    });
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddNpgSql(
        connectionStringFactory: sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Auditoria")!,
        name: "postgres",
        tags: new[] { "ready" });

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrarAlIniciar"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AuditoriaDbContext>().Database.MigrateAsync();
}

app.UseMiddleware<ManejoErroresMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("traceId", System.Diagnostics.Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier);
        diagnosticContext.Set("usuario", httpContext.User.Identity?.Name);
    };
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Auditoría v1 (generado)");
    options.RoutePrefix = "swagger";
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/healthz", new() { Predicate = r => r.Tags.Contains("live") }).AllowAnonymous();
app.MapHealthChecks("/readyz", new() { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

app.Run();

public partial class Program;
