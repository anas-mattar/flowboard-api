// database-standards.md EF Core Mapping Standard. Seeds the fixture owner used by
// board-membership integration tests (BoardConfiguration/WorkspaceConfiguration seed chain).
using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowboard.Api.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    // Second-model adversarial review B1: this seed is emitted into the PRODUCTION migration
    // (HasData), so it must never carry a working credential — a real password hash here
    // would ship a loginable admin account into every database this migration touches.
    // PlaceholderPasswordHash is a well-formed-but-unverifiable BCrypt hash (no known
    // plaintext produces it), so PasswordHasher.Verify always returns false against it. The
    // test host (FlowboardApiFactory.InitializeAsync) overwrites this one row's PasswordHash
    // with a real, known test hash after migrating — that real hash lives only in the test
    // project, never in this production model.
    public const int FixtureOwnerId = 1;
    public static readonly Guid FixtureOwnerPublicId = new("00000000-0000-0000-0000-000000000001");
    public const string FixtureOwnerEmail = "fixture-owner@flowboard.test";
    public const string FixtureOwnerPlaceholderPasswordHash =
        "$2a$12$DISABLED.NO.CREDENTIAL.SHIPPED.IN.THIS.MIGRATION.SEED";
    public static readonly DateTime SeedTimestampUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("User");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();

        // SQL Server's default collation (SQL_Latin1_General_CP1_CI_AS) is case-insensitive,
        // so this unique index already enforces FR-002 case-insensitively without extra config.
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.HasIndex(x => x.Email).IsUnique().HasDatabaseName("IX_User_Email");

        builder.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Initials).HasMaxLength(4).IsRequired();
        builder.Property(x => x.AvatarColor).HasMaxLength(20).IsRequired();

        builder.Property(x => x.CreatedDate).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.HasData(new User
        {
            Id = FixtureOwnerId,
            PublicId = FixtureOwnerPublicId,
            Email = FixtureOwnerEmail,
            PasswordHash = FixtureOwnerPlaceholderPasswordHash,
            DisplayName = "Fixture Owner",
            Initials = "FO",
            AvatarColor = "#64748b",
            CreatedDate = SeedTimestampUtc,
            CreatedBy = "MIGRATION",
        });
    }
}
