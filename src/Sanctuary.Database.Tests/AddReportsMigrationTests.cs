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

/// <summary>AddReports only adds the Reports table (and its indexes): nothing that existed is touched.</summary>
[TestClass]
public class AddReportsMigrationTests
{
    private static IEnumerable<object[]> Migrations()
    {
        yield return [new Sanctuary.Database.MySql.Migrations.AddReports()];
        yield return [new Sanctuary.Database.Sqlite.Migrations.AddReports()];
    }

    [TestMethod]
    [DynamicData(nameof(Migrations))]
    public void OnlyAddsTheReportsTable(Migration migration)
    {
        var operations = migration.UpOperations;

        Assert.AreEqual(1 + operations.OfType<CreateIndexOperation>().Count(), operations.Count, "Only a table and its indexes.");

        var table = operations.OfType<CreateTableOperation>().Single();
        Assert.AreEqual("Reports", table.Name);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "Id", "Kind", "Created", "ReporterUserId", "ReporterCharacterId", "ReporterName", "TargetName", "TargetCharacterId", "TargetUserId",
                "HouseId", "Zone", "Position", "ClientText", "ServerContext", "Status", "ClosedByUserId", "ClosedAt", "CloseNote"
            },
            table.Columns.Select(column => column.Name).ToList());
        Assert.AreEqual(0, table.ForeignKeys.Count, "History has no foreign keys.");

        var indexes = operations.OfType<CreateIndexOperation>().Select(index => string.Join(",", index.Columns)).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "ReporterUserId,Created", "Status,Created", "TargetUserId" }, indexes);

        Assert.AreEqual(4000, table.Columns.Single(column => column.Name == "ClientText").MaxLength);
        Assert.AreEqual(4000, table.Columns.Single(column => column.Name == "ServerContext").MaxLength);
        Assert.AreEqual(200, table.Columns.Single(column => column.Name == "CloseNote").MaxLength);
    }

    [TestMethod]
    public void KeepsEveryAccount_AndTheNewTableWorks_OnSqlite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder().UseSqlite(connection).Options;

        using (var dbContext = new SqliteDatabaseContext(options))
        {
            var migrations = dbContext.Database.GetMigrations().ToList();
            var index = migrations.FindIndex(migration => migration.EndsWith("_AddReports"));
            Assert.IsTrue(index > 0, "The migration exists and isn't the first.");

            var migrator = dbContext.GetService<IMigrator>();
            migrator.Migrate(migrations[index - 1]);

            dbContext.Database.ExecuteSqlRaw(
                "INSERT INTO Users (Id, Username, Password, MaxCharacters, IsMember, IsAdmin, IsMod, Created) VALUES (1, 'player', 'x', 10, 0, 0, 0, '2026-01-01')");

            migrator.Migrate();
        }

        using (var dbContext = new SqliteDatabaseContext(options))
        {
            Assert.AreEqual("player", dbContext.Users.Single().Username);

            dbContext.Reports.Add(new DbReport
            {
                Kind = ReportKind.House,
                Created = new DateTimeOffset(2026, 11, 25, 14, 3, 0, TimeSpan.Zero),
                ReporterUserId = 1,
                ReporterCharacterId = 10,
                ReporterName = "Rita Reporter",
                HouseId = 5
            });
            dbContext.SaveChanges();
        }

        using (var dbContext = new SqliteDatabaseContext(options))
        {
            var report = dbContext.Reports.Single();

            Assert.AreEqual(ReportKind.House, report.Kind);
            Assert.AreEqual(ReportStatus.Open, report.Status);
            Assert.AreEqual(5UL, report.HouseId);
        }
    }
}
