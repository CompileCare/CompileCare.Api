using CompileCare.Api.Security;
using CompileCare.Application.Appointments;

namespace CompileCare.Api;

public static class AppointmentEndpoints
{
    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals/{hospitalId:guid}")
            .WithTags("Appointments and scheduling")
            .WithMetadata(TenantScopedEndpointMetadata.Instance)
            .RequireAuthorization();

        group.MapPost("/availability-sessions", async (
            Guid hospitalId,
            AvailabilitySessionCreateInput request,
            IAppointmentSchedulingService service,
            CancellationToken ct) =>
        {
            var result = await service.CreateAvailabilityAsync(hospitalId, request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{result.Value!.Id:D}", result.Value)
                : Results.Problem(title: "Availability session creation failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("CreateProviderAvailabilitySession")
        .RequireAuthorization(AuthorizationPolicies.AvailabilityManage);

        group.MapGet("/availability-sessions", async (
            Guid hospitalId,
            DateOnly date,
            IAppointmentSchedulingService service,
            CancellationToken ct,
            Guid? departmentId = null,
            int page = 1,
            int pageSize = 25) =>
        {
            var result = await service.ListAvailabilityAsync(hospitalId, date, departmentId, page, pageSize, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Availability search failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("ListProviderAvailabilitySessions")
        .RequireAuthorization(AuthorizationPolicies.AppointmentRead);

        group.MapGet("/availability-sessions/{sessionId:guid}/slots", async (
            Guid hospitalId,
            Guid sessionId,
            IAppointmentSchedulingService service,
            CancellationToken ct) =>
        {
            var result = await service.GetSlotsAsync(hospitalId, sessionId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Availability slots not found", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("GetProviderAvailabilitySlots")
        .RequireAuthorization(AuthorizationPolicies.AppointmentRead);

        group.MapPost("/appointments", async (
            Guid hospitalId,
            AppointmentBookInput request,
            IAppointmentSchedulingService service,
            CancellationToken ct) =>
        {
            var result = await service.BookAsync(hospitalId, request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/hospitals/{hospitalId:D}/appointments/{result.Value!.Id:D}", result.Value)
                : Results.Problem(title: "Appointment booking failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("BookAppointment")
        .RequireAuthorization(AuthorizationPolicies.AppointmentManage);

        group.MapGet("/appointments/{appointmentId:guid}", async (
            Guid hospitalId,
            Guid appointmentId,
            IAppointmentSchedulingService service,
            CancellationToken ct) =>
        {
            var result = await service.GetAppointmentAsync(hospitalId, appointmentId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Appointment not found", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("GetAppointment")
        .RequireAuthorization(AuthorizationPolicies.AppointmentRead);

        group.MapPost("/appointments/{appointmentId:guid}/cancel", async (
            Guid hospitalId,
            Guid appointmentId,
            IAppointmentSchedulingService service,
            CancellationToken ct) =>
        {
            var result = await service.CancelAsync(hospitalId, appointmentId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Appointment cancellation failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("CancelAppointment")
        .RequireAuthorization(AuthorizationPolicies.AppointmentManage);

        return endpoints;
    }
}
