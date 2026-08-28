// Provider side of specs/004-card-crud/contracts/card-crud-api.md.
using System.Security.Claims;
using System.Text.Json;
using Flowboard.Api.Domain;
using Flowboard.Api.Services;

namespace Flowboard.Api.Endpoints;

public sealed record CreateCardRequestBody(string Title);

public sealed record AddLabelRequestBody(Guid LabelPublicId);

public sealed record AddMemberRequestBody(Guid UserPublicId);

public sealed record AddChecklistItemRequestBody(string Text);

public sealed record UpdateChecklistItemRequestBody(bool Done);

public sealed record AddCommentRequestBody(string Body);

public sealed record MoveCardRequestBody(Guid ListPublicId, Guid? BeforeCardPublicId);

public static class CardsEndpoints
{
    private static readonly HashSet<string> KnownUpdateFields = ["title", "description", "dueAt", "dueComplete"];
    private static readonly HashSet<string> RejectedUpdateFields = ["list_id", "position"];

    public static IEndpointRouteBuilder MapCardsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/lists/{listPublicId:guid}/cards", CreateCard)
            .WithTags("Cards")
            .RequireAuthorization();

        var cards = endpoints.MapGroup("/v1/cards/{cardPublicId:guid}")
            .WithTags("Cards")
            .RequireAuthorization();

        cards.MapGet("/", GetCard);
        cards.MapPatch("/", UpdateCard);
        cards.MapDelete("/", DeleteCard);
        cards.MapPost("/labels", AddLabel);
        cards.MapDelete("/labels/{labelPublicId:guid}", RemoveLabel);
        cards.MapPost("/members", AddMember);
        cards.MapDelete("/members/{userPublicId:guid}", RemoveMember);
        cards.MapPost("/checklist-items", AddChecklistItem);
        cards.MapPost("/comments", AddComment);
        cards.MapGet("/activity", GetActivity);
        cards.MapPost("/copy", CopyCard);
        cards.MapPost("/move", MoveCard);

        var checklistItems = endpoints.MapGroup("/v1/checklist-items/{checklistItemPublicId:guid}")
            .WithTags("Cards")
            .RequireAuthorization();

        checklistItems.MapPatch("/", UpdateChecklistItem);
        checklistItems.MapDelete("/", DeleteChecklistItem);

        return endpoints;
    }

    private static async Task<IResult> CreateCard(
        Guid listPublicId,
        CreateCardRequestBody body,
        ClaimsPrincipal caller,
        ICardService service,
        CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateCardAsync(listPublicId, callerPublicId.Value, body.Title, cancellationToken);
        return result.ToHttpResult(card => Results.Json(card, statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> GetCard(
        Guid cardPublicId, ClaimsPrincipal caller, ICardService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetCardDetailAsync(cardPublicId, callerPublicId.Value, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToHttpResult();
        }

        httpContext.Response.Headers.ETag = ToETag(result.Value.RowVersion);
        return Results.Ok(result.Value.Detail);
    }

    private static async Task<IResult> UpdateCard(
        Guid cardPublicId,
        JsonElement body,
        ClaimsPrincipal caller,
        ICardService service,
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

        if (!TryParseETag(httpContext.Request.Headers.IfMatch.ToString(), out var rowVersion))
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["If-Match"] = ["A valid If-Match header is required."] }).ToHttpResult();
        }

        var result = await service.UpdateCardAsync(cardPublicId, callerPublicId.Value, command!, rowVersion, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToHttpResult();
        }

        httpContext.Response.Headers.ETag = ToETag(result.Value.RowVersion);
        return Results.Ok(result.Value.Detail);
    }

    private static async Task<IResult> DeleteCard(
        Guid cardPublicId, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeleteCardAsync(cardPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AddLabel(
        Guid cardPublicId, AddLabelRequestBody body, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.AddLabelAsync(cardPublicId, callerPublicId.Value, body.LabelPublicId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RemoveLabel(
        Guid cardPublicId, Guid labelPublicId, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.RemoveLabelAsync(cardPublicId, callerPublicId.Value, labelPublicId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AddMember(
        Guid cardPublicId, AddMemberRequestBody body, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.AddMemberAsync(cardPublicId, callerPublicId.Value, body.UserPublicId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RemoveMember(
        Guid cardPublicId, Guid userPublicId, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.RemoveMemberAsync(cardPublicId, callerPublicId.Value, userPublicId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AddChecklistItem(
        Guid cardPublicId, AddChecklistItemRequestBody body, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.AddChecklistItemAsync(cardPublicId, callerPublicId.Value, body.Text, cancellationToken);
        return result.ToHttpResult(item => Results.Json(item, statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> UpdateChecklistItem(
        Guid checklistItemPublicId, UpdateChecklistItemRequestBody body, ClaimsPrincipal caller, ICardService service,
        CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpdateChecklistItemAsync(checklistItemPublicId, callerPublicId.Value, body.Done, cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static async Task<IResult> DeleteChecklistItem(
        Guid checklistItemPublicId, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeleteChecklistItemAsync(checklistItemPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AddComment(
        Guid cardPublicId, AddCommentRequestBody body, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.AddCommentAsync(cardPublicId, callerPublicId.Value, body.Body, cancellationToken);
        return result.ToHttpResult(entry => Results.Json(entry, statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> GetActivity(
        Guid cardPublicId, string? cursor, int? limit, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        if (limit is < 1 or > 50)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["limit"] = ["limit must be between 1 and 50."] }).ToHttpResult();
        }

        var result = await service.GetActivityAsync(cardPublicId, callerPublicId.Value, cursor, limit ?? 20, cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static async Task<IResult> CopyCard(
        Guid cardPublicId, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.CopyCardAsync(cardPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult(card => Results.Json(card, statusCode: StatusCodes.Status201Created));
    }

    // contracts/move-api.md — POST /v1/cards/{cardPublicId}/move.
    private static async Task<IResult> MoveCard(
        Guid cardPublicId, MoveCardRequestBody body, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var command = new MoveCardCommand(body.ListPublicId, body.BeforeCardPublicId);
        var result = await service.MoveCardAsync(cardPublicId, callerPublicId.Value, command, cancellationToken);
        return result.ToHttpResult();
    }

    private static (UpdateCardCommand? Command, Failure? Failure) ParseUpdateCommand(JsonElement body)
    {
        var errors = new Dictionary<string, string[]>();
        bool hasTitle = false, hasDescription = false, hasDueAt = false, hasDueComplete = false;
        string? title = null;
        string? description = null;
        DateTime? dueAt = null;
        var dueComplete = false;

        if (body.ValueKind != JsonValueKind.Object)
        {
            return (null, Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["body"] = ["Request body must be a JSON object."] }));
        }

        foreach (var property in body.EnumerateObject())
        {
            if (RejectedUpdateFields.Contains(property.Name))
            {
                errors[property.Name] = ["This field cannot be changed via this endpoint."];
                continue;
            }

            if (!KnownUpdateFields.Contains(property.Name))
            {
                errors[property.Name] = ["Unknown field."];
                continue;
            }

            switch (property.Name)
            {
                case "title":
                    hasTitle = true;
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        title = property.Value.GetString();
                    }
                    else
                    {
                        errors["title"] = ["title must be a string."];
                    }

                    break;
                case "description":
                    hasDescription = true;
                    if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null)
                    {
                        description = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString();
                    }
                    else
                    {
                        errors["description"] = ["description must be a string or null."];
                    }

                    break;
                case "dueAt":
                    hasDueAt = true;
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        dueAt = null;
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetDateTime(out var parsedDueAt))
                    {
                        dueAt = parsedDueAt;
                    }
                    else
                    {
                        errors["dueAt"] = ["dueAt must be an ISO-8601 datetime or null."];
                    }

                    break;
                case "dueComplete":
                    hasDueComplete = true;
                    if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        dueComplete = property.Value.GetBoolean();
                    }
                    else
                    {
                        errors["dueComplete"] = ["dueComplete must be a boolean."];
                    }

                    break;
            }
        }

        if (!hasTitle && !hasDescription && !hasDueAt && !hasDueComplete && errors.Count == 0)
        {
            errors["body"] = ["At least one field must be supplied."];
        }

        if (errors.Count > 0)
        {
            return (null, Failure.Validation("Validation failed", errors));
        }

        return (new UpdateCardCommand(hasTitle, title, hasDescription, description, hasDueAt, dueAt, hasDueComplete, dueComplete), null);
    }

    private static string ToETag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    private static bool TryParseETag(string? etag, out byte[] rowVersion)
    {
        rowVersion = [];
        if (string.IsNullOrWhiteSpace(etag))
        {
            return false;
        }

        var trimmed = etag.Trim();
        if (trimmed.StartsWith("W/", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1];
        }

        try
        {
            rowVersion = Convert.FromBase64String(trimmed);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
