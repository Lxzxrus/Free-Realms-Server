using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.Database.Tests;

/// <summary>
/// <c>dotnet ef migrations add</c> scaffolds AddRankedPlots as a rename of <c>Houses.Votes</c> to <c>Houses.Plot</c>,
/// which would turn every house with one old vote into a ranked plot. The migration is hand-corrected to drop the old
/// columns and add <c>Plot</c> as personal, and these checks catch a regenerated copy that wasn't, on both providers.
/// </summary>
[TestClass]
public class AddRankedPlotsMigrationTests
{
    private static IEnumerable<object[]> Migrations()
    {
        yield return [new Sanctuary.Database.MySql.Migrations.AddRankedPlots()];
        yield return [new Sanctuary.Database.Sqlite.Migrations.AddRankedPlots()];
    }

    [TestMethod]
    [DynamicData(nameof(Migrations))]
    public void ExistingHousesBecomePersonal_NotRenamedFromVotes(Migration migration)
    {
        var operations = migration.UpOperations;

        Assert.IsFalse(operations.OfType<RenameColumnOperation>().Any(), "Votes must not be renamed to Plot.");

        var dropped = operations.OfType<DropColumnOperation>()
            .Where(operation => operation.Table == "Houses")
            .Select(operation => operation.Name)
            .ToList();
        CollectionAssert.IsSubsetOf(new[] { "Votes", "Rating" }, dropped);

        var plot = operations.OfType<AddColumnOperation>().Single(operation => operation.Table == "Houses" && operation.Name == "Plot");
        Assert.AreEqual(0, plot.DefaultValue, "Every existing house is personal.");
        Assert.IsFalse(plot.IsNullable);
    }
}
