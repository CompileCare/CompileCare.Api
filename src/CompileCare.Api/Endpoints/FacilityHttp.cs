using CompileCare.Application.Facilities;
using Microsoft.AspNetCore.Http;

namespace CompileCare.Api;

/// <summary>Maps application results to the platform's RFC 9457 HTTP error contract.</summary>
internal static class FacilityHttp
{
    public static IResult From<T>(FacilityResult<T> result)
    {
        if (result.IsSuccess)
            return Results.Ok(result.Value);

        var title = result.StatusCode switch
        {
            StatusCodes.Status400BadRequest => "Validation failed",
            StatusCodes.Status404NotFound => "Resource not found",
            StatusCodes.Status409Conflict => "Resource conflict",
            StatusCodes.Status401Unauthorized => "Authentication required",
            StatusCodes.Status403Forbidden => "Access denied",
            _ => "The request could not be completed"
        };

        return Results.Problem(
            statusCode: result.StatusCode,
            title: title,
            detail: result.Error);
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
