using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    // specs/004-card-crud/data-model.md — PublicId backfill for the 6 rows 003 already
    // seeded (invariant 8; items are now addressed individually).
    public static readonly Guid WireframesPublicId = new("00000000-0000-0000-0000-000000000030");
    public static readonly Guid VisualDesignPublicId = new("00000000-0000-0000-0000-000000000031");
    public static readonly Guid PrototypeInteractionsPublicId = new("00000000-0000-0000-0000-000000000032");
    public static readonly Guid ProfileVirtualizedRenderingPublicId = new("00000000-0000-0000-0000-000000000033");
    public static readonly Guid TestWith500CardBoardPublicId = new("00000000-0000-0000-0000-000000000034");
    public static readonly Guid DocumentTop3CompetitorFlowsPublicId = new("00000000-0000-0000-0000-000000000035");

    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("ChecklistItem");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.HasOne(x => x.Card)
            .WithMany(x => x.ChecklistItems)
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CardId);

        builder.Property(x => x.Text).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Done).HasDefaultValue(false);
        builder.Property(x => x.Position).IsRequired();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasQueryFilter(x => !x.Card.IsDeleted);

        // specs/003-board-view-readonly/data-model.md Seed data — "Card detail redesign"
        // shows 2/3 done, "Drag & drop performance..." shows 1/2, "Research competitor
        // onboarding flows" shows 0/1 (screenshots/board-canvas.png).
        builder.HasData(
            new ChecklistItem { Id = 1, PublicId = WireframesPublicId, CardId = CardConfiguration.CardDetailRedesignId, Text = "Wireframes", Done = true, Position = 1000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 2, PublicId = VisualDesignPublicId, CardId = CardConfiguration.CardDetailRedesignId, Text = "Visual design", Done = true, Position = 2000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 3, PublicId = PrototypeInteractionsPublicId, CardId = CardConfiguration.CardDetailRedesignId, Text = "Prototype interactions", Done = false, Position = 3000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 4, PublicId = ProfileVirtualizedRenderingPublicId, CardId = CardConfiguration.DragDropPerformanceId, Text = "Profile virtualized rendering", Done = true, Position = 1000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 5, PublicId = TestWith500CardBoardPublicId, CardId = CardConfiguration.DragDropPerformanceId, Text = "Test with 500+ card board", Done = false, Position = 2000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 6, PublicId = DocumentTop3CompetitorFlowsPublicId, CardId = CardConfiguration.ResearchCompetitorFlowsId, Text = "Document top 3 competitor flows", Done = false, Position = 1000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" });
    }
}
