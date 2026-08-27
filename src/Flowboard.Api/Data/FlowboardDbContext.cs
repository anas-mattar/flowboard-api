// plan.md ADR-6 — single DbContext in the existing Flowboard.Api project.
using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Data;

public sealed class FlowboardDbContext(DbContextOptions<FlowboardDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Board> Boards => Set<Board>();

    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowboardDbContext).Assembly);
    }
}
