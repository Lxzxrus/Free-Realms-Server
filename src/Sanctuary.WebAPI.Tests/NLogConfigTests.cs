using System;
using System.IO;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using NLog;
using NLog.Config;
using NLog.Targets.Wrappers;

namespace Sanctuary.WebAPI.Tests;

/// <summary>
/// Each server's NLog.config loads in NLog itself (a config NLog can't read would leave a server logging nothing), and
/// its Info and Error files are behind a LimitingWrapper: Docker's cap covers only the console.
/// </summary>
[TestClass]
public sealed class NLogConfigTests
{
    [TestMethod]
    [DataRow("Sanctuary.Login")]
    [DataRow("Sanctuary.Gateway")]
    [DataRow("Sanctuary.WebAPI")]
    public void TheConfigLoadsAndLimitsItsFiles(string server)
    {
        var factory = new LogFactory { ThrowConfigExceptions = true };
        var configuration = new XmlLoggingConfiguration(FindSourceFile(server, "NLog.config"), factory);

        foreach (var name in new[] { "infoFile", "errorFile" })
        {
            var target = configuration.FindTargetByName(name);

            Assert.IsInstanceOfType<LimitingTargetWrapper>(target, $"{server}: {name}");
            var limit = ((LimitingTargetWrapper)target).MessageLimit.RenderValue(LogEventInfo.CreateNullEvent());
            Assert.IsTrue(limit is > 0 and <= 1000, $"{server}: {name} allows {limit} a minute");
        }

        Assert.IsTrue(configuration.LoggingRules.Count > 0, server);
    }

    private static string FindSourceFile(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"src/{string.Join('/', relativePath)} not found above the test output.");
    }
}
