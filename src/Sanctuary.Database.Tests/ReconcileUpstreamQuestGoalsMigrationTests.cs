using System.Collections.Generic;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;

namespace Sanctuary.Database.Tests;

/// <summary>
/// Upstream OSFR's #166 renamed the first goal of Hasti's Homework Heist (3002) from NameId 46489 to 74443, and a
/// quest's progress is stored under its goals' NameIds. ReconcileUpstreamQuestGoals moves characters on that goal to the
/// new id, with the pages they already found, and touches nothing else.
/// </summary>
[TestClass]
public class ReconcileUpstreamQuestGoalsMigrationTests
{
    private const string MigrationName = "ReconcileUpstreamQuestGoals";

    private const int Homework = 3002;
    private const int OldGoal = 46489;
    private const int NewGoal = 74443;
    private const int ReturnToHasti = 74455;

    private static IEnumerable<object[]> Migrations()
    {
        yield return [new Sanctuary.Database.MySql.Migrations.ReconcileUpstreamQuestGoals()];
        yield return [new Sanctuary.Database.Sqlite.Migrations.ReconcileUpstreamQuestGoals()];
    }

    [TestMethod]
    [DynamicData(nameof(Migrations))]
    public void ChangesOnlyData_AndOnlyHastisHomework(Migration migration)
    {
        var operations = migration.UpOperations;

        Assert.IsTrue(operations.All(operation => operation is SqlOperation), "No table or column changes.");

        var sql = string.Join("\n", operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        StringAssert.Contains(sql, $"UPDATE CharacterQuestProgress SET GoalId = {NewGoal}");
        StringAssert.Contains(sql, $"UPDATE CharacterQuests SET CurrentGoalId = {NewGoal}");
        StringAssert.Contains(sql, $"QuestId = {Homework} AND CurrentGoalId = {OldGoal}");

        var down = string.Join("\n", migration.DownOperations.OfType<SqlOperation>().Select(operation => operation.Sql));
        StringAssert.Contains(down, $"UPDATE CharacterQuests SET CurrentGoalId = {OldGoal}");
    }

    [TestMethod]
    public void MovesProgressOnTheRenamedGoal_OnSqlite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder().UseSqlite(connection).Options;

        // The schema as it stood before the migration, which changes data only.
        using (var dbContext = new SqliteDatabaseContext(options))
        {
            var migrations = dbContext.Database.GetMigrations().ToList();
            var index = migrations.FindIndex(migration => migration.EndsWith("_" + MigrationName));
            Assert.IsTrue(index > 0, "The migration exists and isn't the first.");

            dbContext.GetService<IMigrator>().Migrate(migrations[index - 1]);
        }

        using (var dbContext = new SqliteDatabaseContext(options))
        {
            var user = new DbUser { Id = 1, Username = "players", Password = string.Empty };

            // 10 has found three pages; 11 found every page and is on the way back to Hasti; 12 finished the quest
            // long ago; 13 is on another quest; 14 has a row under each id (a server that ran the new data early).
            var gathering = NewCharacter(10);
            gathering.Quests.Add(NewQuest(Homework, OldGoal, (OldGoal, 0, 3)));

            var returning = NewCharacter(11);
            returning.Quests.Add(NewQuest(Homework, ReturnToHasti, (OldGoal, 0, -1)));

            var done = NewCharacter(12);
            done.Quests.Add(new DbCharacterQuest { QuestId = Homework, Completed = true });

            var elsewhere = NewCharacter(13);
            elsewhere.Quests.Add(NewQuest(3001, 46621, (46621, 0, 1)));

            var both = NewCharacter(14);
            both.Quests.Add(NewQuest(Homework, OldGoal, (OldGoal, 0, 2), (NewGoal, 0, 5), (NewGoal, 4321, 1)));

            foreach (var character in new[] { gathering, returning, done, elsewhere, both })
                user.Characters.Add(character);

            dbContext.Users.Add(user);
            dbContext.SaveChanges();
        }

        using (var dbContext = new SqliteDatabaseContext(options))
            dbContext.GetService<IMigrator>().Migrate();

        using (var dbContext = new SqliteDatabaseContext(options))
        {
            Assert.AreEqual(NewGoal, CurrentGoal(dbContext, 10), "The gatherer stays on the homework goal.");
            CollectionAssert.AreEqual(new[] { (NewGoal, 0UL, 3) }, Progress(dbContext, 10), "Their three pages are kept.");

            Assert.AreEqual(ReturnToHasti, CurrentGoal(dbContext, 11));
            CollectionAssert.AreEqual(new[] { (NewGoal, 0UL, -1) }, Progress(dbContext, 11), "The done homework goal moves too.");

            Assert.AreEqual(0, CurrentGoal(dbContext, 12));
            Assert.IsTrue(dbContext.CharacterQuests.Single(quest => quest.CharacterId == 12).Completed);

            Assert.AreEqual(46621, CurrentGoal(dbContext, 13), "Other quests are untouched.");
            CollectionAssert.AreEqual(new[] { (46621, 0UL, 1) }, Progress(dbContext, 13));

            Assert.AreEqual(NewGoal, CurrentGoal(dbContext, 14));
            CollectionAssert.AreEqual(new[] { (NewGoal, 0UL, 5), (NewGoal, 4321UL, 1) }, Progress(dbContext, 14),
                "Rows already under the new id win; the old row with the same key goes.");
        }

        // And back: Down returns them to the old id.
        using (var dbContext = new SqliteDatabaseContext(options))
        {
            var migrations = dbContext.Database.GetMigrations().ToList();
            var index = migrations.FindIndex(migration => migration.EndsWith("_" + MigrationName));

            dbContext.GetService<IMigrator>().Migrate(migrations[index - 1]);
        }

        using (var dbContext = new SqliteDatabaseContext(options))
        {
            Assert.AreEqual(OldGoal, CurrentGoal(dbContext, 10));
            CollectionAssert.AreEqual(new[] { (OldGoal, 0UL, 3) }, Progress(dbContext, 10));
        }
    }

    private static int CurrentGoal(SqliteDatabaseContext dbContext, ulong characterId) =>
        dbContext.CharacterQuests.AsNoTracking().Single(quest => quest.CharacterId == characterId).CurrentGoalId;

    private static List<(int GoalId, ulong TargetId, int Count)> Progress(SqliteDatabaseContext dbContext, ulong characterId) =>
        dbContext.CharacterQuestProgress.AsNoTracking()
            .Where(progress => progress.CharacterId == characterId)
            .AsEnumerable()
            .OrderBy(progress => progress.GoalId).ThenBy(progress => progress.TargetId)
            .Select(progress => (progress.GoalId, progress.TargetId, progress.Count))
            .ToList();

    private static DbCharacterQuest NewQuest(int questId, int currentGoalId, params (int GoalId, ulong TargetId, int Count)[] progress)
    {
        var quest = new DbCharacterQuest { QuestId = questId, CurrentGoalId = currentGoalId };

        foreach (var (goalId, targetId, count) in progress)
            quest.Progress.Add(new DbCharacterQuestProgress { QuestId = questId, GoalId = goalId, TargetId = targetId, Count = count });

        return quest;
    }

    private static DbCharacter NewCharacter(ulong id) => new()
    {
        Id = id,
        FirstName = $"Character{id}",
        Head = string.Empty,
        HeadId = 0,
        Hair = string.Empty,
        HairId = 0,
        SkinTone = string.Empty,
        SkinToneId = 0
    };
}
