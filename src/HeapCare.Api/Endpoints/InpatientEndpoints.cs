using System.Security.Claims;
using HeapCare.Api.Security;
using HeapCare.Application.Inpatient;

namespace HeapCare.Api;

public static class InpatientEndpoints
{
    public static IEndpointRouteBuilder MapInpatientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals/{hospitalId:guid}/ipd")
            .WithTags("IPD admissions")
            .WithMetadata(TenantScopedEndpointMetadata.Instance)
            .RequireAuthorization();

        group.MapPost("/admissions", async (
            Guid hospitalId,
            InpatientAdmissionInput request,
            ClaimsPrincipal user,
            IInpatientService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAdmissionAsync(
                hospitalId, request, GetActor(user), cancellationToken);
            return result.IsSuccess
                ? Results.Created(
                    $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{result.Value!.Id:D}",
                    result.Value)
                : Results.Problem(
                    title: "IPD admission creation failed",
                    detail: result.Error,
                    statusCode: result.StatusCode);
        })
        .WithName("CreateInpatientAdmission")
        .RequireAuthorization(AuthorizationPolicies.InpatientAdmit);

        group.MapGet("/admissions/{admissionId:guid}", async (
            Guid hospitalId,
            Guid admissionId,
            IInpatientService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(hospitalId, admissionId, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(
                    title: "IPD admission not found",
                    detail: result.Error,
                    statusCode: result.StatusCode);
        })
        .WithName("GetInpatientAdmission")
        .RequireAuthorization(AuthorizationPolicies.InpatientRead);

        group.MapPost("/admissions/{admissionId:guid}/bed-placement", async (
            Guid hospitalId,
            Guid admissionId,
            InpatientBedLocation request,
            ClaimsPrincipal user,
            IInpatientService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.AssignBedAsync(
                hospitalId, admissionId, request, GetActor(user), cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(
                    title: "IPD bed assignment failed",
                    detail: result.Error,
                    statusCode: result.StatusCode);
        })
        .WithName("AssignInpatientBed")
        .RequireAuthorization(AuthorizationPolicies.InpatientBedAssign);

        group.MapPost("/admissions/{admissionId:guid}/bed-transfer", async (
            Guid hospitalId,
            Guid admissionId,
            InpatientBedTransferInput request,
            ClaimsPrincipal user,
            IInpatientService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.TransferBedAsync(
                hospitalId, admissionId, request, GetActor(user), cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(
                    title: "IPD bed transfer failed",
                    detail: result.Error,
                    statusCode: result.StatusCode);
        })
        .WithName("TransferInpatientBed")
        .RequireAuthorization(AuthorizationPolicies.InpatientBedTransfer);

        return endpoints;
    }

    private static InpatientActor GetActor(ClaimsPrincipal user) =>
        new(
            user.FindFirst(AuthorizationPolicies.ValidatedIssuerClaim)?.Value ?? string.Empty,
            user.FindFirst("sub")?.Value ?? string.Empty);
}
