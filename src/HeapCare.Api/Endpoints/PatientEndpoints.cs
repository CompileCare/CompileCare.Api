using HeapCare.Api.Security;
using HeapCare.Application.Patients;

namespace HeapCare.Api;

public static class PatientEndpoints
{
    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals/{hospitalId:guid}/patients")
            .WithTags("Patients")
            .WithMetadata(TenantScopedEndpointMetadata.Instance)
            .RequireAuthorization();

        group.MapPost("/", async (
            Guid hospitalId,
            PatientRegistrationInput request,
            IPatientService service,
            CancellationToken ct) =>
        {
            var result = await service.RegisterAsync(hospitalId, request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/hospitals/{hospitalId:D}/patients/{result.Value!.Id:D}", result.Value)
                : Results.Problem(title: "Patient registration failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("RegisterPatient")
        .RequireAuthorization(AuthorizationPolicies.PatientRegister);

        group.MapPost("/duplicate-candidates", async (
            Guid hospitalId,
            PatientDuplicateCheckRequest request,
            IPatientDuplicateDetectionService service,
            CancellationToken ct) =>
        {
            var result = await service.FindCandidatesAsync(hospitalId, request, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Patient duplicate check failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("FindPatientDuplicateCandidates")
        .RequireAuthorization(AuthorizationPolicies.PatientDuplicateReview);

        group.MapGet("/", async (
            Guid hospitalId,
            IPatientService service,
            CancellationToken ct,
            string? query = null,
            int page = 1,
            int pageSize = 25) =>
        {
            var result = await service.SearchAsync(hospitalId, new(query, page, pageSize), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Patient search failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("SearchHospitalPatients")
        .RequireAuthorization(AuthorizationPolicies.PatientRead);

        group.MapGet("/{patientId:guid}", async (
            Guid hospitalId,
            Guid patientId,
            IPatientService service,
            CancellationToken ct) =>
        {
            var result = await service.GetAsync(hospitalId, patientId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Patient not found", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("GetHospitalPatient")
        .RequireAuthorization(AuthorizationPolicies.PatientIdentityRead);

        group.MapGet("/departments/{departmentId:guid}/patients/{patientId:guid}", async (
            Guid hospitalId,
            Guid departmentId,
            Guid patientId,
            IPatientService service,
            CancellationToken ct) =>
        {
            var result = await service.GetDepartmentAsync(hospitalId, departmentId, patientId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Department patient not found", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("GetDepartmentPatient")
        .RequireAuthorization(AuthorizationPolicies.PatientIdentityRead);

        group.MapGet("/departments/{departmentId:guid}/patients", async (
            Guid hospitalId,
            Guid departmentId,
            IPatientService service,
            CancellationToken ct,
            string? query = null,
            int page = 1,
            int pageSize = 25) =>
        {
            var result = await service.SearchDepartmentAsync(
                hospitalId, departmentId, new(query, page, pageSize), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Department patient search failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("SearchDepartmentPatients")
        .RequireAuthorization(AuthorizationPolicies.PatientIdentityRead);

        return endpoints;
    }
}
