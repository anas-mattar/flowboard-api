using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("ChecklistItem");

        builder.HasKey(x => x.Id);

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
            new ChecklistItem { Id = 1, CardId = CardConfiguration.CardDetailRedesignId, Text = "Wireframes", Done = true, Position = 1000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 2, CardId = CardConfiguration.CardDetailRedesignId, Text = "Visual design", Done = true, Position = 2000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 3, CardId = CardConfiguration.CardDetailRedesignId, Text = "Prototype interactions", Done = false, Position = 3000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 4, CardId = CardConfiguration.DragDropPerformanceId, Text = "Profile virtualized rendering", Done = true, Position = 1000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 5, CardId = CardConfiguration.DragDropPerformanceId, Text = "Test with 500+ card board", Done = false, Position = 2000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new ChecklistItem { Id = 6, CardId = CardConfiguration.ResearchCompetitorFlowsId, Text = "Document top 3 competitor flows", Done = false, Position = 1000, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" });
    }
}
