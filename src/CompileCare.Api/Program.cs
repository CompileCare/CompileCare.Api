using CompileCare.Api;
using CompileCare.Api.Middleware;
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

var app = builder.Build();
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapPrometheusScrapingEndpoint();
app.MapGet("/", () => Results.Ok(new { service = "CompileCare.Api", status = "ready" }));
app.MapHealthEndpoints();
app.MapPlatformEndpoints();
app.MapHospitalEndpoints();
app.MapDepartmentEndpoints();
app.Run();

public partial class Program;