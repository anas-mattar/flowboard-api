using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCardActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "ChecklistItem",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ActivityEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CardId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityEvent_Card_CardId",
                        column: x => x.CardId,
                        principalTable: "Card",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityEvent_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "ChecklistItem",
                keyColumn: "Id",
                keyValue: 1,
                column: "PublicId",
                value: new Guid("00000000-0000-0000-0000-000000000030"));

            migrationBuilder.UpdateData(
                table: "ChecklistItem",
                keyColumn: "Id",
                keyValue: 2,
                column: "PublicId",
                value: new Guid("00000000-0000-0000-0000-000000000031"));

            migrationBuilder.UpdateData(
                table: "ChecklistItem",
                keyColumn: "Id",
                keyValue: 3,
                column: "PublicId",
                value: new Guid("00000000-0000-0000-0000-000000000032"));

            migrationBuilder.UpdateData(
                table: "ChecklistItem",
                keyColumn: "Id",
                keyValue: 4,
                column: "PublicId",
                value: new Guid("00000000-0000-0000-0000-000000000033"));

            migrationBuilder.UpdateData(
                table: "ChecklistItem",
                keyColumn: "Id",
                keyValue: 5,
                column: "PublicId",
                value: new Guid("00000000-0000-0000-0000-000000000034"));

            migrationBuilder.UpdateData(
                table: "ChecklistItem",
                keyColumn: "Id",
                keyValue: 6,
                column: "PublicId",
                value: new Guid("00000000-0000-0000-0000-000000000035"));

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistItem_PublicId",
                table: "ChecklistItem",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvent_ActorId",
                table: "ActivityEvent",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvent_CardId_CreatedDate",
                table: "ActivityEvent",
                columns: new[] { "CardId", "CreatedDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvent");

            migrationBuilder.DropIndex(
                name: "IX_ChecklistItem_PublicId",
                table: "ChecklistItem");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "ChecklistItem");
        }
    }
}
