using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class ListConfiguration : IEntityTypeConfiguration<List>
{
    // specs/003-board-view-readonly/data-model.md Seed data.
    public const int BacklogId = 1;
    public static readonly Guid BacklogPublicId = new("00000000-0000-0000-0000-00000000000c");
    public const int DesignId = 2;
    public static readonly Guid DesignPublicId = new("00000000-0000-0000-0000-00000000000d");
    public const int InProgressId = 3;
    public static readonly Guid InProgressPublicId = new("00000000-0000-0000-0000-00000000000e");
    public const int ReviewId = 4;
    public static readonly Guid ReviewPublicId = new("00000000-0000-0000-0000-00000000000f");
    public const int MarketingToDoId = 5;
    public static readonly Guid MarketingToDoPublicId = new("00000000-0000-0000-0000-000000000010");
    public const int SupportOpenTicketsId = 6;
    public static readonly Guid SupportOpenTicketsPublicId = new("00000000-0000-0000-0000-000000000011");

    public void Configure(EntityTypeBuilder<List> builder)
    {
        builder.ToTable("List");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.HasOne(x => x.Board)
            .WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.BoardId);
        builder.HasIndex(x => new { x.BoardId, x.Position });

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Position).IsRequired();
        builder.Property(x => x.WipLimit);

        // database-rules.md: RowVersion required for 006's list rename If-Match.
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
            new List
            {
                Id = BacklogId,
                PublicId = BacklogPublicId,
                BoardId = BoardConfiguration.ProductRoadmapBoardId,
                Name = "Backlog",
                Position = 1000,
                WipLimit = null,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new List
            {
                Id = DesignId,
                PublicId = DesignPublicId,
                BoardId = BoardConfiguration.ProductRoadmapBoardId,
                Name = "Design",
                Position = 2000,
                WipLimit = 3,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new List
            {
                Id = InProgressId,
                PublicId = InProgressPublicId,
                BoardId = BoardConfiguration.ProductRoadmapBoardId,
                Name = "In Progress",
                Position = 3000,
                WipLimit = 3,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new List
            {
                Id = ReviewId,
                PublicId = ReviewPublicId,
                BoardId = BoardConfiguration.ProductRoadmapBoardId,
                Name = "Review",
                Position = 4000,
                WipLimit = 2,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new List
            {
                Id = MarketingToDoId,
                PublicId = MarketingToDoPublicId,
                BoardId = BoardConfiguration.MarketingLaunchBoardId,
                Name = "To Do",
                Position = 1000,
                WipLimit = null,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            },
            new List
            {
                Id = SupportOpenTicketsId,
                PublicId = SupportOpenTicketsPublicId,
                BoardId = BoardConfiguration.CustomerSupportBoardId,
                Name = "Open Tickets",
                Position = 1000,
                WipLimit = null,
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = "MIGRATION",
                IsDeleted = false,
            });
    }
}
