using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class BoardMemberConfiguration : IEntityTypeConfiguration<BoardMember>
{
    public void Configure(EntityTypeBuilder<BoardMember> builder)
    {
        builder.ToTable("BoardMember");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Board)
            .WithMany()
            .HasForeignKey(x => x.BoardId)
            // database-rules.md: FKs to soft-deleted master data never cascade.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // FR-009: no duplicate membership rows.
        builder.HasIndex(x => new { x.BoardId, x.UserId }).IsUnique();

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_BoardMember_Role",
            "[Role] IN ('BoardAdmin','BoardMember','Observer')"));

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

        // Mirrors Board's own soft-delete filter — a membership row for an archived board
        // should not surface in unfiltered queries either (EF model-validation warning fix).
        builder.HasQueryFilter(x => !x.Board.IsDeleted);
    }
}
