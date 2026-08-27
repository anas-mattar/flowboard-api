using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    // specs/003-board-view-readonly/data-model.md Seed data — "Product Roadmap Q3" (1-11),
    // "Marketing Launch" (12-15), "Customer Support" (16-17). DueAt is left null here for
    // every card — the five that need a due date get one via a hand-authored UPDATE in the
    // AddBoardContent migration's Up(), computed from DateTime.UtcNow at apply time, so the
    // seeded due/soon/overdue buckets stay correct relative to whenever a developer or CI
    // actually runs the migration (a HasData literal would freeze a calendar date at scaffold
    // time and drift stale within days).
    public const int DefineSsoRequirementsId = 1;
    public const int AccessibilityAuditId = 2;
    public const int PrototypeSmokeCardId = 3;
    public const int CardDetailRedesignId = 4;
    public const int EmptyStateIllustrationsId = 5;
    public const int DragDropPerformanceId = 6;
    public const int ResearchCompetitorFlowsId = 7;
    public const int BoardMemberInvitationsId = 8;
    public const int LabelFilterTopBarId = 9;
    public const int KeyboardShortcutsPassId = 10;
    public const int ActivityFeedPaginationId = 11;
    public const int LaunchMessagingBriefId = 12;
    public const int PressKitAssetsId = 13;
    public const int SocialCalendarId = 14;
    public const int PartnerCoMarketingId = 15;
    public const int UpdateFaqId = 16;
    public const int EscalationRunbookId = 17;

    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("Card");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.HasOne(x => x.List)
            .WithMany()
            .HasForeignKey(x => x.ListId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ListId);
        builder.HasIndex(x => new { x.ListId, x.Position });
        builder.HasIndex(x => x.DueAt);

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description);
        builder.Property(x => x.Position).IsRequired();
        builder.Property(x => x.DueAt).HasColumnType("datetime2");
        builder.Property(x => x.DueComplete).HasDefaultValue(false);

        // database-rules.md: RowVersion is required on Card for 004's optimistic
        // concurrency (If-Match); no endpoint writes it yet in this feature.
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedDate).HasColumnType("datetime2");
        builder.Property(x => x.DeletedBy).HasMaxLength(100);

        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasData(
            new Card
            {
                Id = DefineSsoRequirementsId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000012"),
                ListId = ListConfiguration.BacklogId,
                Title = "Define SSO requirements for Enterprise",
                Position = 1000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = AccessibilityAuditId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000013"),
                ListId = ListConfiguration.BacklogId,
                Title = "Accessibility audit (WCAG 2.2 AA)",
                Position = 2000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = PrototypeSmokeCardId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000014"),
                ListId = ListConfiguration.BacklogId,
                Title = "Prototype smoke card",
                Position = 3000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = CardDetailRedesignId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000015"),
                ListId = ListConfiguration.DesignId,
                Title = "Card detail redesign",
                Position = 1000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = EmptyStateIllustrationsId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000016"),
                ListId = ListConfiguration.DesignId,
                Title = "Empty-state illustrations",
                Position = 2000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = DragDropPerformanceId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000017"),
                ListId = ListConfiguration.InProgressId,
                Title = "Drag & drop performance on large boards",
                Position = 1000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = ResearchCompetitorFlowsId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000018"),
                ListId = ListConfiguration.InProgressId,
                Title = "Research competitor onboarding flows",
                Position = 2000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = BoardMemberInvitationsId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000019"),
                ListId = ListConfiguration.InProgressId,
                Title = "Board member invitations",
                Position = 3000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = LabelFilterTopBarId,
                PublicId = new Guid("00000000-0000-0000-0000-00000000001a"),
                ListId = ListConfiguration.InProgressId,
                Title = "Label filter in the top bar",
                Position = 4000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = KeyboardShortcutsPassId,
                PublicId = new Guid("00000000-0000-0000-0000-00000000001b"),
                ListId = ListConfiguration.ReviewId,
                Title = "Keyboard shortcuts pass",
                Position = 1000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = ActivityFeedPaginationId,
                PublicId = new Guid("00000000-0000-0000-0000-00000000001c"),
                ListId = ListConfiguration.ReviewId,
                Title = "Activity feed pagination",
                Position = 2000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = LaunchMessagingBriefId,
                PublicId = new Guid("00000000-0000-0000-0000-00000000001d"),
                ListId = ListConfiguration.MarketingToDoId,
                Title = "Launch messaging brief",
                Position = 1000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = PressKitAssetsId,
                PublicId = new Guid("00000000-0000-0000-0000-00000000001e"),
                ListId = ListConfiguration.MarketingToDoId,
                Title = "Press kit assets",
                Position = 2000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = SocialCalendarId,
                PublicId = new Guid("00000000-0000-0000-0000-00000000001f"),
                ListId = ListConfiguration.MarketingToDoId,
                Title = "Social calendar",
                Position = 3000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = PartnerCoMarketingId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000020"),
                ListId = ListConfiguration.MarketingToDoId,
                Title = "Partner co-marketing outreach",
                Position = 4000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = UpdateFaqId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000021"),
                ListId = ListConfiguration.SupportOpenTicketsId,
                Title = "Update FAQ for v2 pricing",
                Position = 1000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new Card
            {
                Id = EscalationRunbookId,
                PublicId = new Guid("00000000-0000-0000-0000-000000000022"),
                ListId = ListConfiguration.SupportOpenTicketsId,
                Title = "Escalation runbook review",
                Position = 2000,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            });
    }
}
