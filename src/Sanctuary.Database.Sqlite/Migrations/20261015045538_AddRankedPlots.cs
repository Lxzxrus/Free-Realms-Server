using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedPlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The plain average and vote count were a cache of HouseVotes, which stays as history. The weekly score is
            // worked out from PlotVotes when it's shown. EF scaffolds this as a rename of Votes to Plot, which would
            // make every house with one vote a ranked plot.
            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "Votes",
                table: "Houses");

            // Every existing house is a personal plot (HousePlot.Personal).
            migrationBuilder.AddColumn<int>(
                name: "Plot",
                table: "Houses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RankedSince",
                table: "Houses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlotVotes",
                columns: table => new
                {
                    HouseId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    WeekStart = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    Updated = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlotVotes", x => new { x.HouseId, x.UserId, x.WeekStart });
                    table.ForeignKey(
                        name: "FK_PlotVotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlotVotes_UserId",
                table: "PlotVotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlotVotes_WeekStart_HouseId",
                table: "PlotVotes",
                columns: new[] { "WeekStart", "HouseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlotVotes");

            migrationBuilder.DropColumn(
                name: "RankedSince",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "Plot",
                table: "Houses");

            migrationBuilder.AddColumn<int>(
                name: "Votes",
                table: "Houses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "Rating",
                table: "Houses",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            // Rebuild the old cache from the votes it was made from, which this migration kept.
            migrationBuilder.Sql(
                "UPDATE Houses SET " +
                "Votes = (SELECT COUNT(*) FROM HouseVotes WHERE HouseVotes.HouseId = Houses.Id), " +
                "Rating = COALESCE((SELECT AVG(Value) FROM HouseVotes WHERE HouseVotes.HouseId = Houses.Id), 0)");
        }
    }
}
