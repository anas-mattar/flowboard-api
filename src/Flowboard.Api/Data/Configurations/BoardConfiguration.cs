using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    // Fixture board for this feature's own integration tests (plan.md ADR-11). Full board
    // CRUD and the prototype's three-board seed are 003/006's scope.
    public const int FixtureBoardId = 1;
    public static readonly Guid FixtureBoardPublicId = new("00000000-0000-0000-0000-000000000003");

    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("Board");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.WorkspaceId);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedDate).HasColumnType("datetime2");
        builder.Property(x => x.DeletedBy).HasMaxLength(100);

        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasData(new Board
        {
            Id = FixtureBoardId,
            PublicId = FixtureBoardPublicId,
            WorkspaceId = WorkspaceConfiguration.FixtureWorkspaceId,
            Name = "Fixture Board",
            CreatedDate = UserConfiguration.SeedTimestampUtc,
            CreatedBy = "MIGRATION",
            IsDeleted = false,
        });
    }
}
