using CompileCare.Application.Facilities;
using Microsoft.AspNetCore.Http;

namespace CompileCare.Api;

/// <summary>Maps facility application results to consistent HTTP responses.</summary>
internal static class FacilityHttp
{
    public static IResult From<T>(FacilityResult<T> result)
    {
        if (result.IsSuccess)
            return Results.Ok(result.Value);

        return result.StatusCode switch
        {
            StatusCodes.Status400BadRequest => Results.BadRequest(new { message = result.Error }),
            StatusCodes.Status404NotFound => Results.NotFound(new { message = result.Error }),
            StatusCodes.Status409Conflict => Results.Conflict(new { message = result.Error }),
            _ => Results.Problem(statusCode: result.StatusCode, title: result.Error)
        };
    }

    public static IResult Created<T>(
        FacilityResult<T> result,
        string collectionLocation,
        Func<T, Guid> resourceIdSelector)
    {
        if (!result.IsSuccess)
            return From(result);

        if (result.Value is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The facility service returned no resource after a successful create.");
        }

        var resourceId = resourceIdSelector(result.Value);
        var resourceLocation = $"{collectionLocation.TrimEnd('/')}/{resourceId:D}";
        return Results.Created(resourceLocation, result.Value);
    }

    public static IResult Deleted(FacilityResult<bool> result)
    {
        if (!result.IsSuccess)
            return From(result);

        return Results.NoContent();
    }
}
