using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <summary>
    /// Upstream OSFR's quest collections (Sulphural's PR #166) gave the first goal of Hasti's Homework Heist (3002) the
    /// client's own text, NameId 74443, where it was 46489. A quest's progress is stored under its goals' NameIds
    /// (CharacterQuests.CurrentGoalId and CharacterQuestProgress.GoalId, since ReconcileUpstreamQuests), so a character
    /// gathering the homework would restart the quest and lose the pages already found. This moves them to the new id;
    /// it changes nothing when no character is on that goal. No other goal NameId changed in that merge.
    /// </summary>
    public partial class ReconcileUpstreamQuestGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            MoveGoal(migrationBuilder, questId: 3002, fromGoalId: 46489, toGoalId: 74443);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            MoveGoal(migrationBuilder, questId: 3002, fromGoalId: 74443, toGoalId: 46489);
        }

        private static void MoveGoal(MigrationBuilder migrationBuilder, int questId, int fromGoalId, int toGoalId)
        {
            // A progress row already under the new id (only a server that ran the new quest data before this migration
            // could have one) is kept, and the old row with the same key dropped, so the key can't collide.
            migrationBuilder.Sql($@"DELETE FROM CharacterQuestProgress
WHERE QuestId = {questId} AND GoalId = {fromGoalId} AND EXISTS (
    SELECT 1 FROM CharacterQuestProgress AS kept
    WHERE kept.CharacterId = CharacterQuestProgress.CharacterId AND kept.QuestId = CharacterQuestProgress.QuestId
        AND kept.TargetId = CharacterQuestProgress.TargetId AND kept.GoalId = {toGoalId});");

            migrationBuilder.Sql($@"UPDATE CharacterQuestProgress SET GoalId = {toGoalId}
WHERE QuestId = {questId} AND GoalId = {fromGoalId};");

            migrationBuilder.Sql($@"UPDATE CharacterQuests SET CurrentGoalId = {toGoalId}
WHERE QuestId = {questId} AND CurrentGoalId = {fromGoalId} AND Completed = 0;");
        }
    }
}
