using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BanReason",
                table: "Users",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MuteReason",
                table: "Users",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StaffActions",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StaffUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    StaffName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Command = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TargetUserId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    TargetCharacterId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    TargetName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffActions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffActions_Created",
                table: "StaffActions",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_StaffActions_TargetUserId_Created",
                table: "StaffActions",
                columns: new[] { "TargetUserId", "Created" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffActions");

            migrationBuilder.DropColumn(
                name: "BanReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MuteReason",
                table: "Users");
        }
    }
}
