using CompileCare.Api;
using CompileCare.Api.Middleware;
using CompileCare.Api.Security;
using CompileCare.Application;
using CompileCare.Infrastructure;
using CompileCare.Observability.Extensions;
using CompileCare.Observability.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseCompileCareObservability();
builder.Services.AddObservability(builder.Configuration);
builder.Services.AddObservabilityHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCompileCareSecurity(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<AuditEventMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapPrometheusScrapingEndpoint();
app.MapGet("/", () => Results.Ok(new { service = "CompileCare.Api", status = "ready" }));
app.MapHealthEndpoints();
app.MapPlatformEndpoints();
app.MapPlatformTenantEndpoints();
app.MapTenantMembershipEndpoints();
app.MapHospitalEndpoints();
app.MapDepartmentEndpoints();
app.MapWardEndpoints();
app.MapRoomEndpoints();
app.MapBedEndpoints();
app.Run();

public partial class Program;
