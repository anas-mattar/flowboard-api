using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitation");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.HasOne(x => x.Board)
            .WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.InvitedBy)
            .WithMany()
            .HasForeignKey(x => x.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Invitation_Role",
            "[Role] IN ('BoardAdmin','BoardMember','Observer')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Invitation_Status",
            "[Status] IN ('Pending','Accepted','Revoked')"));

        // At most one Pending invitation per (board, email) — re-inviting updates the
        // existing row's role instead of creating a second one (data-model.md, spec edge case).
        builder.HasIndex(x => new { x.BoardId, x.Email })
            .IsUnique()
            .HasFilter("[Status] = 'Pending'")
            .HasDatabaseName("IX_Invitation_BoardId_Email_Pending");

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        // Mirrors Board's own soft-delete filter (EF model-validation warning fix).
        builder.HasQueryFilter(x => !x.Board.IsDeleted);
    }
}
