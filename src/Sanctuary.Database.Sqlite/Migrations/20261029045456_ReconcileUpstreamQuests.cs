using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <summary>
    /// Evergrove's quest tables become upstream OSFR's (Sulphural's quest PRs #130-#162): Evergrove created
    /// CharacterQuests in 20260811000000_AddCharacterQuests with JadenY's earlier GoalProgress and GoalCount, which
    /// upstream replaced with CurrentGoalId, Started and a CharacterQuestProgress table. Written for the merge of
    /// upstream into Evergrove in place of upstream's own AddCharacterQuests and AddCharacterQuestStarted, which
    /// would create tables Evergrove databases already have.
    /// </summary>
    public partial class ReconcileUpstreamQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentGoalId",
                table: "CharacterQuests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Started",
                table: "CharacterQuests",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "DATE()");

            migrationBuilder.CreateTable(
                name: "CharacterQuestProgress",
                columns: table => new
                {
                    QuestId = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    GoalId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterQuestProgress", x => new { x.CharacterId, x.QuestId, x.GoalId, x.TargetId });
                    table.ForeignKey(
                        name: "FK_CharacterQuestProgress_CharacterQuests_QuestId_CharacterId",
                        columns: x => new { x.QuestId, x.CharacterId },
                        principalTable: "CharacterQuests",
                        principalColumns: new[] { "QuestId", "CharacterId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterQuestProgress_QuestId_CharacterId",
                table: "CharacterQuestProgress",
                columns: new[] { "QuestId", "CharacterId" });

            // SQLite is for tests and local runs: its in-progress quests restart at their first goal (CurrentGoalId 0
            // would mean "done", so -1). The live MariaDB migration carries progress over.
            migrationBuilder.Sql("UPDATE CharacterQuests SET CurrentGoalId = -1 WHERE Completed = 0;");

            migrationBuilder.DropColumn(
                name: "GoalCount",
                table: "CharacterQuests");

            migrationBuilder.DropColumn(
                name: "GoalProgress",
                table: "CharacterQuests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterQuestProgress");

            migrationBuilder.DropColumn(
                name: "Started",
                table: "CharacterQuests");

            migrationBuilder.DropColumn(
                name: "CurrentGoalId",
                table: "CharacterQuests");

            migrationBuilder.AddColumn<int>(
                name: "GoalProgress",
                table: "CharacterQuests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GoalCount",
                table: "CharacterQuests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
