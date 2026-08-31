// Provider side of specs/009-card-attachments/contracts/attachments-api.md.
using System.Security.Claims;
using Flowboard.Api.Domain;
using Flowboard.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Flowboard.Api.Endpoints;

public static class AttachmentsEndpoints
{
    // research.md R-4: a server-level backstop above the 25 MB product cap (CardService's
    // AddAttachmentAsync) — an oversized upload is rejected with the same 400 either way,
    // this limit exists only so a caller cannot stream an unbounded body at the server.
    private const long AbuseBackstopBytes = 30 * 1024 * 1024;

    public static IEndpointRouteBuilder MapAttachmentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/cards/{cardPublicId:guid}/attachments", UploadAttachment)
            .WithTags("Attachments")
            .RequireAuthorization()
            // This API is bearer-token authenticated with no cookies (auth-api.md) — the CSRF
            // threat model antiforgery tokens defend against does not apply, and no antiforgery
            // middleware/services are registered anywhere in this project.
            .DisableAntiforgery()
            .AddEndpointFilter(async (context, next) =>
            {
                var bodySizeFeature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (bodySizeFeature is not null && !bodySizeFeature.IsReadOnly)
                {
                    bodySizeFeature.MaxRequestBodySize = AbuseBackstopBytes;
                }

                return await next(context);
            });

        endpoints.MapGet("/v1/attachments/{attachmentPublicId:guid}/content", GetAttachmentContent)
            .WithTags("Attachments")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> UploadAttachment(
        Guid cardPublicId, [FromForm] IFormFile? file, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        if (file is null || file.Length == 0)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["file"] = ["A file is required."] }).ToHttpResult();
        }

        await using var content = file.OpenReadStream();
        var result = await service.AddAttachmentAsync(
            cardPublicId, callerPublicId.Value, file.FileName, file.ContentType, file.Length, content, cancellationToken);
        return result.ToHttpResult(attachment => Results.Json(attachment, statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> GetAttachmentContent(
        Guid attachmentPublicId, ClaimsPrincipal caller, ICardService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetAttachmentContentAsync(attachmentPublicId, callerPublicId.Value, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToHttpResult();
        }

        return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
