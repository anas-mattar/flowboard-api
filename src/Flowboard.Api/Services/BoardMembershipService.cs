// Provider side of specs/002-auth-workspaces/contracts/board-membership-api.md.
using Flowboard.Api.Data;
using Flowboard.Api.Domain;
using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Services;

public sealed record MemberUserDto(Guid PublicId, string DisplayName, string Initials, string AvatarColor);

public sealed record MemberDto(MemberUserDto User, string Role, bool IsWorkspaceOwner);

public sealed record PendingInvitationDto(Guid PublicId, string Email, string Role, string InvitedBy);

public sealed record MembersResponse(
    IReadOnlyList<MemberDto> Members, IReadOnlyList<PendingInvitationDto> PendingInvitations);

public enum InviteOutcomeKind
{
    MemberAdded,
    InvitationCreated,
    InvitationUpdated,
}

public sealed record InviteOutcome(InviteOutcomeKind Kind, MemberDto? Member, PendingInvitationDto? PendingInvitation);

public interface IBoardMembershipService
{
    Task<Result<MembersResponse>> ListAsync(
        Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    Task<Result<InviteOutcome>> InviteAsync(
        Guid boardPublicId, Guid callerPublicId, string email, BoardRole role, CancellationToken cancellationToken);

    Task<Result<Unit>> RevokeInvitationAsync(
        Guid invitationPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    Task<Result<Unit>> RemoveMemberAsync(
        Guid boardPublicId, Guid callerPublicId, Guid memberUserPublicId, CancellationToken cancellationToken);
}

public sealed class BoardMembershipService(FlowboardDbContext db, IBoardAccessService boardAccess, IBoardEventPublisher realtime)
    : IBoardMembershipService
{
    public async Task<Result<MembersResponse>> ListAsync(
        Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var access = await boardAccess.ResolveAsync(boardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }

        // Any role may view (contract: "View board" is checked for every role).
        var owner = await db.Boards
            .AsNoTracking()
            .Where(b => b.Id == access.BoardId)
            .Select(b => new MemberUserDto(
                b.Workspace.Owner.PublicId, b.Workspace.Owner.DisplayName,
                b.Workspace.Owner.Initials, b.Workspace.Owner.AvatarColor))
            .FirstAsync(cancellationToken);

        var members = new List<MemberDto> { new(owner, BoardRole.BoardAdmin.ToString(), IsWorkspaceOwner: true) };
        members.AddRange(await db.BoardMembers
            .AsNoTracking()
            .Where(m => m.BoardId == access.BoardId)
            .Select(m => new MemberDto(
                new MemberUserDto(m.User.PublicId, m.User.DisplayName, m.User.Initials, m.User.AvatarColor),
                m.Role.ToString(),
                false))
            .ToListAsync(cancellationToken));

        var pending = await db.Invitations
            .AsNoTracking()
            .Where(i => i.BoardId == access.BoardId && i.Status == InvitationStatus.Pending)
            .Select(i => new PendingInvitationDto(i.PublicId, i.Email, i.Role.ToString(), i.InvitedBy.DisplayName))
            .ToListAsync(cancellationToken);

        return Result<MembersResponse>.Success(new MembersResponse(members, pending));
    }

    public async Task<Result<InviteOutcome>> InviteAsync(
        Guid boardPublicId, Guid callerPublicId, string email, BoardRole role, CancellationToken cancellationToken)
    {
        var access = await boardAccess.ResolveAsync(boardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }

        // FR-014: only BoardAdmin (including the implicit workspace-owner admin) may invite.
        if (access.Role != BoardRole.BoardAdmin)
        {
            return Failure.Forbidden();
        }

        var normalizedEmail = email.Trim();
        var now = DateTime.UtcNow;

        var ownerPublicId = await db.Boards
            .AsNoTracking()
            .Where(b => b.Id == access.BoardId)
            .Select(b => b.Workspace.Owner.PublicId)
            .FirstAsync(cancellationToken);

        var invitee = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (invitee is not null)
        {
            // FR-009: an existing BoardMember row, or the workspace owner's implicit
            // membership, is a conflict — not a re-invite.
            var alreadyMember = invitee.PublicId == ownerPublicId
                || await db.BoardMembers.AnyAsync(
                    m => m.BoardId == access.BoardId && m.UserId == invitee.Id, cancellationToken);
            if (alreadyMember)
            {
                return Failure.Conflict("This person is already a member of this board");
            }

            var member = new BoardMember
            {
                BoardId = access.BoardId,
                User = invitee,
                Role = role,
                CreatedDate = now,
                // data-model.md: the inviter's PublicId (second-model review B3 — was
                // hardcoded "SYSTEM", discarding who actually granted this access).
                CreatedBy = callerPublicId.ToString(),
            };
            db.BoardMembers.Add(member);
            await db.SaveChangesAsync(cancellationToken);

            var memberDto = new MemberDto(
                new MemberUserDto(invitee.PublicId, invitee.DisplayName, invitee.Initials, invitee.AvatarColor),
                role.ToString(),
                IsWorkspaceOwner: false);
            return Result<InviteOutcome>.Success(new InviteOutcome(InviteOutcomeKind.MemberAdded, memberDto, null));
        }

        var caller = await db.Users.FirstAsync(u => u.PublicId == callerPublicId, cancellationToken);

        // data-model.md's filtered unique index (BoardId, Email) WHERE Status='Pending':
        // re-inviting the same pending email updates its role in place instead of conflicting.
        var existingPending = await db.Invitations.FirstOrDefaultAsync(
            i => i.BoardId == access.BoardId && i.Email == normalizedEmail && i.Status == InvitationStatus.Pending,
            cancellationToken);

        if (existingPending is not null)
        {
            existingPending.Role = role;
            existingPending.UpdatedDate = now;
            existingPending.UpdatedBy = callerPublicId.ToString();
            await db.SaveChangesAsync(cancellationToken);

            var updatedDto = new PendingInvitationDto(
                existingPending.PublicId, existingPending.Email, role.ToString(), caller.DisplayName);
            return Result<InviteOutcome>.Success(
                new InviteOutcome(InviteOutcomeKind.InvitationUpdated, null, updatedDto));
        }

        var invitation = new Invitation
        {
            PublicId = Guid.NewGuid(),
            BoardId = access.BoardId,
            Email = normalizedEmail,
            Role = role,
            InvitedBy = caller,
            Status = InvitationStatus.Pending,
            CreatedDate = now,
            CreatedBy = callerPublicId.ToString(),
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        var createdDto = new PendingInvitationDto(invitation.PublicId, invitation.Email, role.ToString(), caller.DisplayName);
        return Result<InviteOutcome>.Success(new InviteOutcome(InviteOutcomeKind.InvitationCreated, null, createdDto));
    }

    public async Task<Result<Unit>> RevokeInvitationAsync(
        Guid invitationPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(
            i => i.PublicId == invitationPublicId && i.Status == InvitationStatus.Pending, cancellationToken);
        if (invitation is null)
        {
            return Failure.NotFound();
        }

        var boardPublicId = await db.Boards
            .AsNoTracking()
            .Where(b => b.Id == invitation.BoardId)
            .Select(b => b.PublicId)
            .FirstAsync(cancellationToken);

        var access = await boardAccess.ResolveAsync(boardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }

        if (access.Role != BoardRole.BoardAdmin)
        {
            return Failure.Forbidden();
        }

        invitation.Status = InvitationStatus.Revoked;
        invitation.UpdatedDate = DateTime.UtcNow;
        invitation.UpdatedBy = callerPublicId.ToString();
        await db.SaveChangesAsync(cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<Unit>> RemoveMemberAsync(
        Guid boardPublicId, Guid callerPublicId, Guid memberUserPublicId, CancellationToken cancellationToken)
    {
        var access = await boardAccess.ResolveAsync(boardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }

        if (access.Role != BoardRole.BoardAdmin)
        {
            return Failure.Forbidden();
        }

        // The workspace owner's implicit access has no BoardMember row to remove (contract).
        var member = await db.BoardMembers.FirstOrDefaultAsync(
            m => m.BoardId == access.BoardId && m.User.PublicId == memberUserPublicId, cancellationToken);
        if (member is null)
        {
            return Failure.NotFound();
        }

        db.BoardMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);

        // research.md R-7, ADR-38: evict the removed member's live connections to this
        // board immediately, rather than relying solely on their realtime token's 2-minute
        // TTL to expire (FR-007).
        await realtime.EvictUserAsync(boardPublicId, memberUserPublicId, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
