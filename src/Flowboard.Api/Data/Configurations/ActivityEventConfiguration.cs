using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.ToTable("ActivityEvent");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Card)
            .WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Actor)
            .WithMany()
            .HasForeignKey(x => x.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Type).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

        // specs/004-card-crud/data-model.md — feed reads: one card, newest first.
        builder.HasIndex(x => new { x.CardId, x.CreatedDate }).HasDatabaseName("IX_ActivityEvent_CardId_CreatedDate");

        // Card's own query filter already excludes soft-deleted cards' rows via the FK
        // relationship; no separate filter needed here (no soft-delete trio on this table).
        builder.HasQueryFilter(x => !x.Card.IsDeleted);
    }
}
