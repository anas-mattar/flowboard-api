using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public const int FixtureWorkspaceId = 1;
    public static readonly Guid FixtureWorkspacePublicId = new("00000000-0000-0000-0000-000000000002");

    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("Workspace");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();

        builder.HasOne(x => x.Owner)
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        // ADR-10: exactly one workspace per owner.
        builder.HasIndex(x => x.OwnerUserId).IsUnique();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedDate).HasColumnType("datetime2");
        builder.Property(x => x.DeletedBy).HasMaxLength(100);

        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasData(new Workspace
        {
            Id = FixtureWorkspaceId,
            PublicId = FixtureWorkspacePublicId,
            Name = "Fixture Workspace",
            OwnerUserId = UserConfiguration.FixtureOwnerId,
            CreatedDate = UserConfiguration.SeedTimestampUtc,
            CreatedBy = "MIGRATION",
            IsDeleted = false,
        });
    }
}
