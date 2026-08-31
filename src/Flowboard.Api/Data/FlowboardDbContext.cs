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

    public DbSet<List> Lists => Set<List>();

    public DbSet<Card> Cards => Set<Card>();

    public DbSet<Label> Labels => Set<Label>();

    public DbSet<CardLabel> CardLabels => Set<CardLabel>();

    public DbSet<CardMember> CardMembers => Set<CardMember>();

    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowboardDbContext).Assembly);
    }
}
