using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddFixturePlacedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "PlacedByCharacterId",
                table: "HouseFixtures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);

            // Until houses were shared, only the character that bought a house could build in it, so that character
            // placed every fixture already there.
            migrationBuilder.Sql(
                "UPDATE HouseFixtures SET PlacedByCharacterId = " +
                "(SELECT Houses.CharacterId FROM Houses WHERE Houses.Id = HouseFixtures.HouseId)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlacedByCharacterId",
                table: "HouseFixtures");
        }
    }
}
