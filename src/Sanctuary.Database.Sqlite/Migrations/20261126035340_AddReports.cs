using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<byte>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReporterUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ReporterCharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ReporterName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TargetCharacterId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    TargetUserId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    HouseId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    Zone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Position = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClientText = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ServerContext = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Status = table.Column<byte>(type: "INTEGER", nullable: false),
                    ClosedByUserId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CloseNote = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReporterUserId_Created",
                table: "Reports",
                columns: new[] { "ReporterUserId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Status_Created",
                table: "Reports",
                columns: new[] { "Status", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_TargetUserId",
                table: "Reports",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reports");
        }
    }
}
