// Result<T> -> IResult mapping (plan.md ADR-5). RFC 9457 ProblemDetails for every failure kind.
using Flowboard.Api.Domain;

namespace Flowboard.Api.Endpoints;

public static class ResultMapping
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Failure!.ToProblem();

    public static IResult ToHttpResult(this Result<Unit> result, Func<IResult>? onSuccess = null) =>
        result.IsSuccess ? onSuccess?.Invoke() ?? Results.NoContent() : result.Failure!.ToProblem();

    /// <summary>For validation short-circuits in a handler that never went through a Result&lt;T&gt;.</summary>
    public static IResult ToHttpResult(this Failure failure) => failure.ToProblem();

    private static IResult ToProblem(this Failure failure) => failure.Kind switch
    {
        FailureKind.Validation => Results.ValidationProblem(
            failure.Errors ?? new Dictionary<string, string[]>(),
            title: failure.Title),
        FailureKind.Unauthorized => Results.Problem(
            title: failure.Title, statusCode: StatusCodes.Status401Unauthorized),
        FailureKind.Forbidden => Results.Problem(
            title: failure.Title, statusCode: StatusCodes.Status403Forbidden),
        FailureKind.NotFound => Results.Problem(
            title: failure.Title, statusCode: StatusCodes.Status404NotFound),
        FailureKind.Conflict => Results.Problem(
            title: failure.Title, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(title: "Unexpected error", statusCode: StatusCodes.Status500InternalServerError),
    };
}
