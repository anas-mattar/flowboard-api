using System;
using Flowboard.Api.Data.Configurations;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flowboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Board",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "#64748b");

            migrationBuilder.AddColumn<bool>(
                name: "Starred",
                table: "Board",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Label",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Label", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Label_Board_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Board",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "List",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Position = table.Column<double>(type: "float", nullable: false),
                    WipLimit = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_List", x => x.Id);
                    table.ForeignKey(
                        name: "FK_List_Board_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Board",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Card",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Position = table.Column<double>(type: "float", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueComplete = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Card", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Card_List_ListId",
                        column: x => x.ListId,
                        principalTable: "List",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardLabel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CardId = table.Column<int>(type: "int", nullable: false),
                    LabelId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardLabel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardLabel_Card_CardId",
                        column: x => x.CardId,
                        principalTable: "Card",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardLabel_Label_LabelId",
                        column: x => x.LabelId,
                        principalTable: "Label",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardMember",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CardId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardMember_Card_CardId",
                        column: x => x.CardId,
                        principalTable: "Card",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardMember_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChecklistItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CardId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Done = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Position = table.Column<double>(type: "float", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChecklistItem_Card_CardId",
                        column: x => x.CardId,
                        principalTable: "Card",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Comment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CardId = table.Column<int>(type: "int", nullable: false),
                    AuthorId = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comment_Card_CardId",
                        column: x => x.CardId,
                        principalTable: "Card",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comment_User_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Board",
                keyColumn: "Id",
                keyValue: 1,
                column: "Color",
                value: "#64748b");

            migrationBuilder.InsertData(
                table: "Board",
                columns: new[] { "Id", "Color", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Name", "PublicId", "Starred", "UpdatedBy", "UpdatedDate", "WorkspaceId" },
                values: new object[] { 2, "#4f46e5", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Product Roadmap Q3", new Guid("00000000-0000-0000-0000-000000000009"), true, null, null, 1 });

            migrationBuilder.InsertData(
                table: "Board",
                columns: new[] { "Id", "Color", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Name", "PublicId", "UpdatedBy", "UpdatedDate", "WorkspaceId" },
                values: new object[,]
                {
                    { 3, "#7c3aed", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Marketing Launch", new Guid("00000000-0000-0000-0000-00000000000a"), null, null, 1 },
                    { 4, "#16a34a", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Customer Support", new Guid("00000000-0000-0000-0000-00000000000b"), null, null, 1 }
                });

            migrationBuilder.InsertData(
                table: "User",
                columns: new[] { "Id", "AvatarColor", "CreatedBy", "CreatedDate", "DisplayName", "Email", "Initials", "PasswordHash", "PublicId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2, "#2563eb", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Aiko Kimura", "fixture-member-ak@flowboard.test", "AK", "$2a$12$DISABLED.NO.CREDENTIAL.SHIPPED.IN.THIS.MIGRATION.SEED", new Guid("00000000-0000-0000-0000-000000000004"), null, null },
                    { 3, "#7c3aed", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Luca Ferrari", "fixture-member-lf@flowboard.test", "LF", "$2a$12$DISABLED.NO.CREDENTIAL.SHIPPED.IN.THIS.MIGRATION.SEED", new Guid("00000000-0000-0000-0000-000000000005"), null, null },
                    { 4, "#16a34a", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Omar Haddad", "fixture-member-oh@flowboard.test", "OH", "$2a$12$DISABLED.NO.CREDENTIAL.SHIPPED.IN.THIS.MIGRATION.SEED", new Guid("00000000-0000-0000-0000-000000000006"), null, null },
                    { 5, "#ea580c", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Priya Nair", "fixture-member-pn@flowboard.test", "PN", "$2a$12$DISABLED.NO.CREDENTIAL.SHIPPED.IN.THIS.MIGRATION.SEED", new Guid("00000000-0000-0000-0000-000000000007"), null, null },
                    { 6, "#b91c1c", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Tomás Bravo", "fixture-member-tb@flowboard.test", "TB", "$2a$12$DISABLED.NO.CREDENTIAL.SHIPPED.IN.THIS.MIGRATION.SEED", new Guid("00000000-0000-0000-0000-000000000008"), null, null }
                });

            migrationBuilder.InsertData(
                table: "BoardMember",
                columns: new[] { "Id", "BoardId", "CreatedBy", "CreatedDate", "Role", "UserId" },
                values: new object[,]
                {
                    { 1, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "BoardMember", 2 },
                    { 2, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "BoardMember", 3 },
                    { 3, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "BoardMember", 4 },
                    { 4, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "BoardMember", 5 },
                    { 5, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "BoardMember", 6 }
                });

            migrationBuilder.InsertData(
                table: "Label",
                columns: new[] { "Id", "BoardId", "Color", "CreatedBy", "CreatedDate", "Name", "PublicId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1, 2, "#16a34a", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Feature", new Guid("00000000-0000-0000-0000-000000000023"), null, null },
                    { 2, 2, "#7c3aed", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Design", new Guid("00000000-0000-0000-0000-000000000024"), null, null },
                    { 3, 2, "#2563eb", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Research", new Guid("00000000-0000-0000-0000-000000000025"), null, null },
                    { 4, 2, "#dc2626", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bug", new Guid("00000000-0000-0000-0000-000000000026"), null, null },
                    { 5, 2, "#991b1b", "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Urgent", new Guid("00000000-0000-0000-0000-000000000027"), null, null }
                });

            migrationBuilder.InsertData(
                table: "List",
                columns: new[] { "Id", "BoardId", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Name", "Position", "PublicId", "UpdatedBy", "UpdatedDate", "WipLimit" },
                values: new object[,]
                {
                    { 1, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Backlog", 1000.0, new Guid("00000000-0000-0000-0000-00000000000c"), null, null, null },
                    { 2, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Design", 2000.0, new Guid("00000000-0000-0000-0000-00000000000d"), null, null, 3 },
                    { 3, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "In Progress", 3000.0, new Guid("00000000-0000-0000-0000-00000000000e"), null, null, 3 },
                    { 4, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Review", 4000.0, new Guid("00000000-0000-0000-0000-00000000000f"), null, null, 2 },
                    { 5, 3, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "To Do", 1000.0, new Guid("00000000-0000-0000-0000-000000000010"), null, null, null },
                    { 6, 4, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Open Tickets", 1000.0, new Guid("00000000-0000-0000-0000-000000000011"), null, null, null }
                });

            migrationBuilder.InsertData(
                table: "Card",
                columns: new[] { "Id", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "DueAt", "ListId", "Position", "PublicId", "Title", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 1, 1000.0, new Guid("00000000-0000-0000-0000-000000000012"), "Define SSO requirements for Enterprise", null, null },
                    { 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 1, 2000.0, new Guid("00000000-0000-0000-0000-000000000013"), "Accessibility audit (WCAG 2.2 AA)", null, null },
                    { 3, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 1, 3000.0, new Guid("00000000-0000-0000-0000-000000000014"), "Prototype smoke card", null, null },
                    { 4, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 2, 1000.0, new Guid("00000000-0000-0000-0000-000000000015"), "Card detail redesign", null, null },
                    { 5, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 2, 2000.0, new Guid("00000000-0000-0000-0000-000000000016"), "Empty-state illustrations", null, null },
                    { 6, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 3, 1000.0, new Guid("00000000-0000-0000-0000-000000000017"), "Drag & drop performance on large boards", null, null },
                    { 7, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 3, 2000.0, new Guid("00000000-0000-0000-0000-000000000018"), "Research competitor onboarding flows", null, null },
                    { 8, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 3, 3000.0, new Guid("00000000-0000-0000-0000-000000000019"), "Board member invitations", null, null },
                    { 9, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 3, 4000.0, new Guid("00000000-0000-0000-0000-00000000001a"), "Label filter in the top bar", null, null },
                    { 10, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 4, 1000.0, new Guid("00000000-0000-0000-0000-00000000001b"), "Keyboard shortcuts pass", null, null },
                    { 11, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 4, 2000.0, new Guid("00000000-0000-0000-0000-00000000001c"), "Activity feed pagination", null, null },
                    { 12, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 5, 1000.0, new Guid("00000000-0000-0000-0000-00000000001d"), "Launch messaging brief", null, null },
                    { 13, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 5, 2000.0, new Guid("00000000-0000-0000-0000-00000000001e"), "Press kit assets", null, null },
                    { 14, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 5, 3000.0, new Guid("00000000-0000-0000-0000-00000000001f"), "Social calendar", null, null },
                    { 15, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 5, 4000.0, new Guid("00000000-0000-0000-0000-000000000020"), "Partner co-marketing outreach", null, null },
                    { 16, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 6, 1000.0, new Guid("00000000-0000-0000-0000-000000000021"), "Update FAQ for v2 pricing", null, null },
                    { 17, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 6, 2000.0, new Guid("00000000-0000-0000-0000-000000000022"), "Escalation runbook review", null, null }
                });

            // specs/003-board-view-readonly/data-model.md Seed data: due dates are computed
            // relative to migration-apply time (DateTime.UtcNow, captured once here) rather
            // than baked into the InsertData literals above, so the seeded future/soon/
            // overdue dueStatus buckets (contracts/board-content-api.md) stay correct
            // whenever this migration actually runs instead of freezing a calendar date at
            // scaffold time that would drift stale within days.
            var seedNow = DateTime.UtcNow;
            SetCardDueAt(migrationBuilder, CardConfiguration.DefineSsoRequirementsId, seedNow.AddDays(14));
            SetCardDueAt(migrationBuilder, CardConfiguration.CardDetailRedesignId, seedNow.AddDays(10));
            SetCardDueAt(migrationBuilder, CardConfiguration.DragDropPerformanceId, seedNow.AddDays(1));
            SetCardDueAt(migrationBuilder, CardConfiguration.BoardMemberInvitationsId, seedNow.AddDays(7));
            SetCardDueAt(migrationBuilder, CardConfiguration.KeyboardShortcutsPassId, seedNow.AddDays(-3));

            migrationBuilder.InsertData(
                table: "CardLabel",
                columns: new[] { "Id", "CardId", "CreatedBy", "CreatedDate", "LabelId" },
                values: new object[,]
                {
                    { 1, 1, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 2, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2 },
                    { 3, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3 },
                    { 4, 4, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2 },
                    { 5, 5, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2 },
                    { 6, 6, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4 },
                    { 7, 6, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5 },
                    { 8, 7, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3 },
                    { 9, 7, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4 },
                    { 10, 8, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 11, 9, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 12, 10, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 13, 11, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4 }
                });

            migrationBuilder.InsertData(
                table: "CardMember",
                columns: new[] { "Id", "CardId", "CreatedBy", "CreatedDate", "UserId" },
                values: new object[,]
                {
                    { 1, 2, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5 },
                    { 2, 4, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5 },
                    { 3, 5, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3 },
                    { 4, 6, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4 },
                    { 5, 7, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3 },
                    { 6, 8, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2 },
                    { 7, 8, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3 },
                    { 8, 9, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5 },
                    { 9, 10, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 6 },
                    { 10, 11, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4 }
                });

            migrationBuilder.InsertData(
                table: "ChecklistItem",
                columns: new[] { "Id", "CardId", "CreatedBy", "CreatedDate", "Done", "Position", "Text" },
                values: new object[,]
                {
                    { 1, 4, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, 1000.0, "Wireframes" },
                    { 2, 4, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, 2000.0, "Visual design" }
                });

            migrationBuilder.InsertData(
                table: "ChecklistItem",
                columns: new[] { "Id", "CardId", "CreatedBy", "CreatedDate", "Position", "Text" },
                values: new object[] { 3, 4, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3000.0, "Prototype interactions" });

            migrationBuilder.InsertData(
                table: "ChecklistItem",
                columns: new[] { "Id", "CardId", "CreatedBy", "CreatedDate", "Done", "Position", "Text" },
                values: new object[] { 4, 6, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, 1000.0, "Profile virtualized rendering" });

            migrationBuilder.InsertData(
                table: "ChecklistItem",
                columns: new[] { "Id", "CardId", "CreatedBy", "CreatedDate", "Position", "Text" },
                values: new object[,]
                {
                    { 5, 6, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2000.0, "Test with 500+ card board" },
                    { 6, 7, "MIGRATION", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1000.0, "Document top 3 competitor flows" }
                });

            migrationBuilder.InsertData(
                table: "Comment",
                columns: new[] { "Id", "AuthorId", "Body", "CardId", "CreatedBy", "CreatedDate" },
                values: new object[,]
                {
                    { 1, 4, "Repro steps attached in the linked doc.", 6, "00000000-0000-0000-0000-000000000006", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, 3, "Started a competitor matrix, will share by EOW.", 7, "00000000-0000-0000-0000-000000000005", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Card_DueAt",
                table: "Card",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_Card_ListId",
                table: "Card",
                column: "ListId");

            migrationBuilder.CreateIndex(
                name: "IX_Card_ListId_Position",
                table: "Card",
                columns: new[] { "ListId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Card_PublicId",
                table: "Card",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardLabel_CardId_LabelId",
                table: "CardLabel",
                columns: new[] { "CardId", "LabelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardLabel_LabelId",
                table: "CardLabel",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_CardMember_CardId_UserId",
                table: "CardMember",
                columns: new[] { "CardId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardMember_UserId",
                table: "CardMember",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistItem_CardId",
                table: "ChecklistItem",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_Comment_AuthorId",
                table: "Comment",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Comment_CardId",
                table: "Comment",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_Label_BoardId",
                table: "Label",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_Label_PublicId",
                table: "Label",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_List_BoardId",
                table: "List",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_List_BoardId_Position",
                table: "List",
                columns: new[] { "BoardId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_List_PublicId",
                table: "List",
                column: "PublicId",
                unique: true);
        }

        private static void SetCardDueAt(MigrationBuilder migrationBuilder, int cardId, DateTime dueAt)
        {
            migrationBuilder.Sql(
                $"UPDATE [Card] SET [DueAt] = '{dueAt:yyyy-MM-ddTHH:mm:ss.fffffff}' WHERE [Id] = {cardId};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardLabel");

            migrationBuilder.DropTable(
                name: "CardMember");

            migrationBuilder.DropTable(
                name: "ChecklistItem");

            migrationBuilder.DropTable(
                name: "Comment");

            migrationBuilder.DropTable(
                name: "Label");

            migrationBuilder.DropTable(
                name: "Card");

            migrationBuilder.DropTable(
                name: "List");

            migrationBuilder.DeleteData(
                table: "Board",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Board",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "BoardMember",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "BoardMember",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "BoardMember",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "BoardMember",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "BoardMember",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Board",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "User",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "User",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "User",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "User",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "User",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DropColumn(
                name: "Color",
                table: "Board");

            migrationBuilder.DropColumn(
                name: "Starred",
                table: "Board");
        }
    }
}
