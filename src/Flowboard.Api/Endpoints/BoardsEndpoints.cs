// Provider side of specs/003-board-view-readonly/contracts/board-content-api.md and
// specs/006-board-list-management/contracts/board-list-management-api.md.
using System.Security.Claims;
using Flowboard.Api.Domain;
using Flowboard.Api.Services;

namespace Flowboard.Api.Endpoints;

public sealed record CreateBoardRequestBody(string Name);

public sealed record UpdateBoardRequestBody(string Name);

public sealed record CreateListRequestBody(string Name);

public static class BoardsEndpoints
{
    public static IEndpointRouteBuilder MapBoardsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var boards = endpoints.MapGroup("/v1/boards")
            .WithTags("Boards")
            .RequireAuthorization();

        boards.MapGet("/", ListBoards);
        boards.MapPost("/", CreateBoard);
        boards.MapGet("/{boardPublicId:guid}", GetBoardContent);
        boards.MapPatch("/{boardPublicId:guid}", UpdateBoard);
        boards.MapDelete("/{boardPublicId:guid}", DeleteBoard);
        boards.MapPost("/{boardPublicId:guid}/star", StarBoard);
        boards.MapPost("/{boardPublicId:guid}/unstar", UnstarBoard);
        boards.MapPost("/{boardPublicId:guid}/lists", CreateList);

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

    private static async Task<IResult> CreateBoard(
        CreateBoardRequestBody body, ClaimsPrincipal caller, IBoardContentService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateBoardAsync(callerPublicId.Value, body.Name, cancellationToken);
        return result.ToHttpResult(board => Results.Json(board, statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> UpdateBoard(
        Guid boardPublicId,
        UpdateBoardRequestBody body,
        ClaimsPrincipal caller,
        IBoardContentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        if (!ETagHeader.TryParse(httpContext.Request.Headers.IfMatch.ToString(), out var rowVersion))
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["If-Match"] = ["A valid If-Match header is required."] }).ToHttpResult();
        }

        var result = await service.UpdateBoardAsync(boardPublicId, callerPublicId.Value, body.Name, rowVersion, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToHttpResult();
        }

        httpContext.Response.Headers.ETag = ETagHeader.ToETag(result.Value.RowVersion);
        return Results.Ok(new { name = result.Value.Name, rowVersion = Convert.ToBase64String(result.Value.RowVersion) });
    }

    private static async Task<IResult> StarBoard(
        Guid boardPublicId, ClaimsPrincipal caller, IBoardContentService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.StarBoardAsync(boardPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UnstarBoard(
        Guid boardPublicId, ClaimsPrincipal caller, IBoardContentService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.UnstarBoardAsync(boardPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteBoard(
        Guid boardPublicId, ClaimsPrincipal caller, IBoardContentService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeleteBoardAsync(boardPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateList(
        Guid boardPublicId, CreateListRequestBody body, ClaimsPrincipal caller, IListService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateListAsync(boardPublicId, callerPublicId.Value, body.Name, cancellationToken);
        return result.ToHttpResult(list => Results.Json(list, statusCode: StatusCodes.Status201Created));
    }
}
