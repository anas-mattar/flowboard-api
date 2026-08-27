// Provider side of specs/002-auth-workspaces/contracts/auth-api.md.
using Flowboard.Api.Data;
using Flowboard.Api.Domain;
using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Services;

public sealed record UserDto(Guid PublicId, string Email, string DisplayName, string Initials, string AvatarColor)
{
    public static UserDto From(User user) =>
        new(user.PublicId, user.Email, user.DisplayName, user.Initials, user.AvatarColor);
}

public sealed record WorkspaceDto(Guid PublicId, string Name, string Role)
{
    // ADR-10: a workspace has exactly one admin — its creator — in this feature's scope.
    public const string WorkspaceAdminRole = "WorkspaceAdmin";

    public static WorkspaceDto From(Workspace workspace) =>
        new(workspace.PublicId, workspace.Name, WorkspaceAdminRole);
}

public sealed record AuthResponse(UserDto User, WorkspaceDto Workspace, string Token, DateTime ExpiresAtUtc);

public sealed record SignupRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public interface IAuthService
{
    Task<Result<AuthResponse>> SignUpAsync(SignupRequest request, CancellationToken cancellationToken);

    Task<Result<AuthResponse>> LogInAsync(LoginRequest request, CancellationToken cancellationToken);
}

public sealed class AuthService(
    FlowboardDbContext db, IPasswordHasher passwordHasher, ITokenService tokenService) : IAuthService
{
    private static readonly string[] AvatarPalette =
        ["#7c3aed", "#0ea5e9", "#16a34a", "#ea580c", "#db2777", "#64748b"];

    public async Task<Result<AuthResponse>> SignUpAsync(SignupRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim();

        if (await db.Users.AsNoTracking().AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
        {
            return Failure.Conflict("email already in use");
        }

        var now = DateTime.UtcNow;
        var displayName = request.DisplayName.Trim();

        var user = new User
        {
            PublicId = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.Hash(request.Password),
            DisplayName = displayName,
            Initials = DeriveInitials(displayName),
            AvatarColor = DeriveAvatarColor(normalizedEmail),
            CreatedDate = now,
            CreatedBy = "SYSTEM",
        };
        db.Users.Add(user);

        var workspace = new Workspace
        {
            PublicId = Guid.NewGuid(),
            Name = $"{displayName}'s Workspace",
            Owner = user,
            CreatedDate = now,
            CreatedBy = "SYSTEM",
        };
        db.Workspaces.Add(workspace);

        // FR-011: claim any pending invitations for this email, in the same unit of work.
        var pendingInvitations = await db.Invitations
            .Where(i => i.Email == normalizedEmail && i.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var invitation in pendingInvitations)
        {
            db.BoardMembers.Add(new BoardMember
            {
                BoardId = invitation.BoardId,
                User = user,
                Role = invitation.Role,
                CreatedDate = now,
                CreatedBy = "SYSTEM",
            });
            invitation.Status = InvitationStatus.Accepted;
            invitation.UpdatedDate = now;
            invitation.UpdatedBy = "SYSTEM";
        }

        try
        {
            // Single SaveChangesAsync = one transaction (User + Workspace + claimed
            // Invitations all commit together, or none do).
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // SC-004: guard the race where two concurrent signups both pass the check above.
            if (await db.Users.AsNoTracking().AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
            {
                return Failure.Conflict("email already in use");
            }

            throw;
        }

        var issued = tokenService.IssueToken(user);
        return Result<AuthResponse>.Success(
            new AuthResponse(UserDto.From(user), WorkspaceDto.From(workspace), issued.Token, issued.ExpiresAtUtc));
    }

    public async Task<Result<AuthResponse>> LogInAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // FR-004: identical failure for "no such email" and "wrong password" — never
        // reveal which one it was.
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Failure.Unauthorized();
        }

        var workspace = await db.Workspaces.FirstAsync(w => w.OwnerUserId == user.Id, cancellationToken);

        var issued = tokenService.IssueToken(user);
        return Result<AuthResponse>.Success(
            new AuthResponse(UserDto.From(user), WorkspaceDto.From(workspace), issued.Token, issued.ExpiresAtUtc));
    }

    private static string DeriveInitials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)],
            _ => $"{parts[0][0]}{parts[^1][0]}",
        };
        return initials.ToUpperInvariant();
    }

    private static string DeriveAvatarColor(string email)
    {
        var seed = 0;
        foreach (var ch in email)
        {
            seed = (seed * 31 + ch) % AvatarPalette.Length;
        }

        return AvatarPalette[Math.Abs(seed) % AvatarPalette.Length];
    }
}
