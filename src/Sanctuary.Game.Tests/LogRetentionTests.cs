using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Configuration;

namespace Sanctuary.Game.Tests;

/// <summary>Old log files are deleted, recent ones and the Audit log kept (<see cref="LogRetention"/>).</summary>
[TestClass]
public sealed class LogRetentionTests
{
    [TestMethod]
    public void FilesOlderThanTheCutoffAreDeleted()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"evergrove-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

            string Write(string name, int daysOld)
            {
                var path = Path.Combine(directory, name);
                File.WriteAllText(path, "line");
                File.SetLastWriteTimeUtc(path, now.AddDays(-daysOld));
                return path;
            }

            var oldChat = Write("Chat-2026-10-01.log", 53);
            var oldError = Write("Sanctuary.Gateway-Error-2026-10-20.log", 34);
            var recentChat = Write("Chat-2026-11-01.log", 22);
            var today = Write("Chat-2026-11-23.log", 0);
            var oldAudit = Write("Audit-2026-09-01.log", 83);
            var notALog = Write("notes.txt", 90);

            var deleted = LogRetention.DeleteOlderThan(directory, now.AddDays(-LogRetention.DefaultDays));

            Assert.AreEqual(2, deleted);
            Assert.IsFalse(File.Exists(oldChat));
            Assert.IsFalse(File.Exists(oldError));
            Assert.IsTrue(File.Exists(recentChat));
            Assert.IsTrue(File.Exists(today));
            Assert.IsTrue(File.Exists(oldAudit), "staff command logs are kept");
            Assert.IsTrue(File.Exists(notALog), "only .log files");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void AMissingFolderIsNothingToDo() =>
        Assert.AreEqual(0, LogRetention.DeleteOlderThan(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"), DateTime.UtcNow));
}
