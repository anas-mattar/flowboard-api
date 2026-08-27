using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comment");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Card)
            .WithMany(x => x.Comments)
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CardId);

        builder.HasOne(x => x.Author)
            .WithMany()
            .HasForeignKey(x => x.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Body).IsRequired();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasQueryFilter(x => !x.Card.IsDeleted);

        // specs/003-board-view-readonly/data-model.md Seed data — CreatedBy is the author's
        // PublicId (002 wrap-up B3 convention), not "MIGRATION".
        builder.HasData(
            new Comment
            {
                Id = 1,
                CardId = CardConfiguration.DragDropPerformanceId,
                AuthorId = UserConfiguration.FixtureMemberOhId,
                Body = "Repro steps attached in the linked doc.",
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = UserConfiguration.FixtureMemberOhPublicId.ToString(),
            },
            new Comment
            {
                Id = 2,
                CardId = CardConfiguration.ResearchCompetitorFlowsId,
                AuthorId = UserConfiguration.FixtureMemberLfId,
                Body = "Started a competitor matrix, will share by EOW.",
                CreatedDate = UserConfiguration.SeedTimestampUtc,
                CreatedBy = UserConfiguration.FixtureMemberLfPublicId.ToString(),
            });
    }
}
