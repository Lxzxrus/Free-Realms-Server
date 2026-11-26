using System;
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
/// AddStaffActions adds the staff log table and the ban and mute reasons. It only adds: accounts stay as they were,
/// with no reasons.
/// </summary>
[TestClass]
public class AddStaffActionsMigrationTests
{
    private const string MigrationName = "AddStaffActions";

    private static IEnumerable<object[]> Migrations()
    {
        yield return [new Sanctuary.Database.MySql.Migrations.AddStaffActions()];
        yield return [new Sanctuary.Database.Sqlite.Migrations.AddStaffActions()];
    }

    [TestMethod]
    [DynamicData(nameof(Migrations))]
    public void AddsTheTableAndTheReasons_AndDropsNothingThatExisted(Migration migration)
    {
        var operations = migration.UpOperations;

        var table = operations.OfType<CreateTableOperation>().Single();
        Assert.AreEqual("StaffActions", table.Name);
        CollectionAssert.AreEquivalent(
            new[] { "Action", "Command", "Created", "Details", "Id", "StaffName", "StaffUserId", "TargetCharacterId", "TargetName", "TargetUserId" },
            table.Columns.Select(column => column.Name).ToList());
        Assert.AreEqual(0, table.ForeignKeys.Count, "History has no foreign keys.");

        var indexes = operations.OfType<CreateIndexOperation>().Select(index => string.Join(",", index.Columns)).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "Created", "TargetUserId,Created" }, indexes);

        var reasons = operations.OfType<AddColumnOperation>().ToList();
        CollectionAssert.AreEquivalent(new[] { "BanReason", "MuteReason" }, reasons.Select(column => column.Name).ToList());
        Assert.IsTrue(reasons.All(column => column.Table == "Users" && column.IsNullable && column.MaxLength == 200));

        Assert.IsFalse(operations.OfType<DropColumnOperation>().Any(), "Nothing is dropped.");
        Assert.IsFalse(operations.OfType<DropTableOperation>().Any(), "Nothing is dropped.");
    }

    [TestMethod]
    public void KeepsEveryAccount_AndTheNewTableWorks_OnSqlite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder().UseSqlite(connection).Options;

        using (var dbContext = new SqliteDatabaseContext(options))
        {
            // The schema before AddStaffActions, with accounts, one of them banned.
            var migrations = dbContext.Database.GetMigrations().ToList();
            var index = migrations.FindIndex(migration => migration.EndsWith("_" + MigrationName));
            Assert.IsTrue(index > 0, "The migration exists and isn't the first.");

            var migrator = dbContext.GetService<IMigrator>();
            migrator.Migrate(migrations[index - 1]);

            dbContext.Database.ExecuteSqlRaw(
                "INSERT INTO Users (Id, Username, Password, MaxCharacters, IsMember, IsAdmin, IsMod, Created, LockedUntil) VALUES " +
                "(1, 'player', 'x', 10, 0, 0, 0, '2026-01-01', NULL), " +
                "(2, 'banned', 'x', 10, 0, 0, 0, '2026-01-01', '9999-12-31 23:59:59+00:00')");

            migrator.Migrate();
        }

        using (var dbContext = new SqliteDatabaseContext(options))
        {
            var users = dbContext.Users.AsNoTracking().OrderBy(user => user.Username).ToList();

            CollectionAssert.AreEqual(new[] { "banned", "player" }, users.Select(user => user.Username).ToList(), "Every account is kept.");
            Assert.IsTrue(users.All(user => user.BanReason is null && user.MuteReason is null), "No account has a reason yet.");
            Assert.IsNotNull(users.Single(user => user.Username == "banned").LockedUntil, "The ban is kept.");

            var banned = users.Single(user => user.Username == "banned");
            dbContext.Users.Where(user => user.Id == banned.Id).ExecuteUpdate(user => user.SetProperty(u => u.BanReason, "spam"));

            dbContext.StaffActions.Add(new DbStaffAction
            {
                Created = new DateTimeOffset(2026, 11, 25, 14, 3, 0, TimeSpan.Zero),
                StaffUserId = 1,
                StaffName = "Ariel Mod",
                Command = "mod",
                Action = "Ban",
                TargetUserId = banned.Id,
                Details = "Permanent; Reason: spam"
            });
            dbContext.SaveChanges();
        }

        using (var dbContext = new SqliteDatabaseContext(options))
        {
            Assert.AreEqual("spam", dbContext.Users.Single(user => user.Username == "banned").BanReason);
            Assert.AreEqual("Ban", dbContext.StaffActions.Single().Action);
        }
    }
}
