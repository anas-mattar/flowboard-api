// Provider side of specs/002-auth-workspaces/contracts/board-membership-api.md.
using System.Security.Claims;
using Flowboard.Api.Domain;
using Flowboard.Api.Domain.Entities;
using Flowboard.Api.Services;

namespace Flowboard.Api.Endpoints;

public sealed record InviteRequestBody(string Email, string Role);

public static class BoardMembersEndpoints
{
    public static IEndpointRouteBuilder MapBoardMembersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var boards = endpoints.MapGroup("/v1/boards/{boardPublicId:guid}")
            .WithTags("BoardMembers")
            .RequireAuthorization();

        boards.MapGet("/members", ListMembers);
        boards.MapPost("/invitations", Invite);
        boards.MapDelete("/members/{userPublicId:guid}", RemoveMember);

        endpoints.MapDelete("/v1/invitations/{invitationPublicId:guid}", RevokeInvitation)
            .WithTags("BoardMembers")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> ListMembers(
        Guid boardPublicId, ClaimsPrincipal caller, IBoardMembershipService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.ListAsync(boardPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static async Task<IResult> Invite(
        Guid boardPublicId,
        InviteRequestBody body,
        ClaimsPrincipal caller,
        IBoardMembershipService service,
        CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var validationFailure = ValidateInvite(body, out var role);
        if (validationFailure is not null)
        {
            return validationFailure.ToHttpResult();
        }

        var result = await service.InviteAsync(boardPublicId, callerPublicId.Value, body.Email, role, cancellationToken);
        return result.ToHttpResult(outcome => outcome.Kind switch
        {
            InviteOutcomeKind.MemberAdded =>
                Results.Json(outcome.Member, statusCode: StatusCodes.Status201Created),
            InviteOutcomeKind.InvitationCreated =>
                Results.Json(outcome.PendingInvitation, statusCode: StatusCodes.Status201Created),
            InviteOutcomeKind.InvitationUpdated => Results.Ok(outcome.PendingInvitation),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        });
    }

    private static async Task<IResult> RevokeInvitation(
        Guid invitationPublicId, ClaimsPrincipal caller, IBoardMembershipService service, CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.RevokeInvitationAsync(invitationPublicId, callerPublicId.Value, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RemoveMember(
        Guid boardPublicId,
        Guid userPublicId,
        ClaimsPrincipal caller,
        IBoardMembershipService service,
        CancellationToken cancellationToken)
    {
        var callerPublicId = caller.GetUserPublicId();
        if (callerPublicId is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.RemoveMemberAsync(boardPublicId, callerPublicId.Value, userPublicId, cancellationToken);
        return result.ToHttpResult();
    }

    private static Failure? ValidateInvite(InviteRequestBody body, out BoardRole role)
    {
        role = default;
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(body.Email) || !body.Email.Contains('@') || body.Email.Length > 320)
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (!Enum.TryParse(body.Role, out BoardRole parsedRole) || !Enum.IsDefined(parsedRole))
        {
            errors["role"] = ["Role must be one of BoardAdmin, BoardMember, Observer."];
        }
        else
        {
            role = parsedRole;
        }

        return errors.Count == 0 ? null : Failure.Validation("Validation failed", errors);
    }
}
