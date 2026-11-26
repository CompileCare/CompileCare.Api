using CompileCare.Api;
using CompileCare.Api.Endpoints;
using CompileCare.Api.Middleware;
using CompileCare.Api.Security;
using CompileCare.Application;
using CompileCare.Infrastructure;
using CompileCare.Infrastructure.Persistence;
using CompileCare.Observability.Extensions;
using CompileCare.Observability.AspNetCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseCompileCareObservability();
builder.Services.AddObservability(builder.Configuration);
// PostgreSQL data-access spans join the same trace provider/export pipeline.
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddNpgsql());
builder.Services.AddObservabilityHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCompileCareSecurity(builder.Configuration, builder.Environment);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
        options.AddPolicy("CompileCareWeb", policy =>
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

// Local development can opt into migrations explicitly. This is deliberately
// disabled by default and never runs in Staging or Production.
if (app.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
    await database.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRouting();
if (allowedOrigins.Length > 0) app.UseCors("CompileCareWeb");
app.UseAuthentication();
app.UseMiddleware<AuditEventMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapPrometheusScrapingEndpoint();
app.MapGet("/", () => Results.Ok(new { service = "CompileCare.Api", status = "ready" }));
app.MapHealthEndpoints();
app.MapPlatformEndpoints();
app.MapAuditEndpoints();
app.MapPlatformBootstrapEndpoints(app.Environment);
app.MapPlatformTenantEndpoints();
app.MapTenantMembershipEndpoints();
app.MapUserContextEndpoints();
app.MapHospitalEndpoints();
app.MapDepartmentEndpoints();
app.MapWardEndpoints();
app.MapRoomEndpoints();
app.MapBedEndpoints();
app.MapPatientEndpoints();
app.MapStaffEndpoints();
app.MapPatientIdentifierEndpoints();
app.MapAppointmentEndpoints();
app.MapOpdEncounterEndpoints();
app.MapBillingEndpoints();
app.Run();

public partial class Program;
