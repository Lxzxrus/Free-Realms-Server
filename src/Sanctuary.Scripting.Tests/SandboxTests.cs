using System.IO;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.Scripting.Tests;

/// <summary>
/// Scripts get only base, table, string and math (threat model F11).
/// </summary>
[TestClass]
public class SandboxTests
{
    private string _directory = null!;
    private ScriptContext _context = null!;

    [TestInitialize]
    public void Setup()
    {
        _directory = Directory.CreateTempSubdirectory("sanctuary-sandbox-").FullName;

        ILogger logger = NullLogger.Instance;
        var scriptManager = new ScriptManager(NullLoggerFactory.Instance);
        scriptManager.GetOrCreateContext(new MockScriptZone(logger), out _context);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private Task<bool> RunAsync(string name, string source)
    {
        var path = Path.Combine(_directory, name + ".lua");
        File.WriteAllText(path, source);

        return _context.LoadScriptAsync(path).AsTask();
    }

    [TestMethod]
    [DataRow("os")]
    [DataRow("io")]
    [DataRow("package")]
    [DataRow("debug")]
    [DataRow("require")]
    [DataRow("dofile")]
    [DataRow("loadfile")]
    public async Task UnsafeGlobalIsClosed(string name)
    {
        Assert.IsTrue(await RunAsync(name, $"assert({name} == nil, \"{name} is open\")"), $"{name} is reachable from a script.");
    }

    [TestMethod]
    public async Task FailedAssertFailsTheLoad()
    {
        // Control for UnsafeGlobalIsClosed: a script that fails an assert must not load.
        Assert.IsFalse(await RunAsync("control", "assert(math == nil)"));
    }

    [TestMethod]
    public async Task ScriptCannotWriteFiles()
    {
        var target = Path.Combine(_directory, "written.txt");
        var source = $"local f = io.open([[{target}]], \"w\") f:write(\"x\") f:close()";

        Assert.IsFalse(await RunAsync("write", source));
        Assert.IsFalse(File.Exists(target), "A script wrote a file to disk.");
    }

    [TestMethod]
    public async Task ScriptCannotDeleteFiles()
    {
        var target = Path.Combine(_directory, "keep.txt");
        File.WriteAllText(target, "x");

        Assert.IsFalse(await RunAsync("delete", $"os.remove([[{target}]])"));
        Assert.IsTrue(File.Exists(target), "A script deleted a file.");
    }

    [TestMethod]
    public async Task SafeLibrariesWork()
    {
        const string source = """
            local n = math.random(10, 20)
            assert(n >= 10 and n <= 20)
            assert(math.floor(2.5) == 2)

            local parts = {}
            table.insert(parts, "a")
            table.insert(parts, "b")
            assert(table.concat(parts, ",") == "a,b")

            assert(string.format("%d-%s", 7, "x") == "7-x")
            assert(("abc"):upper() == "ABC")

            local ok = pcall(error, "boom")
            assert(not ok)
            assert(tostring(12) == "12" and tonumber("12") == 12)
            assert(type(setmetatable({}, {})) == "table")
            for _, v in ipairs(parts) do assert(type(v) == "string") end
            assert(load("return 1 + 1")() == 2)
            """;

        Assert.IsTrue(await RunAsync("safe", source));
    }
}
