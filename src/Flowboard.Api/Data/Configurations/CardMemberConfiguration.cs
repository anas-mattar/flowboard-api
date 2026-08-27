using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class CardMemberConfiguration : IEntityTypeConfiguration<CardMember>
{
    public void Configure(EntityTypeBuilder<CardMember> builder)
    {
        builder.ToTable("CardMember");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Card)
            .WithMany(x => x.CardMembers)
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CardId, x.UserId }).IsUnique();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasQueryFilter(x => !x.Card.IsDeleted);

        // specs/003-board-view-readonly/data-model.md Seed data (screenshots/board-canvas.png).
        builder.HasData(
            new CardMember { Id = 1, CardId = CardConfiguration.AccessibilityAuditId, UserId = UserConfiguration.FixtureMemberPnId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardMember { Id = 2, CardId = CardConfiguration.CardDetailRedesignId, UserId = UserConfiguration.FixtureMemberPnId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardMember { Id = 3, CardId = CardConfiguration.EmptyStateIllustrationsId, UserId = UserConfiguration.FixtureMemberLfId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardMember { Id = 4, CardId = CardConfiguration.DragDropPerformanceId, UserId = UserConfiguration.FixtureMemberOhId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardMember { Id = 5, CardId = CardConfiguration.ResearchCompetitorFlowsId, UserId = UserConfiguration.FixtureMemberLfId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardMember { Id = 6, CardId = CardConfiguration.BoardMemberInvitationsId, UserId = UserConfiguration.FixtureMemberAkId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardMember { Id = 7, CardId = CardConfiguration.BoardMemberInvitationsId, UserId = UserConfiguration.FixtureMemberLfId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardMember { Id = 8, CardId = CardConfiguration.LabelFilterTopBarId, UserId = UserConfiguration.FixtureMemberPnId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardMember { Id = 9, CardId = CardConfiguration.KeyboardShortcutsPassId, UserId = UserConfiguration.FixtureMemberTbId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" },
            new CardMember { Id = 10, CardId = CardConfiguration.ActivityFeedPaginationId, UserId = UserConfiguration.FixtureMemberOhId, CreatedDate = UserConfiguration.SeedTimestampUtc, CreatedBy = "MIGRATION" });
    }
}
