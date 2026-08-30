// Provider side of specs/005-drag-drop-ordering/contracts/move-api.md and
// specs/006-board-list-management/contracts/board-list-management-api.md.
using System.Security.Claims;
using System.Text.Json;
using Flowboard.Api.Domain;
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
        lists.MapPatch("/", UpdateList);
        lists.MapPost("/sort", SortByDueDate);
        lists.MapPost("/archive-cards", ArchiveAllCards);
        lists.MapDelete("/", DeleteList);

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

    private static async Task<IResult> UpdateList(
        Guid listPublicId,
        JsonElement body,
        ClaimsPrincipal caller,
        IListService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var (command, parseFailure) = ParseUpdateCommand(body);
        if (parseFailure is not null)
        {
            return parseFailure.ToHttpResult();
        }

        if (!ETagHeader.TryParse(httpContext.Request.Headers.IfMatch.ToString(), out var rowVersion))
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["If-Match"] = ["A valid If-Match header is required."] }).ToHttpResult();
        }

        var result = await service.UpdateListAsync(listPublicId, callerPublicId.Value, command!, rowVersion, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToHttpResult();
        }

        httpContext.Response.Headers.ETag = ETagHeader.ToETag(result.Value.RowVersion);
        return Results.Ok(new
        {
            name = result.Value.Name,
            wipLimit = result.Value.WipLimit,
            rowVersion = Convert.ToBase64String(result.Value.RowVersion),
        });
    }

    private static async Task<IResult> SortByDueDate(
        Guid listPublicId, ClaimsPrincipal caller, IListService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.SortByDueDateAsync(listPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ArchiveAllCards(
        Guid listPublicId, ClaimsPrincipal caller, IListService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.ArchiveAllCardsAsync(listPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteList(
        Guid listPublicId, ClaimsPrincipal caller, IListService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeleteListAsync(listPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult();
    }

    private static (UpdateListCommand? Command, Failure? Failure) ParseUpdateCommand(JsonElement body)
    {
        var errors = new Dictionary<string, string[]>();
        bool hasName = false, hasWipLimit = false;
        string? name = null;
        int? wipLimit = null;

        if (body.ValueKind != JsonValueKind.Object)
        {
            return (null, Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["body"] = ["Request body must be a JSON object."] }));
        }

        foreach (var property in body.EnumerateObject())
        {
            switch (property.Name)
            {
                case "name":
                    hasName = true;
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        name = property.Value.GetString();
                    }
                    else
                    {
                        errors["name"] = ["name must be a string."];
                    }

                    break;
                case "wipLimit":
                    hasWipLimit = true;
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        wipLimit = null;
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var parsedWipLimit))
                    {
                        wipLimit = parsedWipLimit;
                    }
                    else
                    {
                        errors["wipLimit"] = ["wipLimit must be an integer or null."];
                    }

                    break;
                default:
                    errors[property.Name] = ["Unknown field."];
                    break;
            }
        }

        if (errors.Count > 0)
        {
            return (null, Failure.Validation("Validation failed", errors));
        }

        return (new UpdateListCommand(hasName, name, hasWipLimit, wipLimit), null);
    }
}
