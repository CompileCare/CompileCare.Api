using CompileCare.Application.Facilities;
using Microsoft.AspNetCore.Http;

namespace CompileCare.Api;

/// <summary>Maps facility application results to consistent HTTP responses.</summary>
internal static class FacilityHttp
{
    public static IResult From<T>(FacilityResult<T> result)
    {
        if (result.IsSuccess) return Results.Ok(result.Value);
        return result.StatusCode switch
        {
            400 => Results.BadRequest(new { message = result.Error }),
            404 => Results.NotFound(new { message = result.Error }),
            409 => Results.Conflict(new { message = result.Error }),
            _ => Results.Problem(statusCode: result.StatusCode, title: result.Error)
        };
    }

    public static IResult Created<T>(FacilityResult<T> result, string location)
    {
        if (!result.IsSuccess) return From(result);
        return Results.Created(location, result.Value);
    }

    public static IResult Deleted(FacilityResult<bool> result)
    {
        if (!result.IsSuccess) return From(result);
        return Results.NoContent();
    }
}
