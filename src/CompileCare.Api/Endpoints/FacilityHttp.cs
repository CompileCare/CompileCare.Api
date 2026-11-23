using CompileCare.Application.Facilities;
namespace CompileCare.Api;
internal static class FacilityHttp
{
    public static IResult From<T>(FacilityResult<T> result) => result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(statusCode: result.StatusCode, title: result.Error);
    public static IResult Created<T>(FacilityResult<T> result, string location) => result.IsSuccess
        ? Results.Created(location, result.Value)
        : From(result);
    public static IResult Deleted(FacilityResult<bool> result) => result.IsSuccess
        ? Results.NoContent()
        : From(result);
}