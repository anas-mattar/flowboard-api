using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    // Fixture board for this feature's own integration tests (plan.md ADR-11). Full board
    // CRUD and the prototype's three-board seed are 003/006's scope.
    public const int FixtureBoardId = 1;
    public static readonly Guid FixtureBoardPublicId = new("00000000-0000-0000-0000-000000000003");

    // specs/003-board-view-readonly/data-model.md Seed data — reproduces
    // screenshots/board-canvas.png. "Product Roadmap Q3"'s sidebar card-count digit in the
    // capture (13) does not match the sum of its own visibly-rendered list card-count pills
    // (3+2+4+2=11) — an internal inconsistency in the static prototype mockup, not a real
    // count. This seed's computed cardCount is a true row count (11), not the capture's
    // literal digit; documented as an accepted, disclosed deviation (see review-notes.md).
    public const int ProductRoadmapBoardId = 2;
    public static readonly Guid ProductRoadmapBoardPublicId = new("00000000-0000-0000-0000-000000000009");
    public const int MarketingLaunchBoardId = 3;
    public static readonly Guid MarketingLaunchBoardPublicId = new("00000000-0000-0000-0000-00000000000a");
    public const int CustomerSupportBoardId = 4;
    public static readonly Guid CustomerSupportBoardPublicId = new("00000000-0000-0000-0000-00000000000b");

    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("Board");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.WorkspaceId);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();

        // specs/003-board-view-readonly/data-model.md#Board — additive columns; the
        // defaults backfill the one pre-existing seeded row without changing 002's tests.
        builder.Property(x => x.Color).HasMaxLength(20).IsRequired().HasDefaultValue("#64748b");
        builder.Property(x => x.Starred).HasDefaultValue(false);

        // database-rules.md: RowVersion required for 006's board rename If-Match.
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedDate).HasColumnType("datetime2");
        builder.Property(x => x.DeletedBy).HasMaxLength(100);

        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasData(new Board
        {
            Id = FixtureBoardId,
            PublicId = FixtureBoardPublicId,
            WorkspaceId = WorkspaceConfiguration.FixtureWorkspaceId,
            Name = "Fixture Board",
            CreatedDate = UserConfiguration.SeedTimestampUtc,
            CreatedBy = "MIGRATION",
            IsDeleted = false,
        });

        builder.HasData(
            new Board
            {
                Id = ProductRoadmapBoardId,
                PublicId = ProductRoadmapBoardPublicId,
                WorkspaceId = WorkspaceConfiguration.FixtureWorkspaceId,
                Name = "Product Roadmap Q3",
                Color = "#4f46e5",
                Starred = true,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Board
            {
                Id = MarketingLaunchBoardId,
                PublicId = MarketingLaunchBoardPublicId,
                WorkspaceId = WorkspaceConfiguration.FixtureWorkspaceId,
                Name = "Marketing Launch",
                Color = "#7c3aed",
                Starred = false,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Board
            {
                Id = CustomerSupportBoardId,
                PublicId = CustomerSupportBoardPublicId,
                WorkspaceId = WorkspaceConfiguration.FixtureWorkspaceId,
                Name = "Customer Support",
                Color = "#16a34a",
                Starred = false,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            });
    }
}
