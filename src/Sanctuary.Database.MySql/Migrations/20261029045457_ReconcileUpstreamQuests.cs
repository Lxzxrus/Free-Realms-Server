using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.MySql.Migrations
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
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Started",
                table: "CharacterQuests",
                type: "datetime(6)",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.CreateTable(
                name: "CharacterQuestProgress",
                columns: table => new
                {
                    QuestId = table.Column<int>(type: "int", nullable: false),
                    CharacterId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GoalId = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterQuestProgress_QuestId_CharacterId",
                table: "CharacterQuestProgress",
                columns: new[] { "QuestId", "CharacterId" });

            // Live characters keep their place in each quest: Evergrove's goal index becomes upstream's goal NameId
            // (0 = every goal done, waiting to be turned in; -1 = unknown, which restarts the quest at its first
            // goal), and pickups already made on the current goal carry over as an anonymous count.
            migrationBuilder.Sql(@"UPDATE CharacterQuests SET CurrentGoalId = CASE
    WHEN QuestId = 2563 AND GoalProgress = 0 THEN 94359
    WHEN QuestId = 2563 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 1801 AND GoalProgress = 0 THEN 100103
    WHEN QuestId = 1801 AND GoalProgress = 1 THEN 94511
    WHEN QuestId = 1801 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 2564 AND GoalProgress = 0 THEN 115453
    WHEN QuestId = 2564 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3001 AND GoalProgress = 0 THEN 46621
    WHEN QuestId = 3001 AND GoalProgress = 1 THEN 74455
    WHEN QuestId = 3001 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3002 AND GoalProgress = 0 THEN 46489
    WHEN QuestId = 3002 AND GoalProgress = 1 THEN 74455
    WHEN QuestId = 3002 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3011 AND GoalProgress = 0 THEN 390850
    WHEN QuestId = 3011 AND GoalProgress = 1 THEN 390804
    WHEN QuestId = 3011 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3020 AND GoalProgress = 0 THEN 93294
    WHEN QuestId = 3020 AND GoalProgress = 1 THEN 93310
    WHEN QuestId = 3020 AND GoalProgress = 2 THEN 101743
    WHEN QuestId = 3020 AND GoalProgress >= 3 THEN 0
    WHEN QuestId = 3021 AND GoalProgress = 0 THEN 93405
    WHEN QuestId = 3021 AND GoalProgress = 1 THEN 93407
    WHEN QuestId = 3021 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3022 AND GoalProgress = 0 THEN 93425
    WHEN QuestId = 3022 AND GoalProgress = 1 THEN 93427
    WHEN QuestId = 3022 AND GoalProgress = 2 THEN 93429
    WHEN QuestId = 3022 AND GoalProgress = 3 THEN 93407
    WHEN QuestId = 3022 AND GoalProgress >= 4 THEN 0
    WHEN QuestId = 3023 AND GoalProgress = 0 THEN 93454
    WHEN QuestId = 3023 AND GoalProgress = 1 THEN 93457
    WHEN QuestId = 3023 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3032 AND GoalProgress = 0 THEN 38049
    WHEN QuestId = 3032 AND GoalProgress = 1 THEN 38051
    WHEN QuestId = 3032 AND GoalProgress = 2 THEN 38053
    WHEN QuestId = 3032 AND GoalProgress = 3 THEN 390017
    WHEN QuestId = 3032 AND GoalProgress >= 4 THEN 0
    WHEN QuestId = 3033 AND GoalProgress = 0 THEN 38154
    WHEN QuestId = 3033 AND GoalProgress = 1 THEN 38156
    WHEN QuestId = 3033 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3040 AND GoalProgress = 0 THEN 38393
    WHEN QuestId = 3040 AND GoalProgress = 1 THEN 38400
    WHEN QuestId = 3040 AND GoalProgress = 2 THEN 38442
    WHEN QuestId = 3040 AND GoalProgress = 3 THEN 38515
    WHEN QuestId = 3040 AND GoalProgress = 4 THEN 38518
    WHEN QuestId = 3040 AND GoalProgress >= 5 THEN 0
    WHEN QuestId = 3041 AND GoalProgress = 0 THEN 70110
    WHEN QuestId = 3041 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3042 AND GoalProgress = 0 THEN 70221
    WHEN QuestId = 3042 AND GoalProgress = 1 THEN 70227
    WHEN QuestId = 3042 AND GoalProgress = 2 THEN 70230
    WHEN QuestId = 3042 AND GoalProgress >= 3 THEN 0
    WHEN QuestId = 3043 AND GoalProgress = 0 THEN 70289
    WHEN QuestId = 3043 AND GoalProgress = 1 THEN 70291
    WHEN QuestId = 3043 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3060 AND GoalProgress = 0 THEN 101402
    WHEN QuestId = 3060 AND GoalProgress = 1 THEN 101404
    WHEN QuestId = 3060 AND GoalProgress = 2 THEN 101406
    WHEN QuestId = 3060 AND GoalProgress >= 3 THEN 0
    WHEN QuestId = 3061 AND GoalProgress = 0 THEN 400357
    WHEN QuestId = 3061 AND GoalProgress = 1 THEN 400426
    WHEN QuestId = 3061 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3070 AND GoalProgress = 0 THEN 385602
    WHEN QuestId = 3070 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3071 AND GoalProgress = 0 THEN 74204
    WHEN QuestId = 3071 AND GoalProgress = 1 THEN 74206
    WHEN QuestId = 3071 AND GoalProgress = 2 THEN 74208
    WHEN QuestId = 3071 AND GoalProgress = 3 THEN 74210
    WHEN QuestId = 3071 AND GoalProgress = 4 THEN 74212
    WHEN QuestId = 3071 AND GoalProgress >= 5 THEN 0
    WHEN QuestId = 3072 AND GoalProgress = 0 THEN 41752
    WHEN QuestId = 3072 AND GoalProgress = 1 THEN 41849
    WHEN QuestId = 3072 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3073 AND GoalProgress = 0 THEN 41867
    WHEN QuestId = 3073 AND GoalProgress = 1 THEN 41849
    WHEN QuestId = 3073 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3074 AND GoalProgress = 0 THEN 46499
    WHEN QuestId = 3074 AND GoalProgress = 1 THEN 46501
    WHEN QuestId = 3074 AND GoalProgress = 2 THEN 46503
    WHEN QuestId = 3074 AND GoalProgress = 3 THEN 46505
    WHEN QuestId = 3074 AND GoalProgress = 4 THEN 46507
    WHEN QuestId = 3074 AND GoalProgress = 5 THEN 46598
    WHEN QuestId = 3074 AND GoalProgress >= 6 THEN 0
    WHEN QuestId = 3075 AND GoalProgress = 0 THEN 387039
    WHEN QuestId = 3075 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3076 AND GoalProgress = 0 THEN 387049
    WHEN QuestId = 3076 AND GoalProgress = 1 THEN 71001
    WHEN QuestId = 3076 AND GoalProgress = 2 THEN 387053
    WHEN QuestId = 3076 AND GoalProgress = 3 THEN 387055
    WHEN QuestId = 3076 AND GoalProgress >= 4 THEN 0
    WHEN QuestId = 3077 AND GoalProgress = 0 THEN 387072
    WHEN QuestId = 3077 AND GoalProgress = 1 THEN 387074
    WHEN QuestId = 3077 AND GoalProgress = 2 THEN 387076
    WHEN QuestId = 3077 AND GoalProgress >= 3 THEN 0
    WHEN QuestId = 3080 AND GoalProgress = 0 THEN 440471
    WHEN QuestId = 3080 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3090 AND GoalProgress = 0 THEN 46618
    WHEN QuestId = 3090 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3091 AND GoalProgress = 0 THEN 387480
    WHEN QuestId = 3091 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3092 AND GoalProgress = 0 THEN 36445
    WHEN QuestId = 3092 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3100 AND GoalProgress = 0 THEN 385984
    WHEN QuestId = 3100 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3101 AND GoalProgress = 0 THEN 71711
    WHEN QuestId = 3101 AND GoalProgress = 1 THEN 71713
    WHEN QuestId = 3101 AND GoalProgress = 2 THEN 71719
    WHEN QuestId = 3101 AND GoalProgress >= 3 THEN 0
    WHEN QuestId = 3102 AND GoalProgress = 0 THEN 71872
    WHEN QuestId = 3102 AND GoalProgress = 1 THEN 71681
    WHEN QuestId = 3102 AND GoalProgress = 2 THEN 71715
    WHEN QuestId = 3102 AND GoalProgress >= 3 THEN 0
    WHEN QuestId = 3103 AND GoalProgress = 0 THEN 386167
    WHEN QuestId = 3103 AND GoalProgress = 1 THEN 386178
    WHEN QuestId = 3103 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3104 AND GoalProgress = 0 THEN 74326
    WHEN QuestId = 3104 AND GoalProgress = 1 THEN 74328
    WHEN QuestId = 3104 AND GoalProgress >= 2 THEN 0
    WHEN QuestId = 3105 AND GoalProgress = 0 THEN 74260
    WHEN QuestId = 3105 AND GoalProgress >= 1 THEN 0
    WHEN QuestId = 3106 AND GoalProgress = 0 THEN 386253
    WHEN QuestId = 3106 AND GoalProgress >= 1 THEN 0
    ELSE -1 END
WHERE Completed = 0;");

            migrationBuilder.Sql(@"INSERT INTO CharacterQuestProgress (QuestId, CharacterId, GoalId, TargetId, Count)
SELECT QuestId, CharacterId, CurrentGoalId, 0, GoalCount
FROM CharacterQuests
WHERE Completed = 0 AND GoalCount > 0 AND CurrentGoalId > 0;");

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
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GoalCount",
                table: "CharacterQuests",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
