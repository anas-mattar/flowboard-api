// Provider side of specs/005-drag-drop-ordering/contracts/move-api.md.
using System.Security.Claims;
using Flowboard.Api.Services;

namespace Flowboard.Api.Endpoints;

public sealed record MoveListRequestBody(Guid? BeforeListPublicId);

public static class ListsEndpoints
{
    public static IEndpointRouteBuilder MapListsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var lists = endpoints.MapGroup("/v1/lists/{listPublicId:guid}")
            .WithTags("Lists")
            .RequireAuthorization();

        lists.MapPost("/move", MoveList);

        return endpoints;
    }

    private static async Task<IResult> MoveList(
        Guid listPublicId, MoveListRequestBody body, ClaimsPrincipal caller, IListService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var command = new MoveListCommand(body.BeforeListPublicId);
        var result = await service.MoveListAsync(listPublicId, callerPublicId.Value, command, cancellationToken);
        return result.ToHttpResult();
    }
}
