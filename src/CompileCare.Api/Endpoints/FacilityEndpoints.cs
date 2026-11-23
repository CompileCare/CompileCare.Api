using CompileCare.Application.Facilities;

namespace CompileCare.Api;

public static class HospitalEndpoints
{
    public static IEndpointRouteBuilder MapHospitalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals").WithTags("Hospitals");

        group.MapGet("/", async (
            IHospitalService service,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
                Results.Ok(await service.ListHospitals(new(page, pageSize), ct)))
            .WithName("ListHospitals");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IHospitalService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.GetHospital(id, ct)))
            .WithName("GetHospital");

        group.MapPost("/", async (
            HospitalRequest request,
            IHospitalService service,
            CancellationToken ct) =>
                FacilityHttp.Created(
                    await service.CreateHospital(new(request.Name, request.Code), ct),
                    "/api/v1/hospitals",
                    hospital => hospital.Id))
            .WithName("CreateHospital");

        group.MapPut("/{id:guid}", async (
            Guid id,
            HospitalRequest request,
            IHospitalService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.UpdateHospital(id, new(request.Name, request.Code), ct)))
            .WithName("UpdateHospital");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IHospitalService service,
            CancellationToken ct) =>
                FacilityHttp.Deleted(await service.DeleteHospital(id, ct)))
            .WithName("DeleteHospital");

        return endpoints;
    }
}

public sealed record HospitalRequest(string? Name, string? Code);
