using System.Security.Claims;
using CompileCare.Api.Security;
using CompileCare.Application.Clinical;

namespace CompileCare.Api;

public static class OpdEncounterEndpoints
{
    public static IEndpointRouteBuilder MapOpdEncounterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals/{hospitalId:guid}/opd")
            .WithTags("OPD encounters")
            .WithMetadata(TenantScopedEndpointMetadata.Instance)
            .RequireAuthorization();

        group.MapPost("/queue/{queueEntryId:guid}/encounter", async (
            Guid hospitalId,
            Guid queueEntryId,
            ClaimsPrincipal user,
            IOpdEncounterService service,
            CancellationToken ct) =>
        {
            var result = await service.CreateFromQueueAsync(hospitalId, queueEntryId, GetActor(user), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{result.Value!.Id:D}", result.Value)
                : Results.Problem(title: "OPD encounter creation failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("CreateOpdEncounterFromQueue")
        .RequireAuthorization(AuthorizationPolicies.EncounterManage);

        group.MapGet("/queue/{queueEntryId:guid}/encounter", async (
            Guid hospitalId,
            Guid queueEntryId,
            IOpdEncounterService service,
            CancellationToken ct) =>
        {
            var result = await service.GetByQueueEntryAsync(hospitalId, queueEntryId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "OPD encounter not found", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("GetOpdEncounterByQueueEntry")
        .RequireAuthorization(AuthorizationPolicies.EncounterRead);

        group.MapGet("/encounters/{encounterId:guid}", async (
            Guid hospitalId,
            Guid encounterId,
            IOpdEncounterService service,
            CancellationToken ct) =>
        {
            var result = await service.GetAsync(hospitalId, encounterId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "OPD encounter not found", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("GetOpdEncounter")
        .RequireAuthorization(AuthorizationPolicies.EncounterRead);

        group.MapPut("/encounters/{encounterId:guid}/note", async (
            Guid hospitalId,
            Guid encounterId,
            OpdEncounterNoteInput request,
            ClaimsPrincipal user,
            IOpdEncounterService service,
            CancellationToken ct) =>
        {
            var result = await service.UpdateNoteAsync(hospitalId, encounterId, request, GetActor(user), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "OPD note update failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("UpdateOpdEncounterNote")
        .RequireAuthorization(AuthorizationPolicies.EncounterManage);

        group.MapPost("/encounters/{encounterId:guid}/sign", async (
            Guid hospitalId,
            Guid encounterId,
            ClaimsPrincipal user,
            IOpdEncounterService service,
            CancellationToken ct) =>
        {
            var result = await service.SignAsync(hospitalId, encounterId, GetActor(user), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "OPD encounter signing failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("SignOpdEncounter")
        .RequireAuthorization(AuthorizationPolicies.EncounterSign);

        group.MapPost("/encounters/{encounterId:guid}/amendments", async (
            Guid hospitalId,
            Guid encounterId,
            OpdEncounterAmendmentInput request,
            ClaimsPrincipal user,
            IOpdEncounterService service,
            CancellationToken ct) =>
        {
            var result = await service.AddAmendmentAsync(hospitalId, encounterId, request, GetActor(user), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "OPD encounter amendment failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("AddOpdEncounterAmendment")
        .RequireAuthorization(AuthorizationPolicies.EncounterAmend);

        group.MapPost("/encounters/{encounterId:guid}/close", async (
            Guid hospitalId,
            Guid encounterId,
            IOpdEncounterService service,
            CancellationToken ct) =>
        {
            var result = await service.CloseAsync(hospitalId, encounterId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "OPD encounter closure failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("CloseOpdEncounter")
        .RequireAuthorization(AuthorizationPolicies.EncounterManage);

        group.MapPost("/encounters/{encounterId:guid}/cancel", async (
            Guid hospitalId,
            Guid encounterId,
            OpdEncounterCancelInput request,
            IOpdEncounterService service,
            CancellationToken ct) =>
        {
            var result = await service.CancelAsync(hospitalId, encounterId, request.Reason, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "OPD encounter cancellation failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("CancelOpdEncounter")
        .RequireAuthorization(AuthorizationPolicies.EncounterManage);

        return endpoints;
    }

    private static OpdEncounterActor GetActor(ClaimsPrincipal user) =>
        new(
            user.FindFirst(AuthorizationPolicies.ValidatedIssuerClaim)?.Value ?? string.Empty,
            user.FindFirst("sub")?.Value ?? string.Empty);
}
