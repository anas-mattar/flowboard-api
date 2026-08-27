using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class CardLabelConfiguration : IEntityTypeConfiguration<CardLabel>
{
    public void Configure(EntityTypeBuilder<CardLabel> builder)
    {
        builder.ToTable("CardLabel");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Card)
            .WithMany(x => x.CardLabels)
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Label)
            .WithMany()
            .HasForeignKey(x => x.LabelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CardId, x.LabelId }).IsUnique();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

        // Mirrors Card's own soft-delete filter (same fix as BoardMember→Board, 002).
        builder.HasQueryFilter(x => !x.Card.IsDeleted);

        // specs/003-board-view-readonly/data-model.md Seed data (screenshots/board-canvas.png).
        builder.HasData(
            new CardLabel { Id = 1, CardId = CardConfiguration.DefineSsoRequirementsId, LabelId = LabelConfiguration.FeatureId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 2, CardId = CardConfiguration.AccessibilityAuditId, LabelId = LabelConfiguration.DesignLabelId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 3, CardId = CardConfiguration.AccessibilityAuditId, LabelId = LabelConfiguration.ResearchId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 4, CardId = CardConfiguration.CardDetailRedesignId, LabelId = LabelConfiguration.DesignLabelId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 5, CardId = CardConfiguration.EmptyStateIllustrationsId, LabelId = LabelConfiguration.DesignLabelId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 6, CardId = CardConfiguration.DragDropPerformanceId, LabelId = LabelConfiguration.BugId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 7, CardId = CardConfiguration.DragDropPerformanceId, LabelId = LabelConfiguration.UrgentId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 8, CardId = CardConfiguration.ResearchCompetitorFlowsId, LabelId = LabelConfiguration.ResearchId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 9, CardId = CardConfiguration.ResearchCompetitorFlowsId, LabelId = LabelConfiguration.BugId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 10, CardId = CardConfiguration.BoardMemberInvitationsId, LabelId = LabelConfiguration.FeatureId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 11, CardId = CardConfiguration.LabelFilterTopBarId, LabelId = LabelConfiguration.FeatureId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 12, CardId = CardConfiguration.KeyboardShortcutsPassId, LabelId = LabelConfiguration.FeatureId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardLabel { Id = 13, CardId = CardConfiguration.ActivityFeedPaginationId, LabelId = LabelConfiguration.BugId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" });
    }
}
