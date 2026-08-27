// plan.md ADR-9. Exercises BoardAccessService directly against flowboard-db-test (real SQL
// Server provider, no HTTP pipeline) — research.md R-9's per-test cleanup pattern.
using Flowboard.Api.Data;
using Flowboard.Api.Domain.Entities;
using Flowboard.Api.Services;
using Flowboard.Api.Tests.TestFixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Flowboard.Api.Tests;

[Collection(FlowboardApiCollection.Name)]
public sealed class BoardAccessServiceTests : IAsyncLifetime
{
    private readonly IServiceScope _scope;
    private readonly FlowboardDbContext _db;
    private readonly IBoardAccessService _sut;

    private readonly List<User> _users = [];
    private readonly List<Workspace> _workspaces = [];
    private readonly List<Board> _boards = [];
    private readonly List<BoardMember> _members = [];

    public BoardAccessServiceTests(FlowboardApiFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();
        _sut = new BoardAccessService(_db);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _db.BoardMembers.RemoveRange(_members);
        _db.Boards.RemoveRange(_boards);
        _db.Workspaces.RemoveRange(_workspaces);
        _db.Users.RemoveRange(_users);
        await _db.SaveChangesAsync();
        _scope.Dispose();
    }

    [Fact]
    public async Task ResolveAsync_WorkspaceOwner_GetsImplicitBoardAdmin()
    {
        var (owner, _, board) = await CreateBoardAsync();

        var access = await _sut.ResolveAsync(board.PublicId, owner.PublicId, CancellationToken.None);

        Assert.NotNull(access);
        Assert.Equal(BoardRole.BoardAdmin, access!.Role);
        Assert.True(access.IsWorkspaceOwner);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitBoardMember_GetsTheirRole()
    {
        var (_, _, board) = await CreateBoardAsync();
        var member = await CreateUserAsync("member");

        var boardMember = new BoardMember
        {
            BoardId = board.Id,
            UserId = member.Id,
            Role = BoardRole.Observer,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = "TEST",
        };
        _db.BoardMembers.Add(boardMember);
        await _db.SaveChangesAsync();
        _members.Add(boardMember);

        var access = await _sut.ResolveAsync(board.PublicId, member.PublicId, CancellationToken.None);

        Assert.NotNull(access);
        Assert.Equal(BoardRole.Observer, access!.Role);
        Assert.False(access.IsWorkspaceOwner);
    }

    [Fact]
    public async Task ResolveAsync_NoAccess_ReturnsNull()
    {
        var (_, _, board) = await CreateBoardAsync();
        var stranger = await CreateUserAsync("stranger");

        var access = await _sut.ResolveAsync(board.PublicId, stranger.PublicId, CancellationToken.None);

        Assert.Null(access);
    }

    [Fact]
    public async Task ResolveAsync_UnknownBoard_ReturnsNull()
    {
        var access = await _sut.ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(access);
    }

    private async Task<User> CreateUserAsync(string label)
    {
        var user = new User
        {
            PublicId = Guid.NewGuid(),
            Email = $"{label}-{Guid.NewGuid()}@test.local",
            PasswordHash = "unused-in-this-test",
            DisplayName = label,
            Initials = label.Length >= 2 ? label[..2].ToUpperInvariant() : label.ToUpperInvariant(),
            AvatarColor = "#000000",
            CreatedDate = DateTime.UtcNow,
            CreatedBy = "TEST",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _users.Add(user);
        return user;
    }

    private async Task<(User Owner, Workspace Workspace, Board Board)> CreateBoardAsync()
    {
        var owner = await CreateUserAsync("owner");

        var workspace = new Workspace
        {
            PublicId = Guid.NewGuid(),
            Name = "Test Workspace",
            OwnerUserId = owner.Id,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = "TEST",
        };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();
        _workspaces.Add(workspace);

        var board = new Board
        {
            PublicId = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Test Board",
            CreatedDate = DateTime.UtcNow,
            CreatedBy = "TEST",
        };
        _db.Boards.Add(board);
        await _db.SaveChangesAsync();
        _boards.Add(board);

        return (owner, workspace, board);
    }
}
