// Provider side of specs/003-board-view-readonly/contracts/board-content-api.md.
using System.Security.Claims;
using Flowboard.Api.Domain;
using Flowboard.Api.Services;

namespace Flowboard.Api.Endpoints;

public static class BoardsEndpoints
{
    public static IEndpointRouteBuilder MapBoardsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var boards = endpoints.MapGroup("/v1/boards")
            .WithTags("Boards")
            .RequireAuthorization();

        boards.MapGet("/", ListBoards);
        boards.MapGet("/{boardPublicId:guid}", GetBoardContent);

        return endpoints;
    }

    private static async Task<IResult> ListBoards(
        string? cursor,
        int? limit,
        ClaimsPrincipal caller,
        IBoardContentService service,
        CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        if (limit is < 1 or > 50)
        {
            return Failure.Validation(
                "Validation failed",
                new Dictionary<string, string[]> { ["limit"] = ["limit must be between 1 and 50."] }).ToHttpResult();
        }

        var result = await service.ListBoardsAsync(callerPublicId.Value, cursor, limit ?? 20, cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static async Task<IResult> GetBoardContent(
        Guid boardPublicId, ClaimsPrincipal caller, IBoardContentService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetBoardContentAsync(boardPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
