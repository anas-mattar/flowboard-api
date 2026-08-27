using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    // specs/003-board-view-readonly/data-model.md Seed data — "Product Roadmap Q3"'s
    // labels (screenshots/board-canvas.png).
    public const int FeatureId = 1;
    public static readonly Guid FeaturePublicId = new("00000000-0000-0000-0000-000000000023");
    public const int DesignLabelId = 2;
    public static readonly Guid DesignLabelPublicId = new("00000000-0000-0000-0000-000000000024");
    public const int ResearchId = 3;
    public static readonly Guid ResearchPublicId = new("00000000-0000-0000-0000-000000000025");
    public const int BugId = 4;
    public static readonly Guid BugPublicId = new("00000000-0000-0000-0000-000000000026");
    public const int UrgentId = 5;
    public static readonly Guid UrgentPublicId = new("00000000-0000-0000-0000-000000000027");

    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("Label");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.HasOne(x => x.Board)
            .WithMany()
            .HasForeignKey(x => x.BoardId)
            // invariant 7's enforcement point: a label belongs to exactly one board.
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.BoardId);

        builder.Property(x => x.Name).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(20).IsRequired();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        // Mirrors Board's own soft-delete filter (same fix as BoardMember→Board, 002) —
        // required-navigation-vs-filtered-principal model-validation warning.
        builder.HasQueryFilter(x => !x.Board.IsDeleted);

        builder.HasData(
            new Label
            {
                Id = FeatureId,
                PublicId = FeaturePublicId,
                BoardId = BoardConfiguration.ProductRoadmapBoardId,
                Name = "Feature",
                Color = "#16a34a",
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
            },
            new Label
            {
                Id = DesignLabelId,
                PublicId = DesignLabelPublicId,
                BoardId = BoardConfiguration.ProductRoadmapBoardId,
                Name = "Design",
                Color = "#7c3aed",
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
            },
            new Label
            {
                Id = ResearchId,
                PublicId = ResearchPublicId,
                BoardId = BoardConfiguration.ProductRoadmapBoardId,
                Name = "Research",
                Color = "#2563eb",
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
            },
            new Label
            {
                Id = BugId,
                PublicId = BugPublicId,
                BoardId = BoardConfiguration.ProductRoadmapBoardId,
                Name = "Bug",
                Color = "#dc2626",
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
            },
            new Label
            {
                Id = UrgentId,
                PublicId = UrgentPublicId,
                BoardId = BoardConfiguration.ProductRoadmapBoardId,
                Name = "Urgent",
                Color = "#991b1b",
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
            });
    }
}
