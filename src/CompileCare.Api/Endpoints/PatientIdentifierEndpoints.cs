using CompileCare.Api.Security;
using CompileCare.Application.Patients;

namespace CompileCare.Api;

public static class PatientIdentifierEndpoints
{
    public static IEndpointRouteBuilder MapPatientIdentifierEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals/{hospitalId:guid}")
            .WithTags("Patient Identifiers")
            .WithMetadata(TenantScopedEndpointMetadata.Instance)
            .RequireAuthorization();

        group.MapPost("/patient-identifier-policy", async (
            Guid hospitalId,
            PatientIdentifierPolicyInput request,
            IPatientIdentifierService service,
            CancellationToken ct) =>
        {
            var result = await service.CreatePolicyAsync(hospitalId, request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy", result.Value)
                : Results.Problem(title: "MRN policy creation failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("CreateHospitalPatientIdentifierPolicy")
        .RequireAuthorization(AuthorizationPolicies.PatientIdentifierPolicyManage);

        group.MapGet("/patient-identifier-policy", async (
            Guid hospitalId,
            IPatientIdentifierService service,
            CancellationToken ct) =>
        {
            var result = await service.GetPolicyAsync(hospitalId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "MRN policy not found", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("GetHospitalPatientIdentifierPolicy")
        .RequireAuthorization(AuthorizationPolicies.PatientIdentifierPolicyRead);

        group.MapPost("/patients/{patientId:guid}/identifiers/mrn", async (
            Guid hospitalId,
            Guid patientId,
            IPatientIdentifierService service,
            CancellationToken ct) =>
        {
            var result = await service.AssignMedicalRecordNumberAsync(hospitalId, patientId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "MRN assignment failed", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("AssignHospitalMedicalRecordNumber")
        .RequireAuthorization(AuthorizationPolicies.PatientIdentifierAssign);

        group.MapGet("/patients/{patientId:guid}/identifiers", async (
            Guid hospitalId,
            Guid patientId,
            IPatientIdentifierService service,
            CancellationToken ct) =>
        {
            var result = await service.GetPatientIdentifiersAsync(hospitalId, patientId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Patient identifiers not found", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("GetHospitalPatientIdentifiers")
        .RequireAuthorization(AuthorizationPolicies.PatientIdentifierRead);

        group.MapGet("/patients/by-mrn/{mrn}", async (
            Guid hospitalId,
            string mrn,
            IPatientIdentifierService service,
            CancellationToken ct) =>
        {
            var result = await service.FindByMedicalRecordNumberAsync(hospitalId, mrn, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Patient not found", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("FindHospitalPatientByMrn")
        .RequireAuthorization(AuthorizationPolicies.PatientIdentifierRead);

        return endpoints;
    }
}
